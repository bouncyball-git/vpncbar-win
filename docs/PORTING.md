# VpncBar → Windows Porting Plan

Port of [VpncBar](../../vpncbar) (macOS menu-bar VPN client) to Windows.
Target: **Windows 10 1809+ / Windows 11**, x64 (arm64 as a stretch goal).

The macOS app is a single-file Swift/AppKit status-bar app orchestrating two
GPL backends — a vendored, patched **vpnc** (Cisco IPSec, IKEv1+XAUTH) and a
system **openconnect** (Cisco AnyConnect SSL) — with profiles in JSON, secrets
in the Keychain, network config via a patched `vpnc-script`, and passwordless
`sudo` for the privileged parts.

---

## 1. Decisions (made up front)

| Decision | Choice | Rationale |
|---|---|---|
| UI / service language | **C# / .NET 8** (WinForms tray app + Worker-service) | NotifyIcon, named pipes, JSON, P/Invoke for CredRead/GetIfEntry2 all first-class; single-file self-contained publish ≈ "no deps" ethos of the mac app |
| vpnc backend | **Native port, winsock + Wintun** (no Cygwin), vendored in this tree with the `--log-file` patch carried over | Clean result, no cygwin1.dll; the in-tree Cygwin `#ifdef`s serve as a map of OS touch-points only |
| Privilege model | **Windows service** (`VpncBar.exe --service`; same binary as the tray app, see §3) | Installed once by the installer; tray app talks to it over a named pipe. Same UX as the mac sudoers rule: no prompt per connect |
| TUN driver | **Wintun** (signed `wintun.dll` from wintun.net) | Modern, fast, signed by WireGuard LLC — no driver-signing problem for us; used by openconnect ≥ 8.10 too, so **both backends share one driver** |
| openconnect | **Bundled, built from source** (pinned release tag, via `tools/fetch-openconnect.ps1`; see §4.2) | Windows has no Homebrew-equivalent the target audience has; "install it yourself" is not a viable UX. From-source build = reproducible, patchable, exact source provenance |
| TLS backend | **GnuTLS for both backends**, dynamically linked against one shared set of bundled DLLs | openconnect requires GnuTLS anyway → building vpnc with it costs little and enables `cert`/`hybrid` authmodes (inert in the default mac build). One DLL set = one place to ship CVE fixes |
| Session logs / Debug tab | **Service redirects each child's stdout/stderr** to the per-profile log (truncated per connect) | Backend-agnostic: no `--log-file` patch needed in openconnect (no daemonization on Windows — both backends are foreground children of the service). vpnc keeps its `--log-file` option for parity with the mac fork |

---

## 2. Component mapping

| macOS | Windows | Notes |
|---|---|---|
| `NSStatusItem` menu-bar app | `NotifyIcon` tray app | Left-click menu with profile rows, ✓ + elapsed timer, right-click → editor |
| Keychain (`security` CLI) | **Credential Manager** (`CredRead`/`CredWrite`/`CredDelete`, generic credentials) | Same key names: `vpnc-<uuid>-secret`, `vpnc-<uuid>-password` |
| `sudo` + `/etc/sudoers.d/vpncbar` | `VpncBar.exe --service` (LocalSystem) + named pipe | See §5 trust model |
| utun (`PF_SYSTEM` kernel control) | **Wintun adapter** | Created/destroyed by the service per tunnel |
| `vpnc-script` (sh; `route`, `scutil`) | **C# network-config**: `VpncBar.exe --script` shim invoked by both backends, sharing code with the service | See §6 |
| Scoped DNS via `State:/Network/Service/<utun>/DNS` | **NRPT rules** (Name Resolution Policy Table) | Windows' native split DNS; arguably cleaner than the macOS mechanism |
| `UNUserNotificationCenter` | `NotifyIcon.ShowBalloonTip` (rendered as toast on Win10+) | Connect/disconnect notifications |
| `ps` + pidfiles | Service owns its child processes directly | Simpler & more robust than process-list parsing; tray queries tunnel state over the pipe |
| `netstat -ib` traffic counters | `GetIfEntry2` / .NET `NetworkInterface.GetIPStatistics()` | Info tab |
| `cisco-decrypt` binary (.pcf import) | **Reimplemented in C#** (~60 lines: SHA-1 + 3DES-CBC) | Avoids shipping a libgcrypt-linked exe just for import |
| `--log-file` + Debug-tab tail | Same (the `--log-file` patch carries over unchanged) | Tray tails the per-profile log file |
| SIGTERM/SIGINT → disconnect-all | Service handles `SERVICE_CONTROL_PRESHUTDOWN`/stop; tray disconnect-all on exit is a pipe call | Tunnels never orphaned by logoff/shutdown |
| `.pkg` installer / `install.sh` | **Inno Setup** installer (app + service + binaries) | MSIX not suitable (service + driver-dll) |

**Profile portability:** the Windows app keeps the exact `profiles.json` schema
(same field names, same `uuid` identity). A profile file moved between the mac
and Windows apps just works; secrets are re-entered (Keychain ↔ Credential
Manager don't transfer).

---

## 3. Architecture

**One executable, multiple modes** (the WireGuard-for-Windows pattern —
`Program.cs` dispatches on argv; tray UI, service, and script shim share one
assembly, one profile model, one pipe protocol):

```
VpncBar.exe                         → tray app (default; per-user, unprivileged)
VpncBar.exe --service               → Windows service (LocalSystem, Session 0)
VpncBar.exe --script                → network-config shim invoked by the backends
VpncBar.exe --install/--uninstall-service → used by the installer
```

> Why not literally one process: services live in **Session 0** (isolated
> since Vista; the UI0Detect desktop bridge was removed in Win10 1803), which
> has no desktop or notification area — and sessions are per-user, so a
> service-owned tray icon is structurally impossible (whose session would it
> appear in under fast-user-switching/RDP?). Hence: privileged engine in
> Session 0, per-session tray instance, same binary.

```
┌────────────────────────────┐        ┌─────────────────────────────────┐
│ VpncBar.exe (tray, user)   │        │ VpncBar.exe --service (SYSTEM)  │
│  - tray menu / editor UI   │  named │  - tunnel manager (per-profile) │
│  - profiles.json (APPDATA) │  pipe  │  - spawns vpnc.exe /            │
│  - Credential Manager      │ ─────► │    openconnect.exe as children  │
│  - .pcf/.conf import       │        │  - Wintun adapter lifecycle     │
│  - notifications, log tail │        │  - routes (never default route) │
└────────────────────────────┘        │  - NRPT split-DNS rules         │
                                      │  - .info file (--script mode,   │
                                      │    runs inside its proc tree)   │
                                      │  - teardown on stop/preshutdown │
                                      └─────────────────────────────────┘
```

The pipe is an internal detail between two instances of the same binary —
tray/service protocol drift is impossible since they always ship together.
The `--script` child spawned by openconnect/vpnc already runs elevated
(inside the service's process tree), so it applies routes/NRPT/`.info`
directly via the same shared classes the service uses.

### Project layout

```
vpncbar-win/
├── src/                     # single C# project: tray + service + script modes
├── vendor/
│   ├── vpnc/                # vendored vpnc fork (from the mac repo) + Windows port
│   ├── openconnect/         # bundled binaries + dependency DLLs + source provenance
│   ├── wintun/              # wintun.dll (x64/arm64) + LICENSE
│   └── NOTICE               # provenance + licenses + local modifications (mirrors mac repo)
├── installer/               # Inno Setup script
├── build.ps1                # one-script build: deps → vpnc → app → installer
└── docs/PORTING.md          # this file
```

### Secrets flow (important)

Credential Manager generic credentials are **per-user**; the LocalSystem
service cannot read them. Therefore:

- The **tray app** reads secrets and assembles the full vpnc config / the
  openconnect stdin payload, and sends it over the pipe per connect request.
- The **service never persists secrets**; they live only in the spawned
  child's stdin pipe (exactly like the mac app pipes config via stdin so
  secrets never appear in argv).

### Pipe protocol & trust model

- Named pipe `\\.\pipe\vpncbar`, JSON messages:
  `connect{uuid, config|args, stdin}`, `disconnect{uuid}`, `disconnect-all`,
  `status` → `[{uuid, pid, since, iface}]`, `sweep`.
- Pipe ACL: `Authenticated Users` read/write (any interactive user may operate
  tunnels — same threat model as the mac NOPASSWD sudoers rule, where any
  process of the user could run vpnc).
- The service **only executes its own installed binaries at fixed paths**
  (`C:\Program Files\VpncBar\bin\vpnc.exe`, `...\openconnect.exe`) — clients
  choose profiles and options, never executable paths. Argv is built
  service-side from a validated allow-list of options (mirrors the sudoers
  rule pinning exact binary paths).

---

## 4. Backend bundling & licensing  ⟵ key topic

### 4.1 vpnc — built by us, fully bundled, **with GnuTLS**

- Built from `vendor/vpnc` with **mingw-w64** (MSYS2), by the same
  from-source toolchain as openconnect (`tools/build-vpnc.ps1`; §4.2).
- **Linked against GnuTLS** (not `CRYPTO_NONE` like the default mac build):
  since openconnect drags GnuTLS in anyway, vpnc shares the same bundled
  DLLs and gains working `cert`/`hybrid` IKE authmodes — a feature upgrade
  over macOS, where those fields are inert by default. libgcrypt +
  libgpg-error (vpnc's core IKE crypto) come from the same dependency set.
- Linking is **dynamic against the shared DLL set in `bin\`** — one copy of
  GnuTLS/nettle/gmp for both backends, one place to ship CVE fixes. (The mac
  static-link rationale — "no Homebrew/MacPorts at runtime" — doesn't apply:
  everything lives in our install dir regardless.)
- The mac app's `vpncSupportsCerts()` (`otool -L`) becomes a PE import-table
  check; with this build it always reports true, so the cert/hybrid UI is
  live from day one.
- The `--log-file` option from the mac fork **carries over** (the syslog
  half compiles out on Windows).
- `cisco-decrypt.exe` is **not** shipped — reimplemented in C# for import.
- `vpnc-disconnect` is **not** shipped — its job (signal a specific tunnel,
  verify the target, sweep stale config) moves into the service.
- License: GPLv2, GnuTLS is the GPL-compatible TLS option (OpenSSL is never
  used — the mac NOTICE documents that caveat). Obligation: full modified
  source lives in `vendor/vpnc` in this repo (same model as the mac repo).
  `vendor/NOTICE` lists our Windows modifications.

### 4.2 openconnect — bundled (decision + options considered)

On macOS, openconnect was deliberately *not* bundled because Homebrew/MacPorts
make installation a one-liner and auto-detection is easy. **Neither holds on
Windows**: there is no package manager the target user reliably has, MSYS2 is
a heavy developer environment, and the only end-user channel (openconnect-gui
installer) means installing a competing GUI to scavenge its CLI. So:

**Decision: bundle `openconnect.exe` + its dependency DLLs, built from
source** (MSYS2/mingw-w64, pinned openconnect release tag, via
`tools/fetch-openconnect.ps1`). Pinned, reproducible, patchable if Windows
quirks surface; the exact source provenance the LGPL requires is satisfied by
recording the tag + dependency package versions (`vendor/openconnect/
VERSIONS.txt`) and mirroring the source tarball in `vendor/openconnect/src/`.

Options that were considered and rejected:

| Option | Why rejected |
|---|---|
| Repackage MSYS2 `mingw-w64-openconnect` + DLL closure | Version drift with MSYS2's rolling packages; no patch leverage; source mirroring is fiddlier. |
| Don't bundle, detect a system install | No realistic install channel on Windows |
| Require/download openconnect-gui's copy | Fragile, not redistributable cleanly |

The same toolchain builds **vpnc** (§4.1, `tools/build-vpnc.ps1`) against the
same dependency set, so both backends share one set of GnuTLS/nettle/gmp/
libxml2 DLLs in `bin\`.

**No log patch needed:** upstream openconnect has no `--log-file` option, and
we don't add one. On Windows both backends run as *foreground children of
the service*, which redirects each child's stdout/stderr to the per-profile
log (`%ProgramData%\VpncBar\run\<uuid>_<name>.log`, truncated per connect).
The mac app needed vpnc's `--log-file` only because vpnc daemonizes there —
no daemonization on Windows, so plain fd redirection covers both backends
uniformly and the Debug tab just tails the file either way. Keeping the
openconnect build patch-free is exactly what makes a version pin-bump cheap.

License obligations of the openconnect stack:

| Component | License | Obligation when bundling |
|---|---|---|
| openconnect | LGPL-2.1 | Provide source for the exact binary; allow relinking (we ship it as a separate exe — satisfied) |
| GnuTLS | LGPL-2.1+ | Same |
| nettle / gmp | LGPL/GPL dual | Same |
| libxml2, zlib | MIT / zlib | Attribution in NOTICE |

All compatible with this project's GPL-family licensing; everything is listed
in `vendor/NOTICE` with versions and source pointers, mirroring the mac repo's
discipline.

openconnect-on-Windows specifics to verify early (phase 3 spike):

- **Wintun use**: openconnect ≥ 8.10 prefers Wintun when `wintun.dll` is
  loadable — place our `wintun.dll` next to `openconnect.exe`. Confirm
  adapter naming/ownership so the service can correlate the adapter for
  routes/NRPT/counters (`-i VpncBar-<name>` style).
- **`--script` semantics on Windows**: openconnect runs `.js` scripts via
  `cscript.exe`; a non-`.js` command line is spawned directly with the
  `VPNGATEWAY`/`INTERNAL_IP4_*`/`CISCO_*` variables in the environment.
  We point it at our `VpncBar.exe --script` shim (see §6). Verify env-var
  delivery and quoting on real Windows openconnect before building on it.
- `--background` does not exist meaningfully on Windows — the service keeps
  openconnect as a foreground child instead (better for lifecycle anyway).

### 4.3 Wintun — bundled signed binary

- Ship the **prebuilt, signed `wintun.dll`** from wintun.net (per-arch). We
  cannot realistically sign a driver ourselves (EV cert + WHQL attestation);
  the prebuilt dll embeds the signed driver and is the intended distribution.
- License: Wintun source is GPLv2; the prebuilt binaries ship under
  WireGuard LLC's "Prebuilt Binaries License" permitting redistribution as
  part of a larger product. **Action item: copy the exact license text into
  `vendor/wintun/LICENSE` at packaging time and confirm terms.**
- The dll is loaded by both `vpnc.exe` (our port) and `openconnect.exe`.

---

## 5. Privileged service design

- .NET 8 Worker Service, runs as **LocalSystem** (needed for adapter
  creation, route table, NRPT registry writes), `Start=Automatic`.
- **Tunnel manager**: one child process per connected profile, keyed by
  profile uuid. State is authoritative in the service (no `ps` parsing, no
  pidfiles needed for correlation — though vpnc keeps writing one for parity).
- **Teardown paths** (all converge on the same routine: stop child → remove
  routes → remove NRPT rules → delete `.info` → destroy adapter):
  - pipe `disconnect{uuid}` / `disconnect-all`
  - child exited unexpectedly (drop detected → cleanup + notify tray via pipe event)
  - service stop / `SERVICE_CONTROL_PRESHUTDOWN`
- **Sweep** on tunnel close (parity with `vpnc-disconnect sweep`): remove
  orphaned NRPT rules / routes / adapters tagged with our naming prefix.
- Logs: service log to `%ProgramData%\VpncBar\service.log`; per-tunnel
  session logs to `%ProgramData%\VpncBar\run\<uuid>_<name>.log`, produced by
  the service **redirecting each child's stdout/stderr** (truncated per
  connect; world-readable so the tray Debug tab can tail them). Identical
  mechanism for both backends — the Debug-tab contract is "the service
  guarantees this file receives the session", not a backend option.

---

## 6. Network configuration (replaces vpnc-script)

The mac repo patches `vpnc-script` with two policies; both carry over:

1. **Never touch the default route.** Only split-include routes from the
   gateway (`CISCO_SPLIT_INC_*`) are installed — via `CreateIpForwardEntry2`
   bound to the tunnel adapter's interface index. Full-tunnel gateways
   coexist instead of fighting over the default route.
2. **Split DNS only, scoped per tunnel.** Implemented with **NRPT**: one rule
   per match domain (`CISCO_DEF_DOMAIN` + `CISCO_SPLIT_DNS` + the profile's
   *VPN domains* field) pointing at `INTERNAL_IP4_DNS`. If no match domain is
   known, that tunnel's DNS is **skipped**, never applied globally. Rules are
   tagged (comment/GUID convention) so sweep can find orphans.
   - NRPT via the `DnsPolicyConfig` registry keys + `Get/Add-DnsClientNrptRule`
     equivalent P/Invoke or PowerShell fallback; flush resolver cache after.

**Who runs it:**

- For **vpnc**: no external script at all. Our native port gains a
  `--config-env-dump` style hand-off? No — simpler: keep vpnc's existing
  "run the Script with env vars" model, pointing it at `VpncBar.exe --script`
  (below), so the C engine stays close to upstream.
- For **openconnect**: `--script "VpncBar.exe --script"` (non-`.js` →
  spawned directly with the env).

`--script` mode is a thin entry point in the same binary that reads the
standard vpnc-script environment (`reason`, `TUNDEV`, `VPNGATEWAY`,
`INTERNAL_IP4_*`, `CISCO_*`, `VPNPID`, `VPNC_MATCH_DOMAINS`) and — since it
already runs elevated as a child of the service's process tree — applies the
config directly via the same shared classes the service uses (no pipe round
trip needed). It also writes the per-tunnel **`.info` file**
(`%ProgramData%\VpncBar\run\<uuid>.info`) with the same keys the mac app
reads (`TUNDEV=`, `INTERNAL_IP4_ADDRESS=`, `ROUTE=`…) so the Info tab logic
ports unchanged.

Gateway-DNS hardening carries over: resolve the gateway hostname to an IP
**before** connect (tray-side, with last-good cache) so a gateway under its
own match domain stays reachable on reconnect.

---

## 7. Tray app (UI)

Feature-parity checklist against the mac app:

- [ ] Tray icon: open/closed lock states; left-click opens menu
- [ ] Menu: one row per profile — ✓ when connected, name, right-aligned
      monospaced elapsed timer ticking while open; click toggles
      connect/disconnect; right-click opens editor
- [ ] `Disconnect All` (only when ≥1 tunnel up), `Manage VPNs…`, `About`, `Quit`
- [ ] Manage window: list + Add / Edit / Remove / Import (`.pcf` / `.conf`)
- [ ] Profile editor (one window per profile): Type selector (vpnc |
      openconnect, locked after save); Credentials / Options / Info / Debug
      tabs; Connect/Disconnect button tracking live state; authmode-dependent
      field dimming; secret fields with reveal-eye
- [ ] openconnect guided setup: **Fetch groups** (credential-less probe via
      bundled openconnect, parse `<option second-auth="1">`) + 2FA auto-detect
      + OTP prompt on connect
- [ ] Info tab: status, uptime, interface, traffic in/out, internal IP,
      gateway, DNS, match domains, routes, exact command line (1 s refresh,
      only while visible)
- [ ] Debug tab: live tail of the per-profile log (~4×/s while visible),
      Clear / Reveal buttons
- [ ] Notifications on connect/disconnect (diff of live tunnel set)
- [ ] Single-instance guard (named mutex)
- [ ] **Disconnect-all on tray exit (mac parity — decided).** Exit paths:
      Quit menu → `disconnect-all` over the pipe before exit; logoff →
      `WM_QUERYENDSESSION`/`SessionEnding` → same; shutdown → same, plus the
      service's `PRESHUTDOWN` teardown as backstop; tray crash/`taskkill /f`
      → tunnels stay up (the `kill -9` exemption on mac), next tray launch
      re-syncs live state over the pipe; `net stop` on the service → service
      tears down everything in its stop handler.
- [ ] Optional: auto-start at login (HKCU Run key, toggle in About window)

Storage map:

| What | macOS | Windows |
|---|---|---|
| Profiles (no secrets) | `~/.config/vpncbar/profiles.json` | `%APPDATA%\vpncbar\profiles.json` |
| Secrets | login Keychain `vpnc-<uuid>-…` | Credential Manager generic creds, same names |
| Per-session logs | `~/.config/vpncbar/run/` | `%ProgramData%\VpncBar\run\` (service-written) |
| Live tunnel info | `/var/run/vpncbar/<uuid>.info` | `%ProgramData%\VpncBar\run\<uuid>.info` |
| Binaries | `/opt/vpncbar/` | `C:\Program Files\VpncBar\bin\` |
| sudoers rule | `/etc/sudoers.d/vpncbar` | the installed service |

---

## 8. Native vpnc port (the core engineering)

Portable as-is (no changes expected): `vpnc.c` (IKE state machine),
`isakmp-pkt.c`, `crypto*`, `dh.c`, `math_group.c`, `config.c` (incl. our
`--log-file` patch), `supp.c`, `decrypt-utils.c`.

To change:

| Area | File(s) | Plan |
|---|---|---|
| TUN device | `sysdep.c` → new `sysdep-win.c` | Wintun API: `WintunCreateAdapter` / `WintunStartSession`; `tun_read`/`tun_write` over the ring buffers (no 4-byte AF header on Windows — simpler than utun). The existing `TUN_READ`/`TUN_WRITE` dispatch macros in `tunip.c` are the hook points. Adapter name from a new `--ifname` option so the service/script can correlate. |
| Main event loop | `tunip.c` `vpnc_main_loop` | `select()` on {UDP socket, tun fd} → `WaitForMultipleObjects` on {`WSAEventSelect` event, `WintunWaitForReadEvent` handle}. Keepalive/DPD timers via the wait timeout, as now. **Highest-risk change — NAT-T encapsulation, keepalives and the packet pump all meet here.** |
| Sockets | `tunip.c`, `vpnc.c` | winsock2 init, `closesocket`, `WSAGetLastError` shims; sockets are already plain UDP — minimal diffs |
| Daemonize | `tunip.c` | Delete (`fork` path compiled out); the service keeps vpnc as a foreground child. `--pid-file` retained but informational |
| Signals / teardown | `tunip.c`, `vpnc.c` | SIGTERM → a named event (`Global\vpncbar-<uuid>-stop`) the service signals; on signal run the normal teardown (script `disconnect` reason) and exit |
| syslog | `config.c`, `tunip.c` | Compiled out on Windows; `--log-file` (already in our fork) is the sole sink |
| Privilege drop / `setuid` | `tunip.c` | No-op on Windows |
| `get_current_dir_name`, misc glibc-isms | `tunip.c`, `sysdep.h` | mingw shims (some already exist for the mac port) |

Toolchain: **MSYS2 / mingw-w64**, driven by `build.ps1` (mirrors `build.sh`:
`deps` → static libgpg-error+libgcrypt → `vpnc` → `app` → `installer`).
The existing `__CYGWIN__` blocks in `sysdep.c` are kept as reference but the
port targets plain win32 (`_WIN32`), not Cygwin.

---

## 9. Phases

1. **Scaffold** — solution + projects, tray shell with menu, profile model
   (mac-compatible JSON), Credential Manager wrapper, `.pcf`/`.conf` import
   with C# cisco-decrypt, Manage/editor windows (fields only, no connect).
2. **Service + pipe + installer** — service skeleton, pipe protocol,
   connect/disconnect round-trip with a **stub backend** (e.g. a sleep
   process), teardown-on-stop, Inno Setup installing app+service.
   *Exit criterion: click row → service spawns child → ✓ + timer in menu.*
3. **openconnect backend** — build openconnect from source (MSYS2, pinned
   tag + GnuTLS dependency set, `tools/fetch-openconnect.ps1`); spike the
   `--script` env-delivery and Wintun-adapter questions (§4.2); implement
   routes + NRPT + `.info` in `--script` mode/service; Fetch groups + OTP
   flow. *First real tunnels.*
4. **vpnc native port** — §8, built with GnuTLS by the same toolchain
   (`tools/build-vpnc.ps1`), against a real IKEv1 gateway; Info/Debug tabs
   wired to counters and logs.
5. **Polish & release** — notifications, Disconnect All, sweep, single
   instance, uninstaller (tear down tunnels first, keep profiles+creds),
   NOTICE/licensing audit, signed installer if a cert is available, README.

Phases 3 and 4 are independent of each other once 2 lands; vpnc can start
early if the gateway for testing is IPSec-only.

## 10. Risks / verify-early

- **openconnect `--script` on Windows** — exact spawn/env/quoting semantics;
  spike in phase 3 before depending on the shim design. Fallback: patched
  `vpnc-script-win.js` (JScript) like upstream uses.
- **`tunip.c` main-loop rework** — highest-risk code change; test DPD,
  NAT-T keepalives, rekey, and large-transfer stability early.
- **NRPT interactions** — NRPT is machine-wide; verify behavior alongside
  corporate group policy (GPO-managed NRPT can shadow local rules) and VPN
  clients that also write NRPT.
- **Wintun prebuilt-binaries license** — copy exact text, confirm
  redistribution terms at packaging time.
- **LocalSystem + per-user notifications/UI** — all UI stays in the tray app
  (session-aware); service must never attempt UI.
- **Defender/SmartScreen** — unsigned installer + unsigned new exes spawning
  VPN tunnels may trip reputation heuristics; plan for code-signing.
- **arm64** — Wintun ships arm64; MSYS2 mingw arm64 toolchain is newer; treat
  as stretch.

## 11. Open questions

(none currently)

Resolved:

- ~~Tray-app exit~~ → tear down all tunnels (mac parity); exact per-exit-path
  semantics in the §7 checklist
- ~~openconnect bundling source~~ → built from source, pinned tag, via tools/ scripts (§4.2)
- ~~vpnc TLS backend~~ → GnuTLS for both backends, shared DLLs; cert/hybrid
  live from day one (§4.1)
- ~~openconnect `--log-file` patch~~ → not needed; service-side stdout/stderr
  redirection covers both backends (§4.2)

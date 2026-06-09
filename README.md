# VpncBar for Windows

A native Windows system-tray front-end for **two** VPN backends:

- **`vpnc`** — Cisco IPSec (IKEv1 + XAUTH). A from-source Windows port of the
  vpnc engine that opens a **Wintun** adapter natively (no third-party kext or
  TAP install) and links GnuTLS for certificate auth.
- **`openconnect`** — Cisco **AnyConnect** SSL (and compatible). Built from
  source, bundled, with a guided setup that fetches the gateway's group list
  and detects per-group 2FA.

It's the Windows counterpart of the macOS [VpncBar](../vpncbar): the same
profile schema, the same lean "everything in one place" ethos, a dark-mode
WinForms UI, and a privileged background service that owns the tunnels.

## Contents

- [How it's put together](#how-its-put-together)
- [Features](#features)
- [Requirements](#requirements)
- [Install](#install)
- [Build from source](#build-from-source)
- [Usage](#usage)
- [Where things are stored](#where-things-are-stored)
- [Uninstall](#uninstall)
- [Licensing](#licensing)

---

## How it's put together

One executable, three modes (the WireGuard-for-Windows pattern):

```
VpncBar.exe                  → tray app (per-user, unprivileged)
VpncBar.exe --service        → Windows service (LocalSystem, Session 0)
VpncBar.exe --script         → network-config shim the backends invoke
VpncBar.exe --install-service / --uninstall-service
```

A service cannot own windows or a tray icon (Session 0 isolation), so the
privileged engine and the UI are separate processes of the **same binary**,
talking over a named pipe (`\\.\pipe\vpncbar`):

```
┌────────────────────────────┐        ┌─────────────────────────────────┐
│ VpncBar.exe (tray, user)   │  named │ VpncBar.exe --service (SYSTEM)  │
│  - tray menu / editor UI   │  pipe  │  - tunnel manager (per-profile) │
│  - profiles.json (APPDATA) │ ─────► │  - spawns vpnc.exe /            │
│  - Credential Manager      │        │    openconnect.exe as children  │
│  - .pcf/.conf import       │        │  - Wintun adapter lifecycle     │
│  - notifications, log tail │        │  - routes (never default route) │
└────────────────────────────┘        │  - NRPT split-DNS rules         │
                                       └─────────────────────────────────┘
```

**Service lifetime is tied to the tray.** The tray starts the (demand-start)
service on launch and registers ownership; the service watches the tray's
process and tears everything down when it exits — clean quit, logoff, or crash
alike. It does not run at boot.

Secrets never reach the service: the tray reads them from the Credential
Manager and pipes the full config/credentials to the backend child on stdin,
so they appear in neither the service's state nor any argv.

| Component | What it is |
|-----------|------------|
| `src/` | The C# / WinForms app (tray + service + script modes), .NET 10 |
| `vendor/vpnc/` | The vpnc engine, ported to Windows (Wintun + winsock); GPLv2. See `vendor/NOTICE` |
| `vendor/openconnect/` | openconnect built from source; the pinned source tarball is fetched at build time (not committed), LGPL |
| `vendor/wintun/` | The signed `wintun.dll` from wintun.net, fetched at build time (not committed); shared by both backends |

> **vpnc on Windows binds a non-default NAT-T source port** (`--local-port 0`).
> Windows' kernel IPsec otherwise intercepts the gateway's inbound ESP-in-UDP on
> port 4500, finds no kernel SA (vpnc does ESP in user space), and drops it — so
> the tunnel connects but passes no data. A random high local port sidesteps the
> kernel's port-4500 demux, so the reply reaches vpnc's socket. (macOS has no such
> demux and needs none of this.)

## Features

- **Tray menu is the UI** — one row per profile with a ✓ when connected and a
  live, right-aligned elapsed timer; click to connect/disconnect, right-click
  to edit. **Disconnect All**, **Manage VPNs…**, **About**, **Quit**.
- **Multiple simultaneous tunnels** — one backend process per profile; split
  routes from every gateway coexist and the **system default route is never
  touched**.
- **Profile editor** — Credentials / Options / Info / Debug tabs; a Type
  selector (vpnc | openconnect, locked after save) swaps the field set;
  authmode-dependent field dimming; in-field reveal-eye for secrets.
- **openconnect guided setup** — **Fetch groups** (a credential-less probe)
  fills the editable Auth-group **dropdown** and tags each group's 2FA need; the
  OTP prompt then appears on connect only for the group you pick. If the gateway's
  certificate isn't trusted (self-signed / private CA), the probe offers to **pin
  it** (trust-on-first-use), enforced on every connect thereafter.
- **Info tab** — live status, uptime, interface, traffic in/out, internal IP,
  gateway, DNS, match domains, routes, and the exact command line.
- **Debug tab** — tails the per-profile session log live, with Clear / Reveal.
- **Split DNS** via **NRPT** — scoped per tunnel to the gateway's domains plus
  the profile's VPN domains; your normal DNS keeps working. No global DNS
  takeover; if no match domain is known, that tunnel's DNS is skipped.
- **Secrets in the Credential Manager** — keyed by a stable per-profile UUID,
  so renaming a profile never loses or duplicates its secrets.
- **Config import** — Cisco `.pcf` or vpnc `.conf`; obfuscated `enc_GroupPwd` /
  `enc_UserPassword` values are decoded (a C# port of `cisco-decrypt`).
- **Notifications** on connect/disconnect; **dark/light** theme following the
  system; **start at login** toggle (in **Manage VPNs…**, lower-right).

## Requirements

- **Windows 10 1809+ / Windows 11**, x64.
- The **.NET 10 Desktop Runtime** (a small, free Microsoft download). The
  installer detects it and offers to **download + install it automatically** if
  it's missing. The backends + Wintun ship with VpncBar, so that's the only
  external prerequisite.

## Install

Run the installer (`VpncBar-<version>-setup.exe`) — it lays the app out under
`C:\Program Files\VpncBar`, registers the service, and offers a "start at
login" option. Admin rights are required once (for the service + driver). If
the **.NET 10 Desktop Runtime** isn't present, the installer offers to download
and install it for you. **Upgrading over a running copy works** — the installer
stops the tunnels, tray, and service first, then replaces the files.

> The installer is currently **unsigned**; SmartScreen may warn on first run
> ("More info" → "Run anyway"). Code-signing is planned.

## Build from source

Prerequisites: **.NET 10 SDK** and (for the installer) **Inno Setup 6**. The
MSYS2/mingw64 build toolchain is provisioned into `vendor\msys64` by
`setup-msys.ps1` — no global install needed (a global `C:\msys64` is used as a
fallback if you have one).

```powershell
# everything A→Z in one command (provisions toolchain, builds both backends,
# publishes the app, runs Inno Setup) → dist\setup\VpncBar-<ver>-setup.exe.
# Re-runs reuse already-built backends; pass -Force for a full from-scratch rebuild.
.\scripts\build-all.ps1

# …or run the stages individually:
.\scripts\setup-msys.ps1           # MSYS2 + mingw toolchain  → vendor\msys64  (~2-3 GB)
.\scripts\fetch-wintun.ps1         # signed wintun.dll        → vendor\wintun
.\scripts\build-openconnect.ps1    # build openconnect        → dist\backend
.\scripts\build-vpnc.ps1           # build vpnc.exe           → dist\backend
.\scripts\publish-app.ps1          # framework-dependent app  → dist\app
.\scripts\build-installer.ps1      # installer                → dist\setup\VpncBar-<ver>-setup.exe
```

All build/helper scripts live in the `scripts\` folder: `build-all.ps1`, `setup-msys.ps1`,
`fetch-wintun.ps1`, `build-openconnect.ps1`, `build-vpnc.ps1`, `build-app.ps1`,
`publish-app.ps1`, `build-installer.ps1`, `install-dev.ps1`, `uninstall-dev.ps1`,
`make-icon.ps1`, `dns-info.ps1` (a read-only DNS / split-DNS diagnostic — also
installed with the app, with a Start Menu **"DNS Info"** shortcut), and `clean.ps1`
(`clean.ps1 -All` resets the backends + toolchain to a from-scratch state). The
backend/fetch scripts skip work that's already done; pass `-Force` to redo it.

### Run from source (without building the installer)

Once the backends exist in `dist\backend` (one `.\scripts\build-all.ps1`, or the
two `build-*` backend scripts), install the dev build straight from `src\bin`:

```powershell
.\scripts\install-dev.ps1              # build (Debug), register the service, launch the tray
.\scripts\install-dev.ps1 -Release     # use the Release build instead
.\scripts\uninstall-dev.ps1            # stop + deregister the service, remove installed files
```

`install-dev` builds the app, registers the Windows service against the local
exe (the one step that self-elevates for UAC), and launches the tray — the
from-source equivalent of running the real installer. Re-run it after a code
change to re-point the service at the fresh build.

`uninstall-dev` stops the tray, deregisters the service, and removes any
installed program files — it then **asks separately** whether to also delete
your saved profiles and stored credentials (both kept by default). It never
touches the build in `src\bin` or the session logs in `%ProgramData%\VpncBar`.

`.\scripts\build-app.ps1` alone just builds (`dotnet build src` works too); each
backend script records its exact source tag + dependency versions
(`vendor\openconnect\VERSIONS.txt`) for reproducible backend builds.

## Usage

1. Tray icon → **Manage VPNs…** → **Add** (or **Import…** a `.pcf`/`.conf`).
2. Pick the **Type** — **vpnc** (Cisco IPSec) or **openconnect** (AnyConnect);
   for openconnect use **Fetch groups** to fill the group dropdown and detect 2FA.
3. **Left-click** a profile row to connect; click again to disconnect.
4. **Right-click** a row to edit it (Credentials / Options / Info / Debug).
5. **Manage VPNs…** has the **start-at-login** toggle (lower-right); **About**
   shows the version and a link to the project on GitHub.

## Where things are stored

| What | Where |
|------|-------|
| Profiles (no secrets) | `%APPDATA%\vpncbar\profiles.json` |
| Secrets | Windows **Credential Manager**, `vpnc-<uuid>-secret` / `…-password` |
| Per-session logs + live tunnel info | `%ProgramData%\VpncBar\run\` |
| Service log | `%ProgramData%\VpncBar\service.log` |
| Binaries | `C:\Program Files\VpncBar\` |

The `profiles.json` schema is identical to the macOS app's — a profile file
moves between the two (secrets are re-entered).

## Uninstall

Uninstall via **"VpncBar" in *Apps & features*** (or `unins000.exe` in the install
folder). It stops the tunnels + service and removes the program, then asks
**separately** whether to also delete your saved profiles and stored credentials —
both are **kept** by default. Session logs in `%ProgramData%\VpncBar` are left in
place; delete that folder too for a full wipe.

## Licensing

The application (`src/`) is under this repository's
[`LICENSE`](LICENSE). The bundled backends are GPL/LGPL: **vpnc** is GPLv2
(full modified source in `vendor/vpnc`), **openconnect** and its GnuTLS stack
are LGPL, and **Wintun** ships under WireGuard LLC's prebuilt-binaries license.
Provenance, versions, and the complete list of local modifications are in
[`vendor/NOTICE`](vendor/NOTICE).

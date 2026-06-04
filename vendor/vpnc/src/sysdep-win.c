/*
   Native Windows tunnel + shims for vpnc — VpncBar port.

   The tunnel interface is a Wintun adapter (wintun.dll loaded from the
   directory of the executable, the signed driver from wintun.net) — the
   Windows analog of the macOS native-utun port this tree carries in
   sysdep.c. tun_read() blocks on Wintun's read event (the reader runs in
   its own thread, like the Cygwin port did), tun_write() posts to the
   send ring. The integer "fd" the rest of vpnc passes around is a dummy:
   all state lives in this file.

   SPDX-License-Identifier: GPL-2.0-or-later
*/
#if defined(_WIN32) && !defined(__CYGWIN__)

#include "sysdep.h"
#include "config.h"

#include <iphlpapi.h>
#include <netioapi.h>
#include <stdarg.h>
#include <time.h>
#include <wintun.h>

#define TUN_DUMMY_FD 0x7e57

/* winsock must be initialized before any socket call; a constructor keeps
   the upstream main() untouched. */
static void __attribute__((constructor)) winsock_init(void)
{
	WSADATA wsa;
	WSAStartup(MAKEWORD(2, 2), &wsa);
}

/* ---- wintun.dll, loaded from the exe's directory ---- */
static WINTUN_CREATE_ADAPTER_FUNC *WintunCreateAdapter;
static WINTUN_CLOSE_ADAPTER_FUNC *WintunCloseAdapter;
static WINTUN_GET_ADAPTER_LUID_FUNC *WintunGetAdapterLUID;
static WINTUN_START_SESSION_FUNC *WintunStartSession;
static WINTUN_END_SESSION_FUNC *WintunEndSession;
static WINTUN_GET_READ_WAIT_EVENT_FUNC *WintunGetReadWaitEvent;
static WINTUN_RECEIVE_PACKET_FUNC *WintunReceivePacket;
static WINTUN_RELEASE_RECEIVE_PACKET_FUNC *WintunReleaseReceivePacket;
static WINTUN_ALLOCATE_SEND_PACKET_FUNC *WintunAllocateSendPacket;
static WINTUN_SEND_PACKET_FUNC *WintunSendPacket;

static WINTUN_ADAPTER_HANDLE wt_adapter;
static WINTUN_SESSION_HANDLE wt_session;
HANDLE win32_tun_abort_event;

static int load_wintun(void)
{
	wchar_t path[MAX_PATH];
	HMODULE mod;
	DWORD n = GetModuleFileNameW(NULL, path, MAX_PATH);
	while (n > 0 && path[n - 1] != L'\\')
		n--;
	wcscpy(path + n, L"wintun.dll");
	mod = LoadLibraryW(path);
	if (!mod)
		mod = LoadLibraryW(L"wintun.dll");   /* fall back to the search path */
	if (!mod) {
		logmsg(LOG_ERR, "cannot load wintun.dll (GetLastError=%lu)", GetLastError());
		return -1;
	}
#define X(name) ((*(FARPROC *)&name = GetProcAddress(mod, #name)) == NULL)
	if (X(WintunCreateAdapter) || X(WintunCloseAdapter) || X(WintunGetAdapterLUID) ||
	    X(WintunStartSession) || X(WintunEndSession) || X(WintunGetReadWaitEvent) ||
	    X(WintunReceivePacket) || X(WintunReleaseReceivePacket) ||
	    X(WintunAllocateSendPacket) || X(WintunSendPacket)) {
		logmsg(LOG_ERR, "wintun.dll is missing required exports");
		return -1;
	}
#undef X
	return 0;
}

/* Create the Wintun adapter and start a session. `dev` carries the desired
   adapter name in (may be empty) and the actual name out (vpnc puts it in
   $TUNDEV). The interface index is exported as $TUNIDX for the config
   script, matching what openconnect does on Windows. */
int tun_open(char *dev, enum if_mode_enum mode)
{
	wchar_t wname[IFNAMSIZ];
	NET_LUID luid;
	NET_IFINDEX ifidx;
	char buf[16];

	if (mode != IF_MODE_TUN) {
		logmsg(LOG_ERR, "only tun mode is supported on Windows");
		return -1;
	}
	if (load_wintun() < 0)
		return -1;

	if (dev == NULL || *dev == 0)
		strcpy(dev, "vpncbar");
	MultiByteToWideChar(CP_UTF8, 0, dev, -1, wname, IFNAMSIZ);

	wt_adapter = WintunCreateAdapter(wname, L"VpncBar", NULL);
	if (!wt_adapter) {
		logmsg(LOG_ERR, "WintunCreateAdapter failed (GetLastError=%lu)", GetLastError());
		return -1;
	}
	wt_session = WintunStartSession(wt_adapter, 0x400000 /* 4 MiB rings */);
	if (!wt_session) {
		logmsg(LOG_ERR, "WintunStartSession failed (GetLastError=%lu)", GetLastError());
		WintunCloseAdapter(wt_adapter);
		wt_adapter = NULL;
		return -1;
	}

	win32_tun_abort_event = CreateEventW(NULL, TRUE /* manual reset */, FALSE, NULL);

	WintunGetAdapterLUID(wt_adapter, (NET_LUID *)&luid);
	if (ConvertInterfaceLuidToIndex((NET_LUID *)&luid, &ifidx) == NO_ERROR) {
		snprintf(buf, sizeof(buf), "%lu", (unsigned long)ifidx);
		setenv("TUNIDX", buf, 1);
	}

	logmsg(LOG_NOTICE, "wintun adapter '%s' up", dev);
	return TUN_DUMMY_FD;
}

int tun_close(int fd, char *dev)
{
	(void)fd;
	(void)dev;
	if (wt_session) {
		WintunEndSession(wt_session);
		wt_session = NULL;
	}
	if (wt_adapter) {
		WintunCloseAdapter(wt_adapter);
		wt_adapter = NULL;
	}
	return 0;
}

/* Blocking read: wait for a packet or the abort event (set on shutdown so
   the reader thread can exit). Returns -1 on abort/error. */
int tun_read(int fd, unsigned char *buf, int len)
{
	(void)fd;
	for (;;) {
		DWORD size;
		BYTE *packet = WintunReceivePacket(wt_session, &size);
		if (packet) {
			if ((int)size > len)
				size = len;
			memcpy(buf, packet, size);
			WintunReleaseReceivePacket(wt_session, packet);
			return (int)size;
		}
		if (GetLastError() != ERROR_NO_MORE_ITEMS)
			return -1;
		{
			HANDLE handles[2] = { WintunGetReadWaitEvent(wt_session), win32_tun_abort_event };
			DWORD r = WaitForMultipleObjects(2, handles, FALSE, INFINITE);
			if (r != WAIT_OBJECT_0)
				return -1;   /* aborted or wait failure */
		}
	}
}

int tun_write(int fd, unsigned char *buf, int len)
{
	BYTE *packet;
	(void)fd;
	if (!wt_session)
		return -1;
	packet = WintunAllocateSendPacket(wt_session, len);
	if (!packet) {
		/* ring full — drop, like a real NIC under pressure */
		return len;
	}
	memcpy(packet, buf, len);
	WintunSendPacket(wt_session, packet);
	return len;
}

int tun_get_hwaddr(int fd, char *dev, uint8_t *hwaddr)
{
	(void)fd;
	(void)dev;
	(void)hwaddr;
	return -1;   /* tap mode is not supported on Windows */
}

/* ---- stop watcher: the VpncBar service signals a named event for a
        graceful disconnect (the SIGTERM analog) ---- */
extern int volatile do_kill;   /* tunip.c */

static DWORD WINAPI stop_watcher_thread(LPVOID param)
{
	HANDLE ev = (HANDLE)param;
	WaitForSingleObject(ev, INFINITE);
	do_kill = 15;   /* SIGTERM */
	if (win32_tun_abort_event)
		SetEvent(win32_tun_abort_event);
	win32_wake();   /* interrupt select() so teardown starts immediately */
	return 0;
}

static BOOL WINAPI console_ctrl(DWORD type)
{
	(void)type;
	do_kill = 15;
	if (win32_tun_abort_event)
		SetEvent(win32_tun_abort_event);
	win32_wake();
	return TRUE;
}

void win32_start_stop_watcher(void)
{
	const char *name = getenv("VPNCBAR_STOP_EVENT");
	if (name && *name) {
		HANDLE ev = CreateEventA(NULL, TRUE, FALSE, name);
		if (ev)
			CreateThread(NULL, 0, stop_watcher_thread, ev, 0, NULL);
	}
	SetConsoleCtrlHandler(console_ctrl, TRUE);   /* Ctrl+C in dev consoles */
}

/* ---- small shims ---- */

void win32_vsyslog(int priority, const char *format, va_list ap)
{
	/* glibc accepts %m (= strerror(errno)) — expand it here. On Windows
	   the socket errors live in WSAGetLastError(), which winsock maps
	   into errno only sometimes; prefer whichever is set. */
	char fmt[1024], msg[2048];
	const char *src = format;
	size_t o = 0;
	int err = WSAGetLastError() ? WSAGetLastError() : errno;
	while (*src && o < sizeof(fmt) - 64) {
		if (src[0] == '%' && src[1] == 'm') {
			o += snprintf(fmt + o, sizeof(fmt) - o, "error %d", err);
			src += 2;
		} else {
			fmt[o++] = *src++;
		}
	}
	fmt[o] = 0;
	vsnprintf(msg, sizeof(msg), fmt, ap);
	fprintf(stderr, "vpnc[%d]: %s\n", priority, msg);
	fflush(stderr);
}

void win32_syslog(int priority, const char *format, ...)
{
	va_list ap;
	va_start(ap, format);
	win32_vsyslog(priority, format, ap);
	va_end(ap);
}

int win32_inet_aton(const char *cp, struct in_addr *addr)
{
	unsigned long a = inet_addr(cp);
	if (a == INADDR_NONE && strcmp(cp, "255.255.255.255") != 0)
		return 0;
	addr->s_addr = a;
	return 1;
}

int uname(struct utsname *buf)
{
	memset(buf, 0, sizeof(*buf));
	strcpy(buf->sysname, "Windows");
	strcpy(buf->release, "10");
	strcpy(buf->version, "0");
	strcpy(buf->machine, "x86_64");
	gethostname(buf->nodename, sizeof(buf->nodename) - 1);
	return 0;
}

int setenv(const char *name, const char *value, int overwrite)
{
	if (!overwrite && getenv(name))
		return 0;
	return _putenv_s(name, value);
}

int unsetenv(const char *name)
{
	return _putenv_s(name, "");
}

/* glibc error(): print and optionally exit (used by config parsing) */
void error(int status, int errornum, const char *fmt, ...)
{
	va_list ap;
	fprintf(stderr, "vpnc: ");
	va_start(ap, fmt);
	vfprintf(stderr, fmt, ap);
	va_end(ap);
	if (errornum)
		fprintf(stderr, ": %s", strerror(errornum));
	fprintf(stderr, "\n");
	fflush(stderr);
	if (status)
		exit(status);
}

/* close() that handles both sockets and CRT fds: vpnc close()s its UDP
   sockets on rekey/teardown, which on winsock must be closesocket(). */
int win32_close(int fd)
{
	if (fd == TUN_DUMMY_FD)
		return 0;   /* the tunnel is closed via tun_close() */
	if (closesocket((SOCKET)fd) == 0)
		return 0;
	if (WSAGetLastError() == WSAENOTSOCK)
		return _close(fd);
	return -1;
}

/* ---- select() wake-up (loopback datagram self-pipe) ---- */
static SOCKET wake_sock = INVALID_SOCKET;
static struct sockaddr_in wake_addr;

SOCKET win32_wake_socket(void)
{
	if (wake_sock == INVALID_SOCKET) {
		int len = sizeof(wake_addr);
		wake_sock = socket(AF_INET, SOCK_DGRAM, 0);
		memset(&wake_addr, 0, sizeof(wake_addr));
		wake_addr.sin_family = AF_INET;
		wake_addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
		bind(wake_sock, (struct sockaddr *)&wake_addr, sizeof(wake_addr));
		getsockname(wake_sock, (struct sockaddr *)&wake_addr, &len);
	}
	return wake_sock;
}

void win32_wake(void)
{
	if (wake_sock != INVALID_SOCKET)
		sendto(wake_sock, "x", 1, 0, (struct sockaddr *)&wake_addr, sizeof(wake_addr));
}

/* CreateProcess-based system(): no cmd.exe quoting roulette, no console
   window, environment inherited (the script reads the CISCO and TUNDEV
   variables). */
int win32_system(const char *command)
{
	STARTUPINFOA si;
	PROCESS_INFORMATION pi;
	DWORD code = 1;
	char *cmd = _strdup(command);

	memset(&si, 0, sizeof(si));
	si.cb = sizeof(si);
	si.dwFlags = STARTF_USESHOWWINDOW;
	si.wShowWindow = SW_HIDE;

	if (!CreateProcessA(NULL, cmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi)) {
		logmsg(LOG_ERR, "script spawn failed (GetLastError=%lu): %s", GetLastError(), command);
		free(cmd);
		return -1;
	}
	free(cmd);
	WaitForSingleObject(pi.hProcess, 30000);
	GetExitCodeProcess(pi.hProcess, &code);
	CloseHandle(pi.hThread);
	CloseHandle(pi.hProcess);
	return (int)code;
}

#endif /* _WIN32 && !__CYGWIN__ */

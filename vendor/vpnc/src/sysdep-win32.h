/*
   Native Windows (mingw-w64) compatibility layer for vpnc — VpncBar port.
   Replaces the BSD/glibc networking headers and provides small shims; the
   win32/ stub-include directory routes the upstream #includes here.

   SPDX-License-Identifier: GPL-2.0-or-later
*/
#ifndef VPNC_SYSDEP_WIN32_H
#define VPNC_SYSDEP_WIN32_H
#if defined(_WIN32) && !defined(__CYGWIN__)

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX                /* keep windows.h from defining min()/max() macros */
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <sys/types.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <io.h>
#include <process.h>
#include <direct.h>

/* ---- constants the BSD headers would have provided ---- */
#ifndef IPPROTO_ESP
#define IPPROTO_ESP 50
#endif
#ifndef IPPROTO_ENCAP
#define IPPROTO_ENCAP 4
#endif
#ifndef IPVERSION
#define IPVERSION 4
#endif
#ifndef IPDEFTTL
#define IPDEFTTL 64
#endif
#ifdef IFNAMSIZ
#undef IFNAMSIZ
#endif
#define IFNAMSIZ 256

/* ---- BSD struct ip (host bit order: little-endian on all Windows) ---- */
struct ip {
	unsigned char ip_hl:4; /* header length */
	unsigned char ip_v:4;  /* version */
	unsigned char ip_tos;  /* type of service */
	unsigned short ip_len; /* total length */
	unsigned short ip_id;  /* identification */
	unsigned short ip_off; /* fragment offset field */
	unsigned char ip_ttl;  /* time to live */
	unsigned char ip_p;    /* protocol */
	unsigned short ip_sum; /* checksum */
	struct in_addr ip_src, ip_dst; /* source and dest address */
};

/* ---- syslog shim: everything goes to stderr, which the VpncBar service
        redirects into the per-profile session log ---- */
#define LOG_EMERG   0
#define LOG_ALERT   1
#define LOG_CRIT    2
#define LOG_ERR     3
#define LOG_WARNING 4
#define LOG_NOTICE  5
#define LOG_INFO    6
#define LOG_DEBUG   7
#define LOG_PID     0
#define LOG_DAEMON  0
#define LOG_PERROR  0
void win32_vsyslog(int priority, const char *format, va_list ap);
void win32_syslog(int priority, const char *format, ...);
#define vsyslog win32_vsyslog
#define syslog win32_syslog
#define openlog(ident, option, facility) ((void)0)
#define closelog() ((void)0)

/* ---- tiny POSIX-isms ---- */
#define fcntl(fd, cmd, ...) (0)        /* only ever used for FD_CLOEXEC here */
#define F_SETFD 0
#define FD_CLOEXEC 0
#ifndef SIGHUP
#define SIGHUP 1                       /* never raised on Windows; value only */
#endif
#define sleep(s) Sleep((s) * 1000)

int win32_inet_aton(const char *cp, struct in_addr *addr);
#define inet_aton win32_inet_aton

/* ---- process-exit status: win32_system() returns the code directly,
        not a wait()-encoded status ---- */
#define WIFEXITED(status) (1)
#define WEXITSTATUS(status) (status)

/* ---- poll() over a socket during the IKE handshake -> WSAPoll ----
        (struct pollfd / POLLIN come from winsock2.h) */
#define poll(fds, n, timeout) WSAPoll((fds), (n), (timeout))

#define STDIN_FILENO 0
#define CEOT 0x04   /* ^D end-of-transmission (sys/ttydefaults.h on unix) */

/* mingw's close() works on CRT fds, not SOCKETs — vpnc only ever close()s
   sockets through these helpers' callers, so map socket closes explicitly
   where patched; plain close() stays CRT for pipe/file fds. */

/* ---- struct utsname (App-version string only) ---- */
struct utsname {
	char sysname[65];
	char nodename[65];
	char release[65];
	char version[65];
	char machine[65];
};
int uname(struct utsname *buf);

/* ---- script execution: CreateProcess instead of cmd.exe quoting roulette ---- */
int win32_system(const char *command);

/* ---- stop event + tun-abort plumbing (sysdep-win.c) ---- */
extern HANDLE win32_tun_abort_event;   /* signaled to unblock the tun reader */
void win32_start_stop_watcher(void);   /* VPNCBAR_STOP_EVENT -> do_kill */

/* Loopback "self-pipe": lets the stop watcher wake the select() loop
   without disturbing the keepalive/DPD timeout cadence. */
SOCKET win32_wake_socket(void);
void win32_wake(void);

/* close() that works on sockets AND CRT fds (winsock needs closesocket) */
int win32_close(int fd);
#define close(fd) win32_close(fd)

#endif /* _WIN32 && !__CYGWIN__ */
#endif /* VPNC_SYSDEP_WIN32_H */

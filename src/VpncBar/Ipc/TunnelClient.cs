using VpncBar.Core;

namespace VpncBar.Ipc;

// Tray-side client for the service's named pipe (\\.\pipe\vpncbar).
// Phase 1 stub: the service doesn't exist yet, so there are never live
// tunnels and connect/disconnect explain themselves. The shape (status map +
// nullable error strings, the mac app's ActionResult) is what phase 2 fills in.
static class TunnelClient
{
    public static bool ServiceAvailable => false;

    // Connected profiles → connected-since, keyed by profile name.
    public static Dictionary<string, DateTime> Status(IReadOnlyList<Profile> profiles) => [];

    const string NoService =
        "The VpncBar service isn't installed yet.\n(Tunnel engine arrives in phase 2.)";

    public static string? Connect(Profile p, string? otp = null) => NoService;
    public static string? Disconnect(Profile p) => NoService;
    public static string? DisconnectAll() => null;
}

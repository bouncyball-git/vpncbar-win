namespace VpncBar;

// Privileged engine (LocalSystem, Session 0): tunnel manager, Wintun adapters,
// routes, NRPT split DNS, named-pipe server. Implemented in phase 2.
static class ServiceMode
{
    public static int Run(string[] args)
    {
        Console.Error.WriteLine("--service mode: not implemented yet (phase 2)");
        return 1;
    }
}

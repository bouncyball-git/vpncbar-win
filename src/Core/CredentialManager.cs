using System.Runtime.InteropServices;
using System.Text;

namespace VpncBar.Core;

// Windows Credential Manager (generic credentials) — the Keychain equivalent.
// Secrets are stored as UTF-8 blobs in the user's credential store, never in
// profiles.json.
static class CredentialManager
{
    const uint CRED_TYPE_GENERIC = 1;
    const uint CRED_PERSIST_LOCAL_MACHINE = 2;   // per-user store, survives logoff

    public static string? Read(string target)
    {
        if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out var pcred)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<NativeCredential>(pcred);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0) return null;
            var blob = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
            var s = Encoding.UTF8.GetString(blob);
            return s.Length == 0 ? null : s;
        }
        finally { CredFree(pcred); }
    }

    public static bool Write(string target, string account, string value)
    {
        var blob = Encoding.UTF8.GetBytes(value);
        var pblob = Marshal.AllocHGlobal(blob.Length);
        Marshal.Copy(blob, 0, pblob, blob.Length);
        var cred = new NativeCredential
        {
            Type = CRED_TYPE_GENERIC,
            TargetName = target,
            UserName = account,
            CredentialBlob = pblob,
            CredentialBlobSize = (uint)blob.Length,
            Persist = CRED_PERSIST_LOCAL_MACHINE,
        };
        try { return CredWriteW(ref cred, 0); }
        finally { Marshal.FreeHGlobal(pblob); }
    }

    public static void Delete(string target) => CredDeleteW(target, CRED_TYPE_GENERIC, 0);

    // Delete every VpncBar credential (vpnc-<uuid>-secret / -password). Used by
    // the uninstaller's "remove credentials" option. Operates on the current
    // user's store, so it must run as that user.
    public static void DeleteAll()
    {
        if (!CredEnumerateW("vpnc-*", 0, out var count, out var pCreds)) return;
        try
        {
            for (int i = 0; i < count; i++)
            {
                var pCred = Marshal.ReadIntPtr(pCreds, i * IntPtr.Size);
                var cred = Marshal.PtrToStructure<NativeCredential>(pCred);
                if (cred.TargetName != null) CredDeleteW(cred.TargetName, cred.Type, 0);
            }
        }
        finally { CredFree(pCreds); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredWriteW(ref NativeCredential credential, uint flags);

    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredEnumerateW(string? filter, int flags, out int count, out IntPtr credentials);

    [DllImport("advapi32")]
    static extern void CredFree(IntPtr buffer);
}

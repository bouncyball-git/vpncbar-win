using System.Security.Cryptography;
using System.Text;

namespace VpncBar.Core;

// Decoder for Cisco's obfuscated .pcf passwords (enc_GroupPwd / enc_UserPassword)
// and vpnc.conf "IPSec obfuscated secret" values. C# port of the mac repo's
// vendor/vpnc/src/decrypt-utils.c deobfuscate() (GPL-2.0-or-later, Maurice
// Massar et al.), replacing the cisco-decrypt binary so import needs no
// libgcrypt-linked exe.
//
// Format (hex-encoded): h1[20] | h4[20] | enc[..]
//   key = SHA1(h1 with last byte +1)[20] + SHA1(h1 with last byte +3)[0..4)
//   integrity: SHA1(enc) must equal h4
//   plaintext = 3DES-CBC-decrypt(enc, key, iv = h1[0..8)), tail-byte padding
static class CiscoDecrypt
{
    public static string? Decrypt(string hex)
    {
        var ct = HexToBytes(hex.Trim());
        if (ct == null || ct.Length < 48) return null;

        var h1 = ct.AsSpan(0, 20).ToArray();
        var h4 = ct.AsSpan(20, 20).ToArray();
        var enc = ct.AsSpan(40).ToArray();

        var ht = (byte[])h1.Clone();
        ht[19]++;
        var h2 = SHA1.HashData(ht);
        ht[19] += 2;
        var h3 = SHA1.HashData(ht);

        var key = new byte[24];
        h2.CopyTo(key, 0);
        Array.Copy(h3, 0, key, 20, 4);
        // (key parity is irrelevant — "who cares about parity anyway?")

        if (!SHA1.HashData(enc).AsSpan().SequenceEqual(h4)) return null;

        byte[] res;
        try
        {
            using var des = TripleDES.Create();
            des.Mode = CipherMode.CBC;
            des.Padding = PaddingMode.None;   // padding stripped manually below
            res = des.DecryptCbc(enc, h1.AsSpan(0, 8).ToArray(), PaddingMode.None);
        }
        catch (CryptographicException)
        {
            return null;   // derived key landed on a rejected weak-key pattern
        }

        int padding = res[^1];
        int len = res.Length - padding;
        if (len < 0) return null;
        return Encoding.UTF8.GetString(res, 0, len);
    }

    static byte[]? HexToBytes(string s)
    {
        if (s.Length == 0 || (s.Length & 1) != 0) return null;
        var bin = new byte[s.Length / 2];
        for (int i = 0; i < bin.Length; i++)
        {
            int hi = HexVal(s[i * 2]), lo = HexVal(s[i * 2 + 1]);
            if (hi < 0 || lo < 0) return null;
            bin[i] = (byte)(hi << 4 | lo);
        }
        return bin;
    }

    static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };
}

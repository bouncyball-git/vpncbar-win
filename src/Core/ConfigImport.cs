namespace VpncBar.Core;

public record ParsedConfig(Profile Profile, string? Secret, string? Password);

// Import a Cisco .pcf or a vpnc .conf file — port of the mac app's
// parseConfigFile(). Obfuscated secrets (enc_GroupPwd, enc_UserPassword,
// "IPSec obfuscated secret") are decoded with CiscoDecrypt.
static class ConfigImport
{
    public static ParsedConfig? Parse(string path)
    {
        string raw;
        try { raw = File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }

        var lines = raw.Replace("\r", "").Split('\n');

        string? Pcf(string key)
        {
            var p = key.ToLowerInvariant() + "=";
            var line = lines.FirstOrDefault(l => l.ToLowerInvariant().StartsWith(p));
            return line?[p.Length..];
        }
        string? Conf(string key)
        {
            var p = key.ToLowerInvariant() + " ";
            var line = lines.FirstOrDefault(l => l.ToLowerInvariant().StartsWith(p));
            return line?[key.Length..].Trim();
        }
        static bool Blank(string? s) => string.IsNullOrEmpty(s);

        string gateway, id, username;
        string? secret, password;

        if (lines.Any(l => l.ToLowerInvariant().StartsWith("ipsec gateway ")))
        {
            gateway = Conf("IPSec gateway") ?? "";
            id = Conf("IPSec ID") ?? "";
            username = Conf("Xauth username") ?? "";
            password = Conf("Xauth password");
            secret = Conf("IPSec secret");
            if (Blank(secret) && Conf("IPSec obfuscated secret") is string obf)
                secret = CiscoDecrypt.Decrypt(obf);
        }
        else if (lines.Any(l => l.ToLowerInvariant().StartsWith("host=")))
        {
            gateway = Pcf("Host") ?? "";
            id = Pcf("GroupName") ?? "";
            username = Pcf("Username") ?? "";
            secret = Pcf("GroupPwd");
            if (Blank(secret) && Pcf("enc_GroupPwd") is string encG)
                secret = CiscoDecrypt.Decrypt(encG);
            password = Pcf("UserPassword");
            if (Blank(password) && Pcf("enc_UserPassword") is string encU)
                password = CiscoDecrypt.Decrypt(encU);
        }
        else
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace(" ", "-");
        var profile = new Profile { Name = name, Gateway = gateway, Id = id, Username = username };
        return new ParsedConfig(profile, Blank(secret) ? null : secret, Blank(password) ? null : password);
    }
}

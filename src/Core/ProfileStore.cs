using System.Text.Json;

namespace VpncBar.Core;

// profiles.json load/save + Credential Manager secrets, mirroring the mac app:
// profiles are keyed by a stable uuid; credentials are generic Windows
// credentials named "vpnc-<uuid>-secret" / "vpnc-<uuid>-password", so renaming
// a profile is purely cosmetic and never loses or duplicates its secrets.
static class ProfileStore
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static List<Profile> Load()
    {
        try
        {
            var data = File.ReadAllText(Paths.ProfilesPath);
            return JsonSerializer.Deserialize<List<Profile>>(data) ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Save(List<Profile> list)
    {
        Directory.CreateDirectory(Paths.ConfigDir);
        File.WriteAllText(Paths.ProfilesPath, JsonSerializer.Serialize(list, JsonOpts));
    }

    // Credential name for a profile's secret/password — same scheme as the mac
    // Keychain services, keyed off the stable uuid.
    public static string CredName(Profile p, string kind) => $"vpnc-{p.Uuid ?? p.Name}-{kind}";

    public static string? Secret(Profile p) => CredentialManager.Read(CredName(p, "secret"));
    public static string? Password(Profile p) => CredentialManager.Read(CredName(p, "password"));

    // Persist a profile + (optional) secrets, keyed by uuid. A new profile gets
    // a uuid here; an edited one keeps its uuid, so this replaces it in place
    // even if the name changed.
    public static Profile Upsert(Profile p, string? secret, string? password)
    {
        p.Uuid ??= Guid.NewGuid().ToString("D").ToUpperInvariant();
        var list = Load().Where(x => x.Uuid != p.Uuid).ToList();
        list.Add(p);
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Save(list);
        if (!string.IsNullOrEmpty(secret)) CredentialManager.Write(CredName(p, "secret"), p.Id, secret);
        if (!string.IsNullOrEmpty(password)) CredentialManager.Write(CredName(p, "password"), p.Username, password);
        return p;
    }

    public static void Remove(Profile p)
    {
        Save(Load().Where(x => x.Uuid != p.Uuid).ToList());
        CredentialManager.Delete(CredName(p, "secret"));
        CredentialManager.Delete(CredName(p, "password"));
    }
}

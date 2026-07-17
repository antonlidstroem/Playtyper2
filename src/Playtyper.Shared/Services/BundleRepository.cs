using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playtyper.Shared.Services;

/// <summary>
/// v12: Load/Save now go via RemoteRepo (GitHub Contents API) instead of
/// local disk. bundlePath is a repo-relative path (e.g. repo.CustomerBundleFile
/// or repo.BundlePath(id)) — never an absolute local filesystem path.
/// </summary>
public static class BundleRepository
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<JsonObject?> LoadAsync(RemoteRepo repo, string bundlePath)
    {
        var raw = await repo.ReadFileAsync(bundlePath);
        return raw == null ? null : JsonNode.Parse(raw) as JsonObject;
    }

    public static async Task SaveAsync(RemoteRepo repo, string bundlePath, JsonObject bundle, string commitMessage)
    {
        await repo.WriteFileAsync(bundlePath, bundle.ToJsonString(Pretty), commitMessage);
    }

    /// <summary>Creates a new minimal app-bundle.json from the supplied values.</summary>
    public static JsonObject Create(
        string bundleId,
        string appId,
        string appName,
        string defaultPack,
        IEnumerable<string> packs,
        string accentColor)
    {
        var packsArray = new JsonArray();
        foreach (var p in packs) packsArray.Add(p);

        return new JsonObject
        {
            ["appId"]        = appId,
            ["appName"]      = appName,
            ["defaultPack"]  = defaultPack,
            ["packs"]        = packsArray,
            ["accentColor"]  = accentColor,
            ["storeDescription"] = new JsonObject
            {
                ["sv"] = "",
                ["en"] = ""
            }
        };
    }

    /// <summary>Extracts the packs list from an app-bundle.json node.</summary>
    public static List<string> GetPacks(JsonObject bundle)
    {
        if (bundle["packs"] is not JsonArray arr) return new();
        return arr
            .Select(n => n?.GetValue<string>())
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();
    }

    // ── License-helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returnerar licenseId från app-bundle.json, eller null om det saknas.
    /// </summary>
    public static string? GetLicenseId(JsonObject bundle) =>
        bundle["licenseId"]?.GetValue<string>();

    /// <summary>
    /// Sätter eller ersätter licenseId i app-bundle.json.
    /// Sparar INTE — anroparen ansvarar för Save.
    /// </summary>
    public static void SetLicenseId(JsonObject bundle, string licenseId) =>
        bundle["licenseId"] = licenseId;

    /// <summary>
    /// Returnerar licenseServerUrl från app-bundle.json, eller null.
    /// </summary>
    public static string? GetLicenseServerUrl(JsonObject bundle) =>
        bundle["licenseServerUrl"]?.GetValue<string>();

    /// <summary>
    /// Sätter eller tar bort licenseServerUrl i app-bundle.json.
    /// Null-värde tar bort fältet (klienten faller tillbaka på standardservern).
    /// </summary>
    public static void SetLicenseServerUrl(JsonObject bundle, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            bundle.Remove("licenseServerUrl");
        else
            bundle["licenseServerUrl"] = url;
    }
}

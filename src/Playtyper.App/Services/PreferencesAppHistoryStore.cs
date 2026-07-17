using System.Text.Json;
using Playtyper.Shared.Services;

namespace Playtyper.App.Services;

/// <summary>
/// IAppHistoryStore för Playtyper.App — Microsoft.Maui.Storage.Preferences
/// istället för SecureStorage, eftersom det här bara är icke-hemlig metadata
/// (repo-namn, typ, senast öppnad), inte ett token. Samma resonemang som
/// varför Web-varianten använder localStorage och inte sessionStorage.
/// </summary>
public sealed class PreferencesAppHistoryStore : IAppHistoryStore
{
    private const string Key = "playtyper.known-apps";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Task<IReadOnlyList<KnownApp>> ListAsync()
    {
        var raw = Preferences.Default.Get(Key, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return Task.FromResult<IReadOnlyList<KnownApp>>(Array.Empty<KnownApp>());

        var list = JsonSerializer.Deserialize<List<KnownApp>>(raw, JsonOpts) ?? new();
        return Task.FromResult<IReadOnlyList<KnownApp>>(list.OrderByDescending(a => a.LastUsedUtc).ToList());
    }

    public async Task RecordAsync(string ownerRepo, string type, string? appName = null)
    {
        var current = (await ListAsync()).ToList();
        current.RemoveAll(a => string.Equals(a.OwnerRepo, ownerRepo, StringComparison.OrdinalIgnoreCase));
        current.Add(new KnownApp(ownerRepo, type, DateTimeOffset.UtcNow, appName));
        Preferences.Default.Set(Key, JsonSerializer.Serialize(current));
    }

    public async Task RemoveAsync(string ownerRepo)
    {
        var current = (await ListAsync())
            .Where(a => !string.Equals(a.OwnerRepo, ownerRepo, StringComparison.OrdinalIgnoreCase)).ToList();
        Preferences.Default.Set(Key, JsonSerializer.Serialize(current));
    }
}

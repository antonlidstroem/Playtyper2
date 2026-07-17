using System.Text.Json;
using Microsoft.JSInterop;

namespace Playtyper.Shared.Services;

public sealed record KnownApp(string OwnerRepo, string Type, DateTimeOffset LastUsedUtc, string? AppName);

/// <summary>
/// "Mina appar" — motsvarar PackWizards RepoHistory.cs, fast lagrad i
/// webbläsaren/appen istället för i en lokal JSON-fil. Icke-hemlig metadata
/// (repo-namn, typ, senast använd) — aldrig token, därför en annan lagringsyta
/// än ICredentialStore (får leva längre än en session).
/// </summary>
public interface IAppHistoryStore
{
    Task<IReadOnlyList<KnownApp>> ListAsync();
    Task RecordAsync(string ownerRepo, string type, string? appName = null);
    Task RemoveAsync(string ownerRepo);
}

/// <summary>Web-implementation (Blazor WASM) — localStorage. Registreras i Playtyper.Web/Program.cs.</summary>
public sealed class LocalStorageAppHistoryStore(IJSRuntime js) : IAppHistoryStore
{
    private const string Key = "playtyper.known-apps";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<KnownApp>> ListAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("playtyperInterop.localGet", Key);
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<KnownApp>();
            var list = JsonSerializer.Deserialize<List<KnownApp>>(raw, JsonOpts) ?? new();
            return list.OrderByDescending(a => a.LastUsedUtc).ToList();
        }
        catch
        {
            return Array.Empty<KnownApp>();
        }
    }

    public async Task RecordAsync(string ownerRepo, string type, string? appName = null)
    {
        var current = (await ListAsync()).ToList();
        current.RemoveAll(a => string.Equals(a.OwnerRepo, ownerRepo, StringComparison.OrdinalIgnoreCase));
        current.Add(new KnownApp(ownerRepo, type, DateTimeOffset.UtcNow, appName));
        await js.InvokeVoidAsync("playtyperInterop.localSet", Key, JsonSerializer.Serialize(current));
    }

    public async Task RemoveAsync(string ownerRepo)
    {
        var current = (await ListAsync()).Where(a =>
            !string.Equals(a.OwnerRepo, ownerRepo, StringComparison.OrdinalIgnoreCase)).ToList();
        await js.InvokeVoidAsync("playtyperInterop.localSet", Key, JsonSerializer.Serialize(current));
    }
}

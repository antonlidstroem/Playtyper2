using Microsoft.JSInterop;

namespace Playtyper.Shared.Services;

/// <summary>
/// Skydd mot dataförlust: speglar det aktuella PackDraft-utkastet till
/// IndexedDB vid varje ändring, så ett tillfälligt stängt flik/dödad app
/// inte kostar osparat arbete. Helt separat från ICredentialStore — det här
/// är bara innehåll, aldrig hemligheter.
///
/// Best-effort med flit: alla fel sväljs (loggas till konsolen via JS) —
/// draft-cachen får ALDRIG blockera eller krascha det faktiska "Spara till
/// GitHub"-flödet, den är bara ett skyddsnät under det.
/// </summary>
public sealed class LocalDraftCache(IJSRuntime js)
{
    private static string KeyFor(string ownerRepo, string packId) => $"{ownerRepo}::{packId}";

    public async Task SaveAsync(string ownerRepo, string packId, string json)
    {
        try
        {
            await js.InvokeVoidAsync("playtyperInterop.draftSave",
                KeyFor(ownerRepo, packId), json, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch { /* best-effort, se klassdoc */ }
    }

    public async Task<(string Json, DateTimeOffset SavedAt)?> LoadAsync(string ownerRepo, string packId)
    {
        try
        {
            var result = await js.InvokeAsync<DraftCacheEntry?>(
                "playtyperInterop.draftLoad", KeyFor(ownerRepo, packId));
            if (result is null) return null;
            return (result.Json, DateTimeOffset.Parse(result.SavedAtIso));
        }
        catch
        {
            return null;
        }
    }

    public async Task DeleteAsync(string ownerRepo, string packId)
    {
        try { await js.InvokeVoidAsync("playtyperInterop.draftDelete", KeyFor(ownerRepo, packId)); }
        catch { /* best-effort */ }
    }

    /// <summary>Styr beforeunload-varningen i webbläsaren (no-op i MAUI-hybridkontext).</summary>
    public async Task SetUnsavedFlagAsync(bool hasUnsaved)
    {
        try { await js.InvokeVoidAsync("playtyperInterop.setUnsavedChangesFlag", hasUnsaved); }
        catch { /* best-effort */ }
    }

    private sealed class DraftCacheEntry
    {
        public string Json { get; set; } = "";
        public string SavedAtIso { get; set; } = "";
    }
}

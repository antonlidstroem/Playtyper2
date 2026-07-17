using System.Text.Json;
using Playtypus.Core.Services;

namespace Playtyper.Shared.Services;

/// <summary>
/// Bridges a Playtyper PackDraft (in-memory editor state) to Playtypus.Core's
/// in-memory entry points (PackContext.LoadFromMemoryAsync,
/// ThemeService.ApplyInMemoryThemeAsync/ApplyTypographyAsync) — the "adapter"
/// GAPS.md §2 step 4 asked for.
///
/// WHY THIS STILL CROSSES A MESSAGE BOUNDARY (JSON string) INSTEAD OF A
/// DIRECT CALL, EVEN THOUGH BOTH SIDES NOW SHARE THE SAME MODEL TYPES:
///
/// The preview cannot render Playtypus.Core's &lt;AppShell&gt; in the same
/// document as the rest of Playtyper. Playtypus.Core's global stylesheets
/// (ui.css etc.) declare bare-element rules — body, button, a, input,
/// h1-h4 — that Playtyper's OWN app.css ALSO declares for its own chrome.
/// Load both in one document and whichever loads last wins those rules
/// EVERYWHERE in that document, not just inside the preview area — there is
/// no CSS wrapper trick that scopes a bare `body {}`/`button {}` rule to a
/// subtree. Confirmed by reading both stylesheets side by side, not
/// assumed. So the preview renders in an &lt;iframe&gt; pointed at
/// PreviewFramePage — a genuinely separate document (isolated CSSOM,
/// guaranteed by the browser) that happens to be a second instance of this
/// SAME Playtyper app. Two documents means two separate DI containers, so
/// there is no direct C# reference from PreviewPanel's world to
/// PreviewFramePage's world to begin with — data has to cross as a
/// postMessage no matter what, and postMessage can only carry serializable
/// data. JSON is that serialization, same as it would be for any
/// cross-iframe payload regardless of C# type identity.
///
/// 2026-07-13 UPDATE: until now there was a SECOND, separate reason to
/// serialize — PackDraft.Config used to be a Playtyper.Shared.Models.
/// PackConfig, while PackContext.LoadFromMemoryAsync needed a
/// Playtypus.Core.Models.PackConfig: two nominally different C# types that
/// merely happened to have an identical wire shape, requiring a
/// serialize-as-one/deserialize-as-the-other dance through two near-
/// duplicate private record types (PlaytyperSidePayload /
/// PlaytypusCoreSidePayload) just to satisfy the compiler. Playtyper.Shared
/// no longer keeps its own copy of these model classes (see
/// Playtyper.Shared.csproj's comment) — PackDraft.Config IS a
/// Playtypus.Core.Models.PackConfig now, full stop. So this file only
/// serializes once, into ONE payload record, for the CSS-isolation reason
/// above. The old "two types, same shape, cross your fingers" risk this
/// class used to warn about no longer exists: there is nothing left to
/// drift apart.
/// </summary>
public static class PreviewAdapter
{
    /// <summary>Parent side (PreviewPanel.razor, main document): draft → JSON ready to postMessage.</summary>
    public static string ToJson(PackDraft draft)
    {
        var payload = new PreviewPayload(
            draft.Config,
            draft.ActivitiesByLang,
            draft.TranslationsByLang,
            PackDraft.ThemeToCss(draft.ThemeLight),
            draft.ThemeDark.Count > 0 ? PackDraft.ThemeToCss(draft.ThemeDark) : null,
            draft.Config.CustomCss);
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// Child side (PreviewFramePage.razor, inside the iframe): the JSON
    /// ToJson produced → applied to this document's own PackContext/ThemeService.
    ///
    /// Deliberately does not take/return "which language" from the caller:
    /// the wire payload never carries one (the parent has no reliable way to
    /// know which language the preview is CURRENTLY showing, since language
    /// switches happen entirely inside AppShell's own UI, inside the iframe,
    /// invisible to the parent without a whole second message channel back
    /// the other way). Instead, every call after the first keeps whatever
    /// pack.Lang is already showing — pack is the same long-lived, DI-scoped
    /// PackContext for this iframe's entire session, so
    /// pack.Lang.ActiveLanguage already reflects the last thing the user
    /// actually selected, and re-passing it back into
    /// LoadFromMemoryAsync's preferredLanguage is a no-op unless something
    /// actually changed elsewhere. Only the very first call (pack.IsLoaded
    /// still false) has nothing to preserve yet, so it passes null and lets
    /// LoadFromMemoryAsync fall back to Config.DefaultLanguage.
    /// </summary>
    public static async Task ApplyAsync(PackContext pack, ThemeService theme, string json)
    {
        var payload = JsonSerializer.Deserialize<PreviewPayload>(json)
            ?? throw new InvalidOperationException("Empty or malformed preview payload.");

        var preferredLanguage = pack.IsLoaded ? pack.Lang.ActiveLanguage : null;
        await pack.LoadFromMemoryAsync(payload.Config, payload.ActivitiesByLang, payload.TranslationsByLang, preferredLanguage);
        await theme.ApplyInMemoryThemeAsync(payload.LightCss, payload.DarkCss, payload.CustomCss);
        await theme.ApplyTypographyAsync(payload.Config.Typography);
    }

    // One shape, used by both ToJson and ApplyAsync — safe now that both
    // sides of the postMessage boundary agree on the same PackConfig/
    // Activity types (Playtypus.Core.Models), not just the same JSON shape.
    private sealed record PreviewPayload(
        PackConfig Config,
        Dictionary<string, List<Activity>> ActivitiesByLang,
        Dictionary<string, Dictionary<string, string>> TranslationsByLang,
        string LightCss,
        string? DarkCss,
        string? CustomCss);
}

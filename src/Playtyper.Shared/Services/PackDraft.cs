using System.Text.Json;
using Playtyper.Shared.Models;

namespace Playtyper.Shared.Services;

/// <summary>
/// Allt redigeras HÄR, i minnet — ingenting skrivs till GitHub förrän
/// PackDraftStore.CommitAsync anropas explicit. Det ger en naturlig
/// "ångra"-punkt (stäng bara utan att spara) och en tydlig plats att visa
/// en diff innan något faktiskt skickas iväg.
///
/// SCOPE-BESLUT: diffen nedan är på FIL-nivå, inte fält-nivå. Att bygga en
/// riktig strukturell JSON-diff (vilka enskilda fält ändrades, med
/// path-markering) är en betydande egen uppgift; för v1 räcker det att visa
/// VILKA av de fem filerna som ändrats plus deras fullständiga före/efter-
/// text — det ger samma säkerhetsnät (inget skrivs blint) utan att behöva
/// en egen diff-algoritm. Ett bra ställe att bygga ut senare om det visar
/// sig behövas i praktiken.
/// </summary>
public sealed class PackDraft
{
    public required string PackId { get; init; }
    public required PackConfig Config { get; set; }

    /// <summary>Nyckel = språkkod ("sv", "en", ...).</summary>
    public required Dictionary<string, List<Activity>> ActivitiesByLang { get; set; }

    /// <summary>Nyckel = språkkod. Värde = nyckel/värde-par för UI-text.</summary>
    public required Dictionary<string, Dictionary<string, string>> TranslationsByLang { get; set; }

    /// <summary>CSS-variabelnamn UTAN "--"-prefix (t.ex. "color-primary") → värde.</summary>
    public required Dictionary<string, string> ThemeLight { get; set; }
    public required Dictionary<string, string> ThemeDark { get; set; }

    /// <summary>
    /// Ursprunglig rå filtext vid LoadAsync, oförändrad — grunden för
    /// Diff(). Nyckel = repo-relativ sökväg.
    /// </summary>
    public required Dictionary<string, string> OriginalFiles { get; init; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serialiserar det aktuella utkastet till de fem filerna som faktiskt
    /// skulle skrivas till GitHub. Ren funktion — inget skrivs här.
    /// </summary>
    public Dictionary<string, string> ToFiles(RemoteRepo repo)
    {
        var files = new Dictionary<string, string>
        {
            [repo.PackConfigPath(PackId)] = JsonSerializer.Serialize(Config, JsonOpts),
            [$"packs/{PackId}/theme.css"]      = ThemeToCss(ThemeLight),
            [$"packs/{PackId}/theme-dark.css"] = ThemeToCss(ThemeDark),
        };

        foreach (var (lang, activities) in ActivitiesByLang)
            files[$"packs/{PackId}/activities.{lang}.json"] = JsonSerializer.Serialize(activities, JsonOpts);

        foreach (var (lang, dict) in TranslationsByLang)
            files[$"packs/{PackId}/translations.{lang}.json"] = JsonSerializer.Serialize(dict, JsonOpts);

        return files;
    }

    public static string ThemeToCss(Dictionary<string, string> tokens)
    {
        var lines = tokens.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"  --{kv.Key}: {kv.Value};");
        return ":root {\n" + string.Join("\n", lines) + "\n}\n";
    }

    /// <summary>Enkel parser för :root { --namn: värde; ... } — täcker de fall PackWizard faktiskt genererar.</summary>
    public static Dictionary<string, string> ThemeFromCss(string css)
    {
        var result = new Dictionary<string, string>();
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(css, @"--([a-zA-Z0-9\-]+)\s*:\s*([^;]+);"))
        {
            result[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim();
        }
        return result;
    }

    /// <summary>True om något fält i utkastet skiljer sig från vad som lästes in senast.</summary>
    public bool IsDirty(RemoteRepo repo) => Diff(repo).Count > 0;

    public IReadOnlyList<FileChange> Diff(RemoteRepo repo)
    {
        var current = ToFiles(repo);
        var changes = new List<FileChange>();

        foreach (var (path, newContent) in current)
        {
            OriginalFiles.TryGetValue(path, out var oldContent);
            if (oldContent == newContent) continue;

            changes.Add(new FileChange(
                path,
                oldContent == null ? FileChangeKind.Added : FileChangeKind.Modified,
                oldContent,
                newContent));
        }

        return changes;
    }
}

public enum FileChangeKind { Added, Modified, Removed }

public sealed record FileChange(string Path, FileChangeKind Kind, string? OldContent, string NewContent);

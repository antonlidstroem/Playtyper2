using System.Text.Json;
using Playtyper.Shared.Models;

namespace Playtyper.Shared.Services;

public sealed record CommitResult(bool Success, string? Error, int FilesWritten);

/// <summary>
/// Läser ett pack från GitHub in i ett redigerbart PackDraft, och skriver
/// tillbaka det igen. Det enda stället i Playtyper som binder ihop
/// RemoteRepo (fil-I/O), PackDraft (redigeringstillstånd) och Validator
/// (kontroll innan skrivning).
/// </summary>
public static class PackDraftStore
{
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    public static async Task<PackDraft> LoadAsync(RemoteRepo repo, string packId)
    {
        var packDir = repo.PackDir(packId);
        var originals = new Dictionary<string, string>();

        var configPath = repo.PackConfigPath(packId);
        var configRaw  = await repo.ReadFileAsync(configPath) ?? "{}";
        originals[configPath] = configRaw;

        // Frånvarande fält i ett äldre pack = default, inte fel — annars
        // blockeras redigering varje gång FeatureManifest växer med ett nytt
        // fält. System.Text.Json ger redan detta beteende gratis så länge
        // Config-klassen har vettiga default-värden (vilket den har, se
        // PackConfig.cs) - men vi säkrar det explicit här ändå för läsbarhet.
        var config = JsonSerializer.Deserialize<PackConfig>(configRaw, ReadOpts) ?? new PackConfig();

        var themeLightPath = $"{packDir}/theme.css";
        var themeDarkPath  = $"{packDir}/theme-dark.css";
        var themeLightRaw  = await repo.ReadFileAsync(themeLightPath) ?? ":root {\n}\n";
        var themeDarkRaw   = await repo.ReadFileAsync(themeDarkPath)  ?? ":root {\n}\n";
        originals[themeLightPath] = themeLightRaw;
        originals[themeDarkPath]  = themeDarkRaw;

        var activitiesByLang    = new Dictionary<string, List<Activity>>();
        var translationsByLang  = new Dictionary<string, Dictionary<string, string>>();

        foreach (var lang in config.Languages is { Count: > 0 }
                     ? config.Languages.Select(l => l.Code)
                     : new List<string> { "sv" })
        {
            var actPath = $"{packDir}/activities.{lang}.json";
            var actRaw  = await repo.ReadFileAsync(actPath);
            if (actRaw != null)
            {
                originals[actPath] = actRaw;
                activitiesByLang[lang] = JsonSerializer.Deserialize<List<Activity>>(actRaw, ReadOpts) ?? new();
            }

            var trPath = $"{packDir}/translations.{lang}.json";
            var trRaw  = await repo.ReadFileAsync(trPath);
            if (trRaw != null)
            {
                originals[trPath] = trRaw;
                translationsByLang[lang] = JsonSerializer.Deserialize<Dictionary<string, string>>(trRaw, ReadOpts) ?? new();
            }
        }

        return new PackDraft
        {
            PackId = packId,
            Config = config,
            ActivitiesByLang = activitiesByLang,
            TranslationsByLang = translationsByLang,
            ThemeLight = PackDraft.ThemeFromCss(themeLightRaw),
            ThemeDark = PackDraft.ThemeFromCss(themeDarkRaw),
            OriginalFiles = originals,
        };
    }

    /// <summary>
    /// Skriver alla ändrade filer (enligt draft.Diff) till GitHub, i tur och
    /// ordning. Kör INTE Validator.ValidateAsync automatiskt — anroparen
    /// (UI:t) förväntas visa valideringsresultatet separat och låta
    /// användaren bekräfta även vid varningar, precis som PackWizard [2] gör.
    /// </summary>
    public static async Task<CommitResult> CommitAsync(RemoteRepo repo, PackDraft draft, string commitMessage)
    {
        var changes = draft.Diff(repo);
        if (changes.Count == 0) return new CommitResult(true, null, 0);

        var written = 0;
        try
        {
            foreach (var change in changes)
            {
                await repo.WriteFileAsync(change.Path, change.NewContent, commitMessage);
                written++;
            }
        }
        catch (RemoteWriteException ex)
        {
            return new CommitResult(false, $"Misslyckades vid \"{ex.Path}\": {ex.GitHubError} " +
                $"({written}/{changes.Count} filer skrevs innan felet — kör Validera för att se det aktuella tillståndet).",
                written);
        }

        // Uppdatera OriginalFiles i draftet så nästa Diff() blir tom igen
        // tills något nytt ändras — annars visar UI:t "osparat" direkt efter
        // en lyckad spara.
        foreach (var change in changes)
            draft.OriginalFiles[change.Path] = change.NewContent;

        return new CommitResult(true, null, written);
    }
}

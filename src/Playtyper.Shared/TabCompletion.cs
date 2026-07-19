using Playtyper.Shared.Services;

namespace Playtyper.Shared;

/// <summary>
/// Beräknar en enkel "grunden ifylld"-status per flik i PackEditorPage —
/// inte en fullständig validering (det är Validator.cs:s jobb, redan
/// inkopplat i PackEditorPage.SaveAsync), bara en snabb, icke-blockerande
/// fingervisning i sidopanelen om var man kommit i redigeringen. Del C,
/// "Nivå 1" i 2026-07-19-pedagogik-strategin.
///
/// Varje flik får ett av tre lägen:
///   - true  ("klar"-prick): de mest grundläggande fälten har ett värde.
///   - false ("tom"-prick):  inget av det grundläggande är ifyllt än.
///   - null  (ingen prick):  fliken har inget meningsfullt "ifyllt"-koncept
///     (Avancerat), eller innehållet är genuint valfritt för ALLA packs
///     (Läge & genvägar, Backend & inloggning — många fullt fungerande
///     packs kommer aldrig använda dessa alls, så en "tom"-prick där vore
///     missvisande skuldbeläggande snarare än vägledande).
///
/// MEDVETET ENKELT: kriterierna nedan är någorlunda godtyckliga trösklar
/// (t.ex. "minst en aktivitet" för Aktiviteter-fliken), inte en fullständig
/// kvalitetsbedömning. Målet är en snabb "har jag börjat här alls"-signal,
/// inte ett omdöme om innehållet är BRA. Håll det så — om det här börjar
/// växa mot riktig validering, hör det hemma i Validator.cs istället.
/// </summary>
public static class TabCompletion
{
    private const string DefaultAppName = "Playtypus"; // PackConfig.AppName:s eget default-värde

    /// <summary>
    /// Beräknar status för samtliga flikar. Nyckeln matchar exakt tab.Id i
    /// PackEditorPage._tabs — ändrar du ett flik-id där, ändra samma sträng
    /// här, annars tappar den fliken tyst sin statusprick (AppShell visar
    /// helt enkelt ingen prick om nyckeln saknas — inget kraschar, men
    /// funktionen slutar synas).
    /// </summary>
    public static Dictionary<string, bool?> Compute(PackDraft draft)
    {
        var config = draft.Config;

        return new Dictionary<string, bool?>
        {
            ["identity"] = !string.IsNullOrWhiteSpace(config.AppName)
                           && config.AppName != DefaultAppName,

            ["tokens"] = draft.ThemeLight.ContainsKey("color-background")
                         || draft.ThemeLight.ContainsKey("color-accent")
                         || draft.ThemeLight.ContainsKey("color-primary"),

            ["activities"] = draft.ActivitiesByLang.Values.Any(list => list.Count > 0),

            // Innehåll-fliken sätter i praktiken ALLTID åtminstone någon
            // funktion eller kategori för ett pack som är på väg att bli
            // användbart — men till skillnad från t.ex. Identitet finns
            // inget enskilt "defaultvärde" att jämföra mot, så kriteriet
            // blir "minst en kategori ELLER minst ett filter definierat".
            ["content"] = config.Categories.Count > 0 || config.Filters.Count > 0,

            // Genuint valfritt för alla packs — se klassdoc ovan om null.
            ["situations"] = null,

            ["onboarding"] = config.Onboarding.Count > 0 || config.Tutorial.Count > 0,

            ["appearance"] = config.Ui != null,

            // Genuint valfritt — de flesta packs har ingen extern backend
            // och inget lösenordsskydd, det är inte ett tecken på att
            // något saknas.
            ["backends"] = null,

            // Avancerat har inget "ifyllt"-koncept — det är en redigeringsyta,
            // inte ett innehållsavsnitt.
            ["advanced"] = null,
        };
    }
}

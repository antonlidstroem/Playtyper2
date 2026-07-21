using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Playtyper.Shared.Services;

public static class Validator
{
    public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Validates a pack for structural correctness, reading everything from
    /// GitHub via RemoteRepo. Checks: required files, auth hash format,
    /// translation key coverage, category/filter consistency, activity id
    /// uniqueness.
    ///
    /// v12: takes (RemoteRepo, packId) instead of a local packDir path.
    /// Performance note: fetches one recursive file listing up front (a
    /// single API call) and uses it for every "does X exist" check below,
    /// instead of one network round-trip per File.Exists like the old local
    /// version effectively got "for free". Content (pack.config.json, each
    /// language's activities.*.json) is still fetched individually since
    /// that's unavoidable — we actually need to read and parse it.
    /// </summary>
    public static async Task<ValidationResult> ValidateAsync(RemoteRepo repo, string packId)
    {
        var errors   = new List<string>();
        var warnings = new List<string>();
        var packDir  = repo.PackDir(packId);

        var existingFiles = (await GitHubRepoService
            .ListFilesRecursiveAsync(repo.Token, repo.OwnerRepo, packDir, repo.Branch))
            .ToHashSet(StringComparer.Ordinal);

        bool Exists(string relativeName) => existingFiles.Contains($"{packDir}/{relativeName}");

        // ── Required files ────────────────────────────────────────────────────
        if (!Exists("pack.config.json"))
        {
            errors.Add("pack.config.json saknas.");
            return new(false, errors, warnings);
        }

        var configRaw = await repo.ReadFileAsync($"{packDir}/pack.config.json");

        JsonObject? config;
        try { config = configRaw == null ? null : JsonNode.Parse(configRaw) as JsonObject; }
        catch { errors.Add("pack.config.json är inte giltig JSON."); return new(false, errors, warnings); }

        if (config == null) { errors.Add("pack.config.json är tom."); return new(false, errors, warnings); }

        // ── Required top-level fields ─────────────────────────────────────────
        foreach (var field in new[] { "appName", "packId", "tagline", "defaultLanguage" })
            if (string.IsNullOrEmpty(config[field]?.GetValue<string>()))
                errors.Add($"Obligatoriskt fält saknas: {field}");

        // ── Auth block ────────────────────────────────────────────────────────
        if (config["auth"] is JsonObject auth)
        {
            var enabled = auth["enabled"]?.GetValue<bool>() ?? false;
            if (enabled)
            {
                var hash = auth["passwordHash"]?.GetValue<string>() ?? "";
                if (!Regex.IsMatch(hash, "^[0-9a-f]{64}$"))
                    errors.Add("auth.passwordHash är inte en giltig SHA-256-hex (64 tecken, a-f 0-9).");

                var hours = auth["sessionHours"]?.GetValue<int>() ?? 0;
                if (hours < 1 || hours > 8760)
                    warnings.Add($"auth.sessionHours={hours} är utanför rimligt intervall (1–8760 timmar).");
            }
        }

        // ── Languages → activities + translations ─────────────────────────────
        var langs = (config["languages"] as JsonArray ?? new JsonArray())
            .Select(n => n?["code"]?.GetValue<string>())
            .Where(c => c != null)
            .Select(c => c!)
            .ToList();

        if (!langs.Any())
            errors.Add("Inga språk deklarerade i languages-listan.");

        foreach (var lang in langs)
        {
            if (!Exists($"activities.{lang}.json"))
                errors.Add($"activities.{lang}.json saknas.");
            if (!Exists($"translations.{lang}.json"))
                errors.Add($"translations.{lang}.json saknas.");
        }

        // ── Category consistency ──────────────────────────────────────────────
        var declaredCategories = (config["categories"] as JsonArray ?? new JsonArray())
            .Select(n => n?["id"]?.GetValue<string>())
            .Where(c => c != null && c != "all")
            .Select(c => c!)
            .ToHashSet();

        // Playtypus.Core.Models.CategoryConfig kräver "labelKey", inte "label".
        // Fel nyckelnamn ger ingen krasch — kategorin dyker upp i schemat och i
        // declaredCategories ovan (id läses fint), men fliken renderas med tom
        // text i FilterDrawer.razor (`@Pack.Lang.T(c.LabelKey)` mot en tom sträng).
        if (config["categories"] is JsonArray categoriesForLabelCheck)
        {
            foreach (var cNode in categoriesForLabelCheck)
            {
                if (cNode is not JsonObject cat) continue;
                var catId = cat["id"]?.GetValue<string>() ?? "(namnlös kategori)";
                if (string.IsNullOrEmpty(cat["labelKey"]?.GetValue<string>()))
                    errors.Add($"Kategori '{catId}': saknar 'labelKey' (CategoryConfig kräver exakt det " +
                               "nyckelnamnet, inte 'label') — kategoriflikens text blir tom i appen.");
            }
        }

        // ── Filter shape + type consistency ────────────────────────────────────
        // Fångar det som JsonNode-parsningen ovan aldrig ser: att `options`
        // faktiskt är objekt (Playtypus.Core.Models.FilterOption kräver
        // { "value": ..., "labelKey": ... }, INTE rena strängar), och att
        // `type` är ett värde FilterDrawer.razor faktiskt renderar.
        // Utan den här kontrollen ser en pack.config.json med
        // "options": ["Lugn", "Lekfull"] helt giltig ut här, men kraschar
        // appen vid körning (DeserializeUnableToConvertValue mot FilterOption).
        var allowedFilterTypes = new HashSet<string> { "segmented", "toggle", "computed" };
        var declaredFilterIds  = new HashSet<string>();

        if (config["filters"] is JsonArray filters)
        {
            foreach (var fNode in filters)
            {
                if (fNode is not JsonObject filter) continue;

                var filterId = filter["id"]?.GetValue<string>() ?? "(namnlöst filter)";
                declaredFilterIds.Add(filterId);

                if (string.IsNullOrEmpty(filter["labelKey"]?.GetValue<string>()))
                    errors.Add($"Filter '{filterId}': saknar 'labelKey' (FilterConfig kräver exakt det " +
                               "nyckelnamnet, inte 'label') — filtrets rubrik blir tom i FilterDrawer.razor.");

                var type = filter["type"]?.GetValue<string>();
                if (type == null || !allowedFilterTypes.Contains(type))
                    errors.Add($"Filter '{filterId}': type='{type}' hanteras inte av FilterDrawer.razor " +
                               "(stödjer just nu bara \"segmented\" och \"toggle\"; \"computed\" kräver " +
                               "features.smartFilters=true). Filtret renderas tyst som ingenting — " +
                               "ingen krasch, men fungerar inte.");

                if (filter["options"] is JsonArray options)
                {
                    for (int i = 0; i < options.Count; i++)
                    {
                        if (options[i] is not JsonObject opt)
                        {
                            errors.Add($"Filter '{filterId}', options[{i}]: är en ren sträng/värde, inte ett " +
                                       "objekt { \"value\": ..., \"labelKey\": ... }. Det här är exakt formen " +
                                       "som kraschar Playtypus.Core.Models.FilterOption vid körning " +
                                       "(DeserializeUnableToConvertValue).");
                            continue;
                        }
                        if (string.IsNullOrEmpty(opt["value"]?.GetValue<string>()))
                            errors.Add($"Filter '{filterId}', options[{i}]: saknar 'value'.");
                        if (string.IsNullOrEmpty(opt["labelKey"]?.GetValue<string>()))
                            errors.Add($"Filter '{filterId}', options[{i}]: saknar 'labelKey'.");
                    }
                }
            }
        }

        // ── situationPresets: labelKey + filterBundle ──────────────────────────
        // SituationPreset kräver "labelKey" (inte "label") och "filterBundle"
        // (inte "filterValues" — det namnet är upptaget av Activity istället).
        // Fel fältnamn ger ingen krasch: chippen syns (id/emoji räcker för att
        // rendera knappen) men gör tyst ingenting när man trycker på den,
        // eftersom PackContext.ApplySituationPreset loopar över en tom
        // FilterBundle-dictionary.
        if (config["situationPresets"] is JsonArray presets)
        {
            foreach (var pNode in presets)
            {
                if (pNode is not JsonObject preset) continue;
                var presetId = preset["id"]?.GetValue<string>() ?? "(namnlös situationPreset)";

                if (string.IsNullOrEmpty(preset["labelKey"]?.GetValue<string>()))
                    errors.Add($"situationPresets['{presetId}']: saknar 'labelKey' — chippens text blir tom.");

                if (preset["filterBundle"] is not JsonObject)
                {
                    if (preset["filterValues"] != null)
                        errors.Add($"situationPresets['{presetId}']: har 'filterValues', men SituationPreset " +
                                   "heter fältet 'filterBundle'. Chippen syns men gör ingenting när man " +
                                   "trycker på den (FilterBundle blir en tom dictionary).");
                    else
                        errors.Add($"situationPresets['{presetId}']: saknar 'filterBundle' — chippen gör " +
                                   "ingenting när man trycker på den.");
                }
            }
        }

        // ── panicButton: labelKey/sublabelKey ───────────────────────────────────
        // PanicButtonConfig.LabelKey defaultar till "panic.button" (icke-tomt!)
        // om fältet saknas helt, vilket är OK om translations har den nyckeln.
        // Men om panicButton-objektet FINNS och använder fel fältnamn
        // (buttonText/subtitleText/label) skrivs aldrig den avsedda texten in —
        // LabelKey stannar på defaultvärdet, och T() faller tillbaka till att
        // visa den råa nyckelsträngen ("panic.button") som synlig knapptext om
        // den nyckeln inte heller råkar finnas i translations.
        if (config["panicButton"] is JsonObject panic)
        {
            if (string.IsNullOrEmpty(panic["labelKey"]?.GetValue<string>()))
                errors.Add("panicButton: saknar 'labelKey' (PanicButtonConfig kräver 'labelKey'/'sublabelKey', " +
                           "inte t.ex. 'buttonText') — knappen visar antingen tom text eller den råa " +
                           "nyckelsträngen istället för er egen text.");
            if (string.IsNullOrEmpty(panic["sublabelKey"]?.GetValue<string>()))
                errors.Add("panicButton: saknar 'sublabelKey'.");
        }

        // ── quickActions (v14): labelKey + behavior + behavior-specific fields ──
        // QuickActionConfig är en generalisering av panicButton/situationPresets
        // — samma vanliga fällor gäller: fel/saknat 'labelKey' ger tom knapptext,
        // och filterBundle-värden måste vara strängar av samma skäl som
        // situationPresets.filterBundle och activity.filterValues ovan.
        var allowedQuickActionBehaviors = new HashSet<string>
            { "randomFromPool", "applyFilter", "openActivity", "openCategory" };

        if (config["quickActions"] is JsonArray quickActions)
        {
            foreach (var qNode in quickActions)
            {
                if (qNode is not JsonObject qa) continue;
                var qaId = qa["id"]?.GetValue<string>() ?? "(namnlös quickAction)";

                if (string.IsNullOrEmpty(qa["labelKey"]?.GetValue<string>()))
                    errors.Add($"quickActions['{qaId}']: saknar 'labelKey' — knappens text blir tom.");

                var behavior = qa["behavior"]?.GetValue<string>();
                if (behavior == null || !allowedQuickActionBehaviors.Contains(behavior))
                    errors.Add($"quickActions['{qaId}']: behavior='{behavior}' hanteras inte av " +
                               "AppShell.HandleQuickAction (giltiga värden: randomFromPool, applyFilter, " +
                               "openActivity, openCategory). Knappen renderas men gör ingenting när man " +
                               "trycker på den.");

                if (behavior == "openActivity" && string.IsNullOrEmpty(qa["targetActivityId"]?.GetValue<string>()))
                    errors.Add($"quickActions['{qaId}']: behavior=openActivity men 'targetActivityId' saknas.");

                if (behavior == "openCategory")
                {
                    var targetCat = qa["targetCategoryId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(targetCat))
                        errors.Add($"quickActions['{qaId}']: behavior=openCategory men 'targetCategoryId' saknas.");
                    else if (!declaredCategories.Contains(targetCat))
                        errors.Add($"quickActions['{qaId}']: targetCategoryId='{targetCat}' matchar ingen " +
                                   "deklarerad kategori.");
                }

                if (qa["filterBundle"] is JsonObject qaBundle)
                {
                    foreach (var kv in qaBundle)
                        if (kv.Value != null && kv.Value.GetValueKind() != JsonValueKind.String)
                            errors.Add($"quickActions['{qaId}']: filterBundle.{kv.Key} måste vara en sträng " +
                                       "(samma krav som situationPresets.filterBundle).");
                }
            }
        }

        // ── ui.homeLayout (v14, extended v18) ───────────────────────────────────
        var homeLayout = config["ui"]?["homeLayout"]?.GetValue<string>();
        var validHomeLayouts = new[] { "feed", "dashboard", "map", "today", "magazine", "search", "sections" };
        if (homeLayout != null && !validHomeLayouts.Contains(homeLayout))
            warnings.Add($"ui.homeLayout='{homeLayout}' är okänt för AppShell.razor (giltiga värden: " +
                         string.Join(", ", validHomeLayouts.Select(v => $"\"{v}\"")) + ") — behandlas som \"feed\".");
        var hasQuickActions = (config["quickActions"] as JsonArray)?.Count > 0;
        if (homeLayout == "dashboard" && !hasQuickActions && !declaredCategories.Any())
            warnings.Add("ui.homeLayout='dashboard' men varken quickActions eller categories är satta — " +
                         "dashboarden renderas tom. (Fortsätter fungera: \"Bläddra allt\" visar den vanliga " +
                         "listan precis som idag.)");
        if (homeLayout == "sections" && declaredCategories.Count < 2)
            warnings.Add("ui.homeLayout='sections' men färre än två categories är satta — " +
                         "sektions-vyn bygger på en hylla per kategori och ger lite mening med bara en. " +
                         "(Fortsätter fungera: renderas som en enda hylla plus ev. \"Övrigt\".)");
        // Not checked here (would need activities.*.json, only loaded further
        // down in this method): whether "map" has any activities with a map
        // content block, or "today" has any with `repeat` set. Both degrade
        // to a friendly in-app empty state rather than an error either way —
        // see MapHomeView.razor / TodayHomeView.razor — so this is a nice-to-
        // have warning, not a correctness gap, if someone wants to add it later.

        // ── readyNow: krävs om features.readyNowSection är true ────────────────
        // ReadyNowSection.razor renderar sektionen bara om Config.ReadyNow != null.
        // features.readyNowSection defaultar till true i genererade packs, så en
        // AI som glömmer readyNow-objektet ger en "på men tom" sektion — flaggan
        // säger ett, appen gör ett annat, helt utan felmeddelande.
        var readyNowSectionOn = config["features"]?["readyNowSection"]?.GetValue<bool>() ?? false;
        if (readyNowSectionOn && config["readyNow"] is not JsonObject)
            errors.Add("features.readyNowSection=true men pack.config.json saknar ett 'readyNow'-objekt " +
                       "på toppnivå — \"Redo nu\"-sektionen renderas aldrig trots att flaggan är på. " +
                       "Lägg till readyNow eller sätt readyNowSection=false.");

        // Check activities for each language
        foreach (var lang in langs)
        {
            if (!Exists($"activities.{lang}.json")) continue;

            var actRaw = await repo.ReadFileAsync($"{packDir}/activities.{lang}.json");
            if (actRaw == null) continue;

            JsonArray? activities;
            try { activities = JsonNode.Parse(actRaw) as JsonArray; }
            catch { errors.Add($"activities.{lang}.json är inte giltig JSON."); continue; }

            if (activities == null) continue;

            var ids     = new HashSet<string>();
            int actIdx  = 0;   // 1-based for human-readable messages

            foreach (var node in activities)
            {
                actIdx++;
                var act = node as JsonObject;
                if (act == null) continue;

                var id  = act["id"]?.GetValue<string>() ?? $"(index {actIdx})";
                var cat = act["category"]?.GetValue<string>();

                // Duplicate ID
                if (!ids.Add(id))
                    errors.Add($"[{lang}] Duplicerat aktivitets-id: {id}");

                // Unknown category
                if (cat != null && !declaredCategories.Contains(cat))
                    errors.Add($"[{lang}] Aktivitet '{id}' har okänd kategori: {cat}");

                // Steps
                var steps = act["steps"] as JsonArray;
                if (steps == null || steps.Count < 3)
                    warnings.Add($"[{lang}] Aktivitet '{id}' har färre än 3 steg.");

                // requiresProps consistency
                var reqProps = act["requiresProps"]?.GetValue<bool>() ?? false;
                var props    = act["props"] as JsonArray;
                if (reqProps && (props == null || props.Count == 0))
                    errors.Add($"[{lang}] Aktivitet '{id}': requiresProps=true men props är tom.");

                // contentVersion
                var cv = act["contentVersion"]?.GetValue<int>() ?? 0;
                if (cv < 1)
                    warnings.Add($"[{lang}] Aktivitet '{id}' saknar contentVersion (bör vara ≥1).");

                // cardTemplate (v14): valfritt fält på Activity. Okända värden
                // kraschar inte — ActivityCard.razor faller tillbaka till den
                // vanliga (standard) kortmallen för allt utom exakt "profile" —
                // men en felstavning ("proifle") ger tyst fel resultat, så det
                // är värt en varning.
                var cardTemplate = act["cardTemplate"]?.GetValue<string>();
                if (cardTemplate != null && cardTemplate != "standard" && cardTemplate != "profile")
                    warnings.Add($"[{lang}] Aktivitet '{id}': cardTemplate='{cardTemplate}' känns inte igen " +
                                 "av ActivityCard.razor (giltiga värden: \"standard\", \"profile\") — " +
                                 "renderas som \"standard\".");

                // filterValues: Activity.FilterValues är Dictionary<string,string> —
                // stödjer INTE flervärda filter (arrayer) ännu, oavsett vad filtrets
                // egen `type` säger. Samma bugklass som options-kontrollen ovan,
                // bara i activities-filen istället för pack.config.json.
                if (act["filterValues"] is JsonObject fv)
                {
                    foreach (var kv in fv)
                    {
                        if (kv.Value is JsonArray)
                            errors.Add($"[{lang}] Aktivitet '{id}': filterValues.{kv.Key} är en array — måste " +
                                       "vara en enda sträng (Activity.FilterValues är Dictionary<string,string>).");
                        else if (kv.Value is JsonObject)
                            errors.Add($"[{lang}] Aktivitet '{id}': filterValues.{kv.Key} är ett objekt — " +
                                       "måste vara en sträng.");
                        else if (kv.Value != null && kv.Value.GetValueKind() != JsonValueKind.String)
                            errors.Add($"[{lang}] Aktivitet '{id}': filterValues.{kv.Key} är {kv.Value.GetValueKind()} " +
                                       $"({kv.Value.ToJsonString()}), inte en sträng. Det här är allvarligare än det " +
                                       $"ser ut: JsonSerializer.Deserialize kraschar då på HELA activities.{lang}.json, " +
                                       "och PackContext.LoadActivitiesAsync fångar felet tyst — resultatet är att " +
                                       "APPEN VISAR NOLL AKTIVITETER för hela packet, inte bara den här. Vanligaste " +
                                       "orsaken: \"requiresProps\": true/false skrivet inuti filterValues (utelämna " +
                                       "det hellre helt — se kommentar i aktivitetsschemat).");
                        else if (declaredFilterIds.Count > 0 && !declaredFilterIds.Contains(kv.Key))
                            warnings.Add($"[{lang}] Aktivitet '{id}': filterValues.{kv.Key} matchar inget " +
                                         "filter-id i pack.config.json.");
                    }
                }
            }
        }

        // ── Theme files ───────────────────────────────────────────────────────
        if (!Exists("theme.css"))
            warnings.Add("theme.css saknas.");
        if (!Exists("theme-dark.css"))
            warnings.Add("theme-dark.css saknas.");

        return new(!errors.Any(), errors, warnings);
    }
}

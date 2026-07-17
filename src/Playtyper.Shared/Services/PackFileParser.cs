using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playtyper.Shared.Services;

/// <summary>
/// Parsar Claude's (eller annan LLM:s) markdown-svar och extraherar pack-filerna.
///
/// Filerna PackWizard letar efter:
///   Fas 1:  pack.config.json  |  theme.css  |  theme-dark.css
///   Fas 2:  activities.{lang}.json  |  translations.{lang}.json
///
/// Tre identifieringsstrategier i prioritetsordning:
///   1. Filnamnet som language-tag i code fence:   ```pack.config.json
///   2. Filnamnet i texten ovanför code block:     **pack.config.json** eller ## theme.css
///   3. Innehållsdetektering: JSON-struktur + CSS-selektorer
///
/// v11-ändringar:
///   - Parse() tar nu en valfri IEnumerable&lt;string&gt; langCodes för att dynamiskt
///     registrera aktivitets- och translations-filnamn för alla språk i briefen.
///     Tidigare var bara sv och en hårdkodade — packs med no, ar, so m.m. hittades aldrig.
///   - KnownFilenames är nu en instans-uppsättning (inte static readonly) som byggs
///     per anrop utifrån de givna språkkoderna.
/// </summary>
public static class PackFileParser
{
    // ── Alltid kända filnamn (oberoende av språk) ─────────────────────────────

    private static readonly string[] BaseFilenames =
    {
        "pack.config.json",
        "theme.css",
        "theme-dark.css",
        // Utan språkkod (LLM:en skriver ibland så här — normaliseras nedan):
        "activities.json",
        "translations.json",
    };

    private static readonly Regex CodeBlockRegex = new(
        @"```(?<lang>[^\n`]*?)\r?\n(?<content>.*?)(?:\r?\n)?```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // ── Publik API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parsar ett markdown-svar och returnerar {filnamn → filinnehåll}.
    /// Duplicat (samma filnamn funnet flera gånger) → första träffen vinner.
    ///
    /// langCodes: språkkoder från briefen (t.ex. ["sv","en","ar"]).
    ///   Lämna null/tomt för att bara känna igen sv och en (bakåtkompatibelt).
    /// </summary>
    public static Dictionary<string, string> Parse(
        string response,
        IEnumerable<string>? langCodes = null)
    {
        var known      = BuildKnownFilenames(langCodes);
        var aliases    = BuildAliases(langCodes);
        var result     = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cssCounter = 0;

        foreach (Match match in CodeBlockRegex.Matches(response))
        {
            var lang    = match.Groups["lang"].Value.Trim();
            var content = match.Groups["content"].Value.Trim();
            var pos     = match.Index;

            if (string.IsNullOrWhiteSpace(content)) continue;

            var filename = DetectFilename(response, pos, content, lang, ref cssCounter, known);
            if (filename == null) continue;

            // Normalisera alias (activities.json → activities.{defaultLang}.json)
            if (aliases.TryGetValue(filename, out var normalized))
                filename = normalized;

            if (!result.ContainsKey(filename))
                result[filename] = content;
        }

        return result;
    }

    // ── Bygga känd-uppsättning dynamiskt ──────────────────────────────────────

    private static HashSet<string> BuildKnownFilenames(IEnumerable<string>? langCodes)
    {
        var known = new HashSet<string>(BaseFilenames, StringComparer.OrdinalIgnoreCase);

        // Alltid med sv och en som bakåtkompatibelt minimum.
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sv", "en" };
        if (langCodes != null)
            foreach (var c in langCodes)
                if (!string.IsNullOrWhiteSpace(c))
                    codes.Add(c.Trim().ToLowerInvariant());

        foreach (var code in codes)
        {
            known.Add($"activities.{code}.json");
            known.Add($"translations.{code}.json");
        }

        return known;
    }

    private static Dictionary<string, string> BuildAliases(IEnumerable<string>? langCodes)
    {
        // Utan språkkod: mappa till det första språket i listan (eller sv som fallback).
        var codes     = langCodes?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList()
                        ?? new List<string>();
        var defaultLang = codes.Count > 0 ? codes[0].Trim().ToLowerInvariant() : "sv";

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["activities.json"]   = $"activities.{defaultLang}.json",
            ["translations.json"] = $"translations.{defaultLang}.json",
        };
    }

    // ── Strategi 1–3 ─────────────────────────────────────────────────────────

    private static string? DetectFilename(
        string response, int blockPos, string content, string lang,
        ref int cssCounter, HashSet<string> known)
    {
        // Strategi 1: Filnamnet som language identifier i fencen
        //   ```pack.config.json
        if (known.Contains(lang)) return lang.ToLowerInvariant();

        // Strategi 2: Filnamnet i texten direkt ovanför code block
        var preceding    = GetPreceding(response, blockPos, maxChars: 400);
        var fromHeader   = ExtractFilenameFromHeader(preceding, known);
        if (fromHeader != null) return fromHeader;

        // Strategi 2b: Filnamnet som kommentar på första raden i blocket
        //   // pack.config.json  eller  /* theme.css */
        var firstLine     = content.Split('\n')[0].Trim();
        var fromFirstLine = ExtractFilenameFromComment(firstLine, known);
        if (fromFirstLine != null) return fromFirstLine;

        // Strategi 3: Innehållsdetektering
        return DetectByContent(content, lang, ref cssCounter, known);
    }

    // ── Rubrik-extraktion ─────────────────────────────────────────────────────

    private static string? ExtractFilenameFromHeader(string preceding, HashSet<string> known)
    {
        var lines = preceding.TrimEnd().Split('\n');
        var tail  = lines.Skip(Math.Max(0, lines.Length - 6)).ToArray();

        foreach (var line in tail.Reverse())
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Mönster: **filename**, *filename*, __filename__
            var m = Regex.Match(trimmed, @"\*{1,2}([\w.+-]+\.(?:json|css))\*{0,2}");
            if (m.Success && known.Contains(m.Groups[1].Value))
                return m.Groups[1].Value.ToLowerInvariant();

            // Mönster: ## filename eller ### filename
            m = Regex.Match(trimmed, @"^#{1,4}\s+([\w.+-]+\.(?:json|css))");
            if (m.Success && known.Contains(m.Groups[1].Value))
                return m.Groups[1].Value.ToLowerInvariant();

            // Mönster: "Och theme.css:" / "theme.css:" / "theme.css"
            m = Regex.Match(trimmed,
                @"(?:^|\s)([\w.+-]+\.(?:json|css))\s*:?\s*$",
                RegexOptions.IgnoreCase);
            if (m.Success && known.Contains(m.Groups[1].Value))
                return m.Groups[1].Value.ToLowerInvariant();

            // Mönster: "Här är pack.config.json" / "Och theme-dark.css nedan"
            m = Regex.Match(trimmed,
                @"([\w.+-]+\.(?:json|css))",
                RegexOptions.IgnoreCase);
            if (m.Success && known.Contains(m.Groups[1].Value))
                return m.Groups[1].Value.ToLowerInvariant();
        }

        return null;
    }

    private static string? ExtractFilenameFromComment(string firstLine, HashSet<string> known)
    {
        // // filename.json  |  /* filename.css */  |  # filename.css
        var m = Regex.Match(firstLine, @"^(?://|/\*|#)\s*([\w.+-]+\.(?:json|css))");
        if (m.Success && known.Contains(m.Groups[1].Value))
            return m.Groups[1].Value.ToLowerInvariant();
        return null;
    }

    // ── Innehållsdetektering ──────────────────────────────────────────────────

    private static string? DetectByContent(
        string content, string lang, ref int cssCounter, HashSet<string> known)
    {
        var trimmed     = content.TrimStart();
        bool looksLikeCss = lang.Equals("css", StringComparison.OrdinalIgnoreCase)
                         || trimmed.StartsWith(":root")
                         || (trimmed.Contains("--color-") && trimmed.Contains("{"));

        if (looksLikeCss)
        {
            if (content.Contains("[data-theme") ||
                content.Contains(":root[data-theme") ||
                Regex.IsMatch(content, @"\[data-theme\s*=") ||
                content.Contains("html.dark"))
                return "theme-dark.css";

            cssCounter++;
            return cssCounter == 1 ? "theme.css" : "theme-dark.css";
        }

        if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
            return null;

        try
        {
            using var doc  = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                // pack.config.json: har fältet "packId"
                if (root.TryGetProperty("packId", out _))
                    return "pack.config.json";

                // translations: top-level keys är language codes ("sv", "en" etc.)
                // eller fältet "translations"
                if (root.TryGetProperty("translations", out _))
                    return FindFirstTranslationsFilename(known);

                foreach (var prop in root.EnumerateObject())
                {
                    if (known.Contains($"translations.{prop.Name}.json"))
                        return FindFirstTranslationsFilename(known);
                }
            }

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root.EnumerateArray().First();
                if (first.ValueKind == JsonValueKind.Object &&
                    (first.TryGetProperty("activityId", out _) ||
                     first.TryGetProperty("id",         out _) ||
                     first.TryGetProperty("title",      out _)))
                    return FindFirstActivitiesFilename(known);
            }
        }
        catch
        {
            // Ogiltig JSON — hoppa över
        }

        return null;
    }

    // ── Hjälpare ──────────────────────────────────────────────────────────────

    private static string? FindFirstTranslationsFilename(HashSet<string> known)
    {
        // Föredra sv, sedan en, sedan första träffen.
        foreach (var lang in new[] { "sv", "en" })
        {
            var name = $"translations.{lang}.json";
            if (known.Contains(name)) return name;
        }
        return known.FirstOrDefault(n => n.StartsWith("translations.", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindFirstActivitiesFilename(HashSet<string> known)
    {
        foreach (var lang in new[] { "sv", "en" })
        {
            var name = $"activities.{lang}.json";
            if (known.Contains(name)) return name;
        }
        return known.FirstOrDefault(n => n.StartsWith("activities.", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetPreceding(string text, int pos, int maxChars)
    {
        var start = Math.Max(0, pos - maxChars);
        return text[start..pos];
    }
}

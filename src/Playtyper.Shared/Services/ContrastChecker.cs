using System.Text.RegularExpressions;

namespace Playtyper.Shared.Services;

/// <summary>
/// WCAG 2.1-kontrastberäkning för hex-färgpar. Används av Validator.cs för att
/// fånga svaga temakontraster INNAN de skeppas — se PromptGenerator.WriteThemeSection
/// för motsvarande instruktion på promptsidan (samma lager-på-lager-mönster som
/// redan används för labelKey/filterBundle-incidenten: prompt + validator, inte
/// bara det ena).
///
/// Rotorsak till varför det här behövs: PromptGenerator.cs nämnde tidigare noll
/// (0) konkreta CSS-variabelnamn — bara "definiera alla obligatoriska variabler
/// (se schema ovan)" utan att schemat ovan någonsin namngav en enda. Två redan
/// skeppade packs (samfunden, badplatserisverige) uppfann varsin egen, sinsemellan
/// olika FEL variabelkonvention som bevis. Även med rätt variabelnamn på plats
/// finns inget som helst hindrar en AI från att välja två nyanser som råkar
/// ligga för nära varandra i ljushet — därav ett eget numeriskt kontrollsteg,
/// oberoende av hur bra prompttexten är formulerad.
/// </summary>
public static class ContrastChecker
{
    /// <summary>
    /// WCAG 2.1 minimikrav för normal brödtext (under ~18pt/24px eller
    /// ~14pt/18.5px fet). Nivå AA.
    /// </summary>
    public const double MinRatioNormalText = 4.5;

    /// <summary>
    /// WCAG 2.1 minimikrav för stor text (≥18pt/24px, eller ≥14pt/18.5px fet)
    /// samt UI-komponenter/grafiska objekt som förmedlar betydelse (t.ex.
    /// ramar som markerar ett aktivt tillstånd). Nivå AA.
    /// </summary>
    public const double MinRatioLargeTextOrUi = 3.0;

    /// <summary>
    /// Försöker tolka en hex-färgsträng (#RGB, #RRGGBB eller #RRGGBBAA — alpha
    /// ignoreras för kontrastberäkning) till (r, g, b) i 0–255. Returnerar
    /// false om strängen inte är en giltig hex-färg.
    /// </summary>
    public static bool TryParseHex(string? hex, out (byte R, byte G, byte B) rgb)
    {
        rgb = (0, 0, 0);
        if (string.IsNullOrWhiteSpace(hex)) return false;

        var h = hex.Trim();
        if (!h.StartsWith('#')) return false;
        h = h[1..];

        try
        {
            switch (h.Length)
            {
                case 3: // #RGB — varje siffra dubbleras
                    rgb = (
                        Convert.ToByte(new string(h[0], 2), 16),
                        Convert.ToByte(new string(h[1], 2), 16),
                        Convert.ToByte(new string(h[2], 2), 16));
                    return true;
                case 6: // #RRGGBB
                case 8: // #RRGGBBAA — de sista två (alpha) ignoreras
                    rgb = (
                        Convert.ToByte(h[..2], 16),
                        Convert.ToByte(h[2..4], 16),
                        Convert.ToByte(h[4..6], 16));
                    return true;
                default:
                    return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Relativ luminans enligt WCAG 2.1 (https://www.w3.org/TR/WCAG21/#dfn-relative-luminance).
    /// </summary>
    public static double RelativeLuminance(byte r, byte g, byte b)
    {
        double Linearize(byte c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    /// <summary>
    /// Kontrastkvot mellan två hex-färger, alltid ≥1.0 (ljusaste/mörkaste,
    /// ordning på argumenten spelar ingen roll). Null om någon av färgerna
    /// inte gick att tolka.
    /// </summary>
    public static double? ContrastRatio(string? hex1, string? hex2)
    {
        if (!TryParseHex(hex1, out var c1) || !TryParseHex(hex2, out var c2))
            return null;

        var l1 = RelativeLuminance(c1.R, c1.G, c1.B);
        var l2 = RelativeLuminance(c2.R, c2.G, c2.B);
        var (lighter, darker) = l1 >= l2 ? (l1, l2) : (l2, l1);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Extraherar `--namn: #hex;`-deklarationer ur en CSS-textkropp till en
    /// dictionary. Grov men tillräcklig — pack-teman är enkla platta
    /// variabellistor, inga nästlade block eller calc()-uttryck att hantera.
    /// Sista förekomsten av en variabel vinner (matchar CSS:ens eget beteende
    /// vid dubbletter inom samma selector).
    /// </summary>
    public static Dictionary<string, string> ExtractHexVariables(string? css)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(css)) return result;

        foreach (Match m in Regex.Matches(css, @"(--[a-zA-Z0-9-]+)\s*:\s*(#[0-9a-fA-F]{3,8})\s*;"))
            result[m.Groups[1].Value] = m.Groups[2].Value;

        return result;
    }
}

namespace Playtyper.Shared;

/// <summary>
/// Single source of truth for every valid value of the structural "ui.*"
/// style fields (headerStyle, cardStyle, gridStyle, detailStyle, density,
/// timelineGrouping, homeLayout, categoryNavPosition).
///
/// WHY THIS FILE EXISTS (2026-07, v18):
/// A senior-architect review found that every one of these fields had
/// exactly one value ever shown anywhere — to the AI (PromptGenerator
/// rendered one example value per field with no alternatives listed) and
/// to a human (AppearanceEditor.razor had three dropdowns whose option
/// values didn't match ui-variants.css at all — "solid"/"masonry"/"page"
/// where the CSS actually listens for "primary"/"two-column"/"fullscreen"
/// — plus a fourth field, cardStyle, that was a blank text box with no
/// options shown at all). Both problems have the same root cause: there
/// was no single place that actually knew the full, correct value set, so
/// nobody — human or AI — could discover or safely use most of it. See
/// GAPS.md and the architecture review thread for the full writeup.
///
/// Every value below is verified against
/// Playtypus.Core/wwwroot/css/ui-variants.css and v18-additions.css
/// (the actual CSS selectors, not memory/assumption) as of v18. If you add
/// a new variant to that CSS, add it here in the same change — and if you
/// add it here, it does nothing until the matching CSS selector exists.
/// The two must move together; that discipline is the entire point of
/// this file.
///
/// AppearanceEditor.razor drives its dropdowns from these lists.
/// PromptGenerator.cs renders these lists (value + label + description)
/// into the AI-facing schema instead of a single unexplained example
/// value, so the AI can actually see that alternatives exist and pick one
/// deliberately instead of always copying the one example it's shown.
/// </summary>
public static class LayoutStyleManifest
{
    public sealed record StyleOption(string Value, string Label, string Description);

    // ── ui.headerStyle ──────────────────────────────────────────────────
    public static readonly IReadOnlyList<StyleOption> HeaderStyles = new List<StyleOption>
    {
        new("surface",     "Följer ytan",     "Sidhuvudet smälter in i bakgrunden bakom det, ingen tydlig gräns. Standardvärdet — passar de flesta packs."),
        new("primary",     "Enfärgad",        "Sidhuvudet får appens primärfärg som egen, tydlig bakgrund. Bra när appens färgidentitet ska synas direkt."),
        new("transparent", "Genomskinligt",   "Sidhuvudet syns bara som text/ikoner ovanpå innehållet, ingen egen bakgrund alls. Ger en luftig, minimalistisk känsla."),
        new("image",       "Bild",            "En bild fyller hela sidhuvudet, med en mörk gradient bakom text/ikoner för läsbarhet. Kräver att headerImage också sätts (URL eller pack-relativ sökväg). Passar recept, resor, stadsguider — packs där en stämningsbild högst upp gör mer än en enfärgad yta."),
    };

    // ── ui.cardStyle ────────────────────────────────────────────────────
    public static readonly IReadOnlyList<StyleOption> CardStyles = new List<StyleOption>
    {
        new("default",  "Standard",   "Emoji/bild + titel + kort beskrivning, samma kortform som de flesta packs redan använder. Standardvärdet."),
        new("compact",  "Kompakt",    "Mindre kort, tätare packade — bra när appen har många aktiviteter och överblick väger tyngre än detaljer per kort."),
        new("magazine", "Magasin",    "Större kort med mer synlig beskrivningstext (upp till tre rader). Passar innehåll där själva texten på kortet ska löcka till läsning, inte bara vara en etikett."),
        new("wall",     "Bildvägg",   "Kant-till-kant-foton, titel som bildtext ovanpå en gradient — som ett bildgalleri. Kräver att de flesta aktiviteter faktiskt har heroImage/thumbnail; aktiviteter utan bild visas som en centrerad emoji-ruta istället."),
    };

    // ── ui.gridStyle ────────────────────────────────────────────────────
    public static readonly IReadOnlyList<StyleOption> GridStyles = new List<StyleOption>
    {
        new("single",       "Ett kort brett",     "Ett kort per rad på smala skärmar (mobilstandard). Standardvärdet."),
        new("two-column",   "Två kolumner",       "Två kort per rad redan från en smal mobilskärm (≥360px) — mer överblick, mindre varje kort."),
        new("profile-grid", "Profil-/katalogrutnät", "Många små, kvadratiska rutor i rad — för kataloger, register, personallistor. Passar bäst tillsammans med cardTemplate: \"profile\" på aktiviteterna (porträtt + namn/roll), men fungerar även med vanliga kort, de blir bara smalare."),
    };

    // ── ui.detailStyle ──────────────────────────────────────────────────
    // Note: no "split"/master-detail value here even though it was
    // proposed during the review — premium-desktop.css already gives
    // every pack a persistent side-panel treatment at ≥900px regardless
    // of this field, so a separate opt-in would just be a confusing,
    // redundant second switch for something that's already the default.
    public static readonly IReadOnlyList<StyleOption> DetailStyles = new List<StyleOption>
    {
        new("sheet",      "Blad underifrån", "Detaljvyn glider upp som ett blad, med resten av appen synlig/dimmad bakom. Standardvärdet."),
        new("fullscreen", "Helskärm",         "Detaljvyn täcker hela skärmen, ingen bakgrund synlig. Passar innehållstungt material (långa recept, artiklar) där hela ytan bör gå till innehållet."),
    };

    // ── ui.density ──────────────────────────────────────────────────────
    public static readonly IReadOnlyList<StyleOption> DensityOptions = new List<StyleOption>
    {
        new("comfortable", "Bekväm",  "Generösa marginaler och radavstånd. Standardvärdet — passar de flesta packs, särskilt för äldre eller stressade användare."),
        new("compact",     "Kompakt", "Mindre marginaler, mer innehåll synligt utan att scrolla."),
        new("minimal",     "Minimal", "Så tätt som möjligt utan att bli svårläst — för powerusers och referensverk med mycket innehåll."),
    };

    // ── ui.timelineGrouping (endast relevant när layoutMode = timeline) ──
    public static readonly IReadOnlyList<StyleOption> TimelineGroupingOptions = new List<StyleOption>
    {
        new("month", "Månad", "Grupperar tidslinjen per månad. Standardvärdet — passar de flesta logg-/dagboksliknande packs."),
        new("week",  "Vecka", "Grupperar per vecka — bättre överblick för packs med tät aktivitet (flera poster per vecka)."),
        new("year",  "År",   "Grupperar per år — bättre överblick för packs som sträcker sig över lång tid med glesare poster."),
    };

    // ── ui.homeLayout ─────────────────────────────────────────────────────
    public static readonly IReadOnlyList<StyleOption> HomeLayouts = new List<StyleOption>
    {
        new("feed",      "Flöde",              "Den vanliga, scrollbara aktivitetslistan direkt på startsidan. Standardvärdet — rätt val för de flesta packs."),
        new("dashboard", "Instrumentpanel",    "Snabbknappar + kategoriplattor fyller startskärmen; det vanliga flödet nås via en kategori eller \"bläddra bland allt\". Passar packs byggda kring en handfull tydliga handlingar (6–10 QuickActions) snarare än en lång lista att scrolla."),
        new("map",       "Karta",              "Aktiviteter med en \"map\"-innehållsblock (lat/lng) visas som nålar; resten listas nedanför per kategori. Passar platser, friluftsliv, stadsguider. Faller tillbaka till en vanlig grupperad lista om inget i packet har koordinater."),
        new("today",     "Idag",               "Visar bara aktiviteter med repeat satt som inte redan är klarmarkerade idag. Passar rutin-/vaneappar. Om packet inte använder repeat alls visas hela flödet istället, med en tydlig förklaring varför."),
        new("magazine",  "Magasin",            "Ett stort, framhävt \"hero\"-kort (första aktiviteten med layoutHint: \"featured\", annars den första i packordningen) följt av resten i vanligt rutnät. Passar redaktionellt/berättande innehåll."),
        new("search",    "Sök",                "Ett framträdande sökfält + kategorigenvägar istället för ett fullt flöde. Passar stora referensbibliotek (50+ aktiviteter) där bläddring inte är poängen."),
        new("sections",  "Sektioner",          "En horisontellt skrollande hylla per kategori (\"Netflix-stil\") istället för ett långt blandat flöde. Kräver minst ett par kategorier med flera aktiviteter var för att kännas motiverat — annars ge \"feed\" eller \"dashboard\" företräde."),
    };

    // ── ui.categoryNavPosition ────────────────────────────────────────────
    public static readonly IReadOnlyList<StyleOption> CategoryNavPositions = new List<StyleOption>
    {
        new("drawer", "I filterlådan",  "Kategoriflikarna syns bara när användaren öppnar filter-/sök-lådan. Standardvärdet."),
        new("pinned", "Fast högst upp", "Samma kategoriflikar visas även som en fast rad ovanför resultatlistan, för att byta kategori med ett tryck utan att öppna lådan. Kräver features.categoryTabs = true och minst två kategorier; blir trångt med fler än ~7."),
    };

    /// <summary>
    /// Renders every list above as Markdown for the AI-facing prompt —
    /// value, label and a one-line description each — so the model sees
    /// the full option space instead of a single unexplained example.
    /// Called from PromptGenerator.WriteSystemSchema.
    /// </summary>
    public static string RenderForPrompt()
    {
        var sb = new System.Text.StringBuilder();

        void Section(string title, string field, IReadOnlyList<StyleOption> options, string extra = "")
        {
            sb.AppendLine($"**`{field}`** — {title}{(extra.Length > 0 ? $" ({extra})" : "")}:");
            foreach (var o in options)
                sb.AppendLine($"- `\"{o.Value}\"` — {o.Label}: {o.Description}");
            sb.AppendLine();
        }

        Section("sidhuvudets stil", "ui.headerStyle", HeaderStyles);
        Section("kortens stil", "ui.cardStyle", CardStyles);
        Section("rutnätets kolumnbredd", "ui.gridStyle", GridStyles);
        Section("detaljvyns presentation", "ui.detailStyle", DetailStyles);
        Section("täthet/marginaler", "ui.density", DensityOptions);
        Section("gruppering i tidslinjeläge", "ui.timelineGrouping", TimelineGroupingOptions, "bara relevant om layoutMode/defaultLayoutMode = timeline någonstans");
        Section("startsidans layout", "ui.homeLayout", HomeLayouts);
        Section("var kategoriflikarna visas", "ui.categoryNavPosition", CategoryNavPositions, "bara relevant om features.categoryTabs = true");

        return sb.ToString();
    }
}

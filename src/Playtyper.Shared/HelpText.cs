namespace Playtyper.Shared;

/// <summary>
/// Förklarande text riktad till en person UTAN programmeringsbakgrund —
/// medvetet skild från <see cref="FeatureManifest"/>, som är "single source
/// of truth" för AI-PROMPTEN (se den filens egen klassdoc) och skriven i en
/// helt annan stil: kort, teknisk, full av C#/JSON-fältnamn i backticks
/// ("Activity.heroImage", "ui.availableLayouts") — rätt för en AI som redan
/// kan läsa ett schema, fel för en människa som inte kan programmering.
///
/// Den här filen försöker INTE täcka alla ~55 fält FeatureManifest känner
/// till. De flesta av dem har redan en kort, läsbar <c>What</c>-etikett som
/// duger fint i UI:t som den är (t.ex. "Favoritmarkering på aktiviteter").
/// Det som samlas här är:
///   1. De fält där den korta etiketten ändå lutar sig mot ett fackuttryck,
///      en förkortning, eller en C#/JSON-referens en icke-tekniker snubblar
///      på (se <see cref="Features"/>).
///   2. Text för sådant som INTE kommer från FeatureManifest alls — flikars
///      syfte, enum-liknande val (StartView, header-stil), och andra ställen
///      i UI:t som pekades ut under 2026-07-18-genomgången.
///   3. Återkommande KONCEPT som dyker upp i många olika fält snarare än ett
///      enda — t.ex. <see cref="TranslationKeyExplanation"/>, som förklarar
///      *Key-namnkonventionen (TitleKey, LabelKey, HintKey, ...) en gång på
///      ett ställe istället för att varje editor uppfinner sin egen
///      formulering (sju filer refererade konceptet innan den här posten
///      fanns — risk för att formuleringarna glider isär över tid).
///
/// FALLBACK-KONTRAKT: varje ställe som läser härifrån (se ContentEditor.razor,
/// IdentityEditor.razor m.fl.) ska falla tillbaka på den befintliga korta
/// texten (Feature.What, eller en hårdkodad etikett) om inget hittas här —
/// aldrig visa tomt. Se <see cref="ForFeature"/>.
///
/// UNDERHÅLL: det här är INTE en spegling av FeatureManifest och ska inte
/// synkas fält-för-fält mot den. Lägg bara till en post här när du faktiskt
/// stöter på ett ställe i UI:t som förvirrar en icke-teknisk användare —
/// annars växer filen i onödan och blir lika svårnavigerad som problemet
/// den skulle lösa.
/// </summary>
public static class HelpText
{
    /// <summary>
    /// Återanvändbar förklaring av *Key-namnkonventionen (TitleKey, BodyKey,
    /// LabelKey, NameKey, HintKey, ...) som dyker upp i praktiskt taget varje
    /// editor. Tänkt att visas i en <c>info-note</c> bredvid det FÖRSTA
    /// sådana fältet på en given flik, inte upprepas vid varje enskilt fält
    /// (det skulle bli tjatigt) — se t.ex. IdentityEditor.razor för mönstret.
    /// </summary>
    public const string TranslationKeyExplanation =
        "Trots namnet skriver du in den riktiga texten direkt här, inte en kod. " +
        "\"Key\" i fältnamnet syftar på hur det lagras internt (som en rad i en översättningsfil) " +
        "— relevant bara om appen senare ska finnas på flera språk, annars kan du bortse från det helt.";

    /// <param name="Title">Kort rubrik, samma som visas i navigeringen.</param>
    /// <param name="Summary">En mening — vad gör den här fliken, i vardagsspråk.</param>
    /// <param name="Detail">Valfri — 1-3 meningar för den som vill förstå mer. Visas i en utfällbar info-not, inte som ständig brödtext.</param>
    public sealed record TabHelp(string Title, string Summary, string? Detail = null);

    /// <summary>
    /// En flik i PackEditorPage, i samma ordning som _tabs där. Nyckeln
    /// matchar tab.Id exakt (se PackEditorPage.razor).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, TabHelp> Tabs = new Dictionary<string, TabHelp>
    {
        ["identity"] = new(
            "Identitet",
            "Appens namn, färger och vad besökaren möts av först.",
            "Det här är grundstommen — appens namn, huvudfärg och vilken skärm som visas när någon öppnar appen. De flesta andra flikar bygger vidare på valen du gör här."),

        ["tokens"] = new(
            "Färger",
            "De exakta färgerna i appen: bakgrund, text, knappar.",
            "Kallas \"design-tokens\" i tekniska sammanhang — inte att förväxla med GitHub-token (som du skapade på anslutningssidan för att logga in). Här handlar \"token\" om en enskild färg i appens palett, ingenting med säkerhet eller inloggning att göra."),

        ["activities"] = new(
            "Aktiviteter",
            "Listan över allt innehåll besökaren bläddrar bland — tips, övningar, platser, eller vad appen nu handlar om.",
            "Varje rad är en sak besökaren kan öppna och läsa. Du kan lägga till, ta bort och ändra ordning här. Vissa kolumner syns bara om du slagit på rätt funktion under Innehåll-fliken — appen döljer dem annars för att inte skräpa ner tabellen med val som ändå är avstängda."),

        ["content"] = new(
            "Innehåll",
            "Vilka funktioner appen har — bildgalleri, quiz, favoriter, ljud, och mycket annat.",
            "Det här är den stora växeltavlan. Varje rad är en funktion du kan sätta på eller av. Vissa hänger ihop (en kräver en annan för att synas), och appen berättar det när det är läget."),

        ["situations"] = new(
            "Läge & genvägar",
            "Extra lägen för särskilda situationer — till exempel en förenklad panik-vy, eller snabbfilter som visar bara det mest relevanta just nu.",
            "\"Situationspresets\" är knappar som ställer in flera filter på en gång, till exempel en \"Regnig dag\"-genväg som bara visar inomhus-aktiviteter. Helt valfritt att använda."),

        ["onboarding"] = new(
            "Introduktion",
            "De första skärmarna en ny besökare ser, innan de når appens startsida.",
            "Tänk broschyr-sidorna innan man \"kommer in\" i själva appen — några bilder med kort text som förklarar vad appen är till för. Kan stängas av helt om appen inte behöver någon."),

        ["appearance"] = new(
            "Utseende",
            "Hur kort och sidhuvud ser ut rent visuellt — kantrundning, skuggor, bakgrundsstil.",
            "Skiljer sig från \"Utseende — färger\"-fliken: här ändrar du FORMEN på saker (runda hörn eller raka, genomskinligt sidhuvud eller enfärgat), inte färgerna i sig."),

        ["backends"] = new(
            "Backend & inloggning",
            "Om appen ska hämta eller spara data från en extern källa, och om besökare behöver logga in.",
            "\"Backend\" är den tekniska termen för en server någon annanstans som appen pratar med — till exempel för att spara favoriter mellan enheter. De flesta enklare appar klarar sig utan och kan hoppa över den här fliken helt."),

        ["advanced"] = new(
            "Avancerat",
            "Den råa konfigurationsfilen (JSON) — för den som redan känner till formatet.",
            "Allt du ändrar på de andra flikarna påverkar samma fil som visas här. Du behöver aldrig öppna den här fliken för att använda Playtyper, men den finns om du vet vad du letar efter eller behöver ett fält som ingen annan flik täcker än."),
    };

    /// <summary>
    /// Fördjupad, vardagsspråks-text för enskilda FeatureManifest-fält där
    /// den korta <c>Feature.What</c>-etiketten ändå lutar sig mot ett
    /// fackuttryck eller en kodreferens. Nyckeln är <c>Feature.Id</c> exakt
    /// (samma sträng som <c>[JsonPropertyName]</c> i FeatureFlags).
    ///
    /// Bara en delmängd av alla features finns med — se klassdoc-kommentaren
    /// ovan för varför. Slå alltid upp via <see cref="ForFeature"/>, som
    /// faller tillbaka på den korta etiketten om inget hittas här.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Features = new Dictionary<string, string>
    {
        ["quiz"] =
            "Ett litet flervalsprov kopplat till en aktivitet — besökaren svarar på frågor och ser om de hade rätt.",

        ["heroImages"] =
            "En stor bild högst upp när besökaren öppnar en aktivitet, innan de börjar läsa. Bra för att sätta stämningen direkt.",

        ["cardThumbnails"] =
            "En liten förhandsbild på varje aktivitets-kort i listan, så besökaren ser ett smakprov innan de klickar in.",

        ["smartCardVisuals"] =
            "Kortet väljer automatiskt den bästa bilden det har tillgång till (foto, om det finns) istället för att alltid visa samma lilla ikon.",

        ["socialFeed"] =
            "Ett flöde av inlägg (typ ett litet socialt medie-flöde) kopplat till en aktivitet — för communities som vill dela uppdateringar.",

        ["audioPlayer"] =
            "En ljudspelare inuti aktiviteten, för den som vill lägga till exempelvis en guidad ljudberättelse.",

        ["ambientSound"] =
            "Bakgrundsljud som går att sätta på/av i sidhuvudet — till exempel naturljud som spelar medan man bläddrar.",

        ["textToSpeech"] =
            "En knapp som läser upp texten högt, för den som hellre lyssnar än läser.",

        ["shareActivity"] =
            "En dela-knapp så besökaren kan skicka en aktivitet vidare till någon annan, via telefonens vanliga dela-meny eller genom att kopiera en länk.",

        ["imageGallery"] =
            "Flera bilder visas som ett bläddringsbart galleri inuti aktiviteten, inte bara en enda bild.",

        ["galleryLightbox"] =
            "Trycker besökaren på en bild öppnas den i helskärm, så man kan se den ordentligt utan att texten runt om är i vägen.",

        ["galleryBrowse"] =
            "En egen sida där besökaren kan bläddra bland ALLA bilder i hela appen på ett ställe, inte bara de som hör till en enskild aktivitet.",

        ["logbookMultiPhoto"] =
            "Besökaren kan lägga till upp till tre foton per anteckning i loggboken, istället för bara ett.",

        ["guidedMode"] =
            "Visar aktivitetens steg ett i taget med en \"Nästa\"-knapp, istället för att lista alla steg på en gång. Bra för recept eller instruktioner man följer medan man gör dem.",

        ["slideshow"] =
            "Ett bildspels-liknande läge tänkt för att visas på en skärm i rummet, till exempel vid en aktivitet eller ett event.",

        ["doneTracking"] =
            "Besökaren kan markera en aktivitet som \"klar\", och appen kommer ihåg det.",

        ["streakTracking"] =
            "Räknar hur många dagar i rad besökaren gjort något i appen — samma idé som \"streaks\" i andra appar man kanske känner igen.",

        ["badges"] =
            "Besökaren låser upp små \"utmärkelser\" (badges) för saker de gjort i appen, med en liten notis när det händer.",

        ["weeklyGoal"] =
            "Ett mål för hur många aktiviteter besökaren ska hinna med per vecka, med en visuell påminnelse om hur det går.",

        ["levelBadges"] =
            "En liten visuell markering som visar svårighetsgrad eller nivå på en aktivitet.",

        ["favoritesShelf"] =
            "Besökarens favoritmarkerade aktiviteter samlas i en egen \"hylla\" högst upp på startsidan, så de är snabba att hitta igen.",

        ["longPressFavorite"] =
            "Håller besökaren fingret nedtryckt på ett kort en stund favoritmarkeras det direkt, som en genväg utöver den vanliga favorit-knappen.",

        ["activityVoiceNotes"] =
            "Besökaren kan spela in en egen röstanteckning kopplad till en specifik aktivitet.",

        ["logbookVoiceNotes"] =
            "Besökaren kan lägga till en eller flera röstinspelningar till en sparad loggbokspost, när som helst — inte bara i samma stund som de markerar något klart.",

        ["printView"] =
            "Ett utskriftsvänligt format för aktivitetskort, om någon vill skriva ut och sätta upp på en anslagstavla eller liknande.",

        ["export"] =
            "Besökaren kan spara sin loggbok som en PDF-fil.",

        ["ageAdaptedSteps"] =
            "Instruktionerna kan skrivas i flera versioner som anpassas efter ålder — till exempel en enklare formulering för yngre barn.",

        ["showEmoji"] =
            "Använder emoji som en enkel, färgglad ikon på kort och kategorier, istället för egna grafiska ikoner.",

        ["voiceRecorder"] =
            "En fristående lista för röstinspelningar som inte hör till någon specifik aktivitet — nås via appens meny.",

        ["layoutUserToggle"] =
            "Besökaren kan själv växla mellan olika sätt att visa listan (till exempel rutnät eller lista), istället för att du bestämmer ett enda sätt.",

        ["densityUserToggle"] =
            "Besökaren kan själv välja om listan ska visa fler rader tätt packade eller färre rader med mer luft mellan.",

        ["allowUserContent"] =
            "Besökare kan skapa egna aktiviteter i appen, inte bara läsa de du lagt in.",

        ["activityNotes"] =
            "Ett fritextfält där besökaren kan skriva egna anteckningar direkt på en aktivitet, oavsett om de markerat den som klar.",

        ["adminEditorFields"] =
            "Styr vilka fält en administratör får ändra direkt i appen, om appen har en inbyggd redigeringsfunktion. Lämna avstängd om ingen ska kunna redigera innehåll direkt i appen.",

        ["progressionLock"] =
            "Aktiviteter låses upp i ordning — besökaren måste bli klar med en innan nästa öppnas. Bra för kurser eller stegvisa program.",

        ["smartFilters"] =
            "Filter som räknas fram automatiskt istället för att du väljer dem för hand — till exempel \"Populärast just nu\".",

        ["reminders"] =
            "Push-notiser som påminner besökaren om att komma tillbaka till appen.",

        ["multiLanguage"] =
            "En knapp där besökaren kan byta språk. Kräver att du lagt in innehåll på minst två språk — annars finns inget att växla mellan.",

        ["darkMode"] =
            "Besökaren kan själv slå på ett mörkt färgschema i appen, oberoende av vilka färger du valt på Identitet-fliken.",

        ["recentHistory"] =
            "En rad högst upp med de senaste aktiviteterna besökaren tittat på, som en snabb återgång.",

        ["categoryTabs"] =
            "Flikar högst upp för att snabbt filtrera på kategori, istället för att bläddra igenom allt på en gång.",

        ["packVersioning"] =
            "Visar ett litet versionsnummer i appen, användbart för dig när du vill hålla koll på vilken version av innehållet som är driftsatt.",

        ["fontSizeScale"] =
            "Ett reglage där besökaren kan förstora eller förminska texten i hela appen, för bättre läsbarhet.",

        ["cardActions"] =
            "Snabbknappar direkt på aktivitetskortet (till exempel starta timer, eller öppna en länk) utan att behöva klicka in på hela aktiviteten först.",

        ["logbook"] =
            "En loggbok där besökaren sparar en anteckning och gärna ett foto varje gång de gör klart en aktivitet — som en liten dagbok över vad de hunnit med.",
    };

    /// <summary>
    /// Hämtar den fördjupade förklaringen för ett feature-id om en sådan
    /// finns, annars null (aldrig en tom sträng) — anroparen förväntas då
    /// visa den befintliga korta Feature.What-texten precis som innan.
    /// Se t.ex. ContentEditor.razor:s användning.
    /// </summary>
    public static string? ForFeature(string featureId) =>
        Features.TryGetValue(featureId, out var text) ? text : null;

    // ── StartView (IdentityEditor) ──────────────────────────────────────

    /// <param name="Value">Exakt samma sträng som sparas i JSON (ui.startView).</param>
    /// <param name="Label">Vardagsspråks-namn, visas i UI:t istället för det råa värdet.</param>
    /// <param name="Description">En kort mening om vad besökaren faktiskt ser.</param>
    public sealed record StartViewOption(string Value, string Label, string Description);

    /// <summary>
    /// De tre giltiga värdena för ui.startView, i samma ordning som
    /// IdentityEditor.razor's befintliga &lt;select&gt;. Byt du ordningen
    /// eller lägger till ett fjärde värde där, gör samma ändring här.
    /// </summary>
    public static readonly IReadOnlyList<StartViewOption> StartViewOptions = new List<StartViewOption>
    {
        new("grid", "Rutnät med allt innehåll",
            "Besökaren ser hela listan direkt när appen öppnas — det vanligaste valet."),
        new("welcome", "Välkomstskärm först",
            "En kort hälsning eller introduktion visas innan besökaren kommer till listan."),
        new("panic", "Förenklad snabbvy",
            "En avskalad startsida med bara det viktigaste — tänkt för akuta eller stressade situationer där besökaren inte ska behöva leta."),
    };

    // ── Header-stil (AppearanceEditor) ──────────────────────────────────

    public sealed record HeaderStyleOption(string Value, string Label, string Description);

    public static readonly IReadOnlyList<HeaderStyleOption> HeaderStyleOptions = new List<HeaderStyleOption>
    {
        new("solid", "Enfärgad", "Sidhuvudet har en egen, tydlig bakgrundsfärg."),
        new("surface", "Följer ytan", "Sidhuvudet smälter in i bakgrunden bakom det, ingen tydlig gräns."),
        new("transparent", "Genomskinligt", "Sidhuvudet syns bara som text/ikoner ovanpå innehållet, ingen egen bakgrund alls."),
    };
}

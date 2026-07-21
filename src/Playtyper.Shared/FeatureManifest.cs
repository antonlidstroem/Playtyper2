using System.Text;

using Playtyper.Shared.Models;

namespace Playtyper.Shared;

/// <summary>
/// Single source of truth for vad Playtypus.Core's PackConfig/FeatureFlags/
/// Activity-schema faktiskt kan göra — vad varje feature är, när den ska
/// användas, vad den kräver/utesluter, och hur features kombineras till
/// sammanhängande app-typer.
///
/// VARFÖR DEN HÄR FILEN FINNS (2026-07 gap-analys):
/// Feature-kunskap låg tidigare utspritt på tre ställen som drev isär:
///   1. Inline-text i PromptGenerator.cs — 43 av 55 FeatureFlags-fält nämnda,
///      nästan alla utan förklaring, 12 nämndes ALDRIG (imageGallery,
///      galleryLightbox, galleryBrowse, activityVoiceNotes, logbookVoiceNotes,
///      adminEditorFields, shareActivity, levelBadges, showEmoji, voiceRecorder,
///      densityUserToggle, logbookMultiPhoto), och sex toppnivåfält (mascot,
///      tutorial, typography, customCss, backends, tre av sju ui-fält) nämndes
///      noll gånger.
///   2. En fristående referensfil (tools/playtypus-pack-ai-referens-1.md,
///      69 KB, aldrig inläst av någon kod — verifierat: ingen ReadAllText
///      mot den filen existerar i tools/PackWizard/src). Bättre på vissa
///      punkter (har en app-typ-tabell, en partiell CSS-variabelguide) men
///      själv efter samma FeatureFlags-fält (v11/v13-tillägg saknas där också)
///      och helt utan kontrastregler.
///   3. Den faktiska C#-modellen (PackConfig.cs) — den enda av de tre som
///      inte kan ljuga, men som AI:n aldrig ser direkt.
///
/// Bevis på vad glappet kostar: två redan skeppade packs (samfunden,
/// badplatserisverige) har VARSIN egen påhittade CSS-variabelkonvention,
/// och ingen matchar det app.css faktiskt läser. Dessutom visade det sig vid
/// den här omskrivningen att flera av de fält som VERKLIGEN nämndes i det
/// gamla schemat hade FEL exempelvärde jämfört med FeatureFlags verkliga
/// C#-default (textToSpeech, audioPlayer, printView, recentHistory visades
/// som false trots att default är true; shareActivity nämndes inte alls
/// trots default true) — en AI som följde exemplet stängde alltså av saker
/// som är på som standard i koden, helt utan att brief:en bad om det.
///
/// FeatureManifest.cs är nu den enda sanningen för PROMPTEN. PromptGenerator.cs
/// renderar ur den här filen istället för hårdkodad text. Håll den här filen
/// synkad med Playtypus.Core/Models/PackConfig.cs — INTE tvärtom.
///
/// UNDERHÅLL: lägg du till ett nytt fält i FeatureFlags/PackConfig, lägg till
/// det HÄR samtidigt. En enkel reflection-baserad kontroll (gå igenom
/// FeatureFlags via System.Reflection, kräv att varje JsonPropertyName finns
/// i Features.Select(f => f.Id)) skulle göra en missad synk till ett byggfel
/// istället för ett hål som upptäcks om tre månader — se CHANGES.md, punkt 7.
///
/// UPPFÖLJNING (2026-07, efter v14-rundan): exakt det där hålet hann uppstå
/// innan reflection-kontrollen fanns. `smartCardVisuals` (ny FeatureFlags-
/// flagga i samma v14-runda som ProfileCardVariant/QuickActionConfig/
/// UiConfig.HomeLayout/Activity.CardTemplate) saknades helt här — tillagd nu,
/// se "Innehåll & media"-gruppen. De tre andra nya fälten är INTE
/// FeatureFlags-fält (QuickActions/HomeLayout/CardTemplate lever på
/// PackConfig/UiConfig/Activity direkt) och hör därför inte hemma i
/// Features-listan nedan — de är istället dokumenterade direkt i
/// PromptGenerator.WriteSystemSchema (toppnivåschema + aktivitetsschema).
/// Samma runda korrigerade även `galleryBrowse`s Requires-text nedan: den
/// påstod att "gallery" i ui.availableLayouts fungerar — det gör det inte
/// (GalleryBrowseView.razor instansieras aldrig), se den uppdaterade texten.
/// </summary>
public static class FeatureManifest
{
    // ── Datamodeller ─────────────────────────────────────────────────────────

    /// <param name="Id">Exakt JSON-nyckelnamn (JsonPropertyName i FeatureFlags).</param>
    /// <param name="Category">Grupprubrik i schemat, matchar FeatureFlags egna "── X ──"-kommentarer.</param>
    /// <param name="DefaultJson">Literal JSON-representation av C#-defaultvärdet (verifierat mot PackConfig.cs).</param>
    /// <param name="What">Kort, en rad — vad flaggan gör.</param>
    /// <param name="WhenHow">Valfri — 1-3 meningar om NÄR/HUR, bara där det inte är självförklarat.</param>
    /// <param name="Requires">Valfri — beroende till annat fält/objekt.</param>
    /// <param name="AlternativeTo">Valfri — ömsesidigt uteslutande alternativ (inte "kräver", utan "välj en av").</param>
    public sealed record Feature(
        string Id,
        string Category,
        string DefaultJson,
        string What,
        string? WhenHow = null,
        string? Requires = null,
        string? AlternativeTo = null);

    public sealed record ContentBlockType(
        string Type,
        string Fields,
        string What,
        string? WhenHow = null);

    /// <param name="RedefineInDark">
    /// True om variabeln behöver ett eget värde i theme-dark.css. Färger (och
    /// skuggor) ska normalt omdefinieras; typografi/spacing/radie/transition
    /// ärvs från :root i theme.css eftersom html.dark bara skriver över det
    /// som faktiskt står inuti dess egen selector — verifierat mot den
    /// buntade referens-packen "demo": theme-dark.css där definierar bara
    /// de ~26 färgtokens (+ card-shadow/card-shadow-hover), aldrig font-*/
    /// spacing-*/border-radius-*/app-max-width/transition-*.
    /// </param>
    public sealed record CssVariable(
        string Name,
        string Role,
        bool RedefineInDark);

    public sealed record AppTypePreset(
        string Id,
        string Emoji,
        string Name,
        string For,
        string Features,
        string Rationale,
        string SuggestedUi);

    // ── Features (56 FeatureFlags-fält, exakt verifierade mot PackConfig.cs) ──

    public static readonly IReadOnlyList<Feature> Features =
    [
        // ── Navigering & visning ────────────────────────────────────────────
        new("categoryTabs", "Navigering & visning", "true",
            "Kategori-flikar högst upp"),
        new("readyNowSection", "Navigering & visning", "true",
            "\"Redo nu\"-sektion med ett litet urval snabbtillgängliga aktiviteter",
            Requires: "Ett `readyNow`-objekt på TOPPNIVÅ i pack.config.json (labelKey/sublabelKey/count/criteria) — annars renderas sektionen aldrig alls, trots att flaggan är på. Sätt hellre denna till false om sektionen inte passar."),
        new("situationPresets", "Navigering & visning", "true",
            "Situationspresets — snabbknappar som sätter flera filter samtidigt",
            WhenHow: "Varje preset är ett eget objekt i `situationPresets`-arrayen på toppnivå: `{ id, labelKey, emoji, filterBundle }`. Fältet heter `filterBundle`, ALDRIG `filterValues` (det namnet är aktiviteters eget fält i Fas 2)."),
        new("darkMode", "Navigering & visning", "true",
            "Mörkt läge, växlas manuellt i overflow-menyn"),
        new("multiLanguage", "Navigering & visning", "true",
            "Språkväljare — visas bara meningsfullt när `languages` har ≥2 poster"),
        new("recentHistory", "Navigering & visning", "true",
            "Karusell med senast visade aktiviteter (10 st)"),

        // ── Innehåll & media ─────────────────────────────────────────────────
        new("contentBlocks", "Innehåll & media", "false",
            "Rika innehållsblock i aktiviteter — 10 typer, se CONTENT-BLOCK-PALETT nedan",
            WhenHow: "Aktivera så fort en aktivitet behöver mer än text+steg: bilder, karta, ljud, video, PDF, externa länkar. Sätt värdet PER AKTIVITET i Fas 2 (`activity.contentBlocks`), inte här."),
        new("activityActions", "Innehåll & media", "false",
            "Handlingsknappar i detaljvyn: timer, dela, öppna länk, custom"),
        new("quiz", "Innehåll & media", "true",
            "Flervalsquiz kopplat till en aktivitet (Activity.quiz)",
            WhenHow: "Renderas per aktivitet så fort quiz-data finns på den aktiviteten, oavsett annan brief — sätt denna till false bara för att stänga av quiz helt i HELA packen."),
        new("heroImages", "Innehåll & media", "false",
            "Stor bild överst i detaljvyn (Activity.heroImage)",
            Requires: "En riktig bildkälla: antingen en lokal fil i pack-mappen ELLER en absolut https://-URL (hotlinkas direkt, ingen nedladdning krävs). Se \"TILLGÄNGLIGA BILDER\" om källmaterial gav bildkandidater — hitta ALDRIG på en trovärdig-seende URL om ingen riktig finns."),
        new("cardThumbnails", "Innehåll & media", "false",
            "Miniatyrbild på aktivitetskortet (Activity.thumbnail)",
            Requires: "Samma bildkällor som heroImages ovan — lokal fil eller absolut URL."),
        new("smartCardVisuals", "Innehåll & media", "false",
            "v14. Standardkortet visar sin bästa tillgängliga media istället för miniatyr-eller-emoji",
            WhenHow: "Prioritetsordning: Activity.heroImage/första galleribild → första \"youtube\"-content-block (lokal play-ikon, hämtar ALDRIG den riktiga YouTube-miniatyren av integritetsskäl) → första \"map\"-content-block (lokal nål-ikon + locationLabel, hämtar inga kartplattor) → emoji. Global, paket-nivå på/av-växel för STANDARDKORTET — helt oberoende av Activity.cardTemplate (som väljer en annan kortFORM per aktivitet, t.ex. \"profile\"). Säker att slå på även utan bildmaterial: en aktivitet utan bild/video/karta faller tillbaka till exakt samma emoji som idag. Se CardVisual.razor."),
        new("socialFeed", "Innehåll & media", "true",
            "v15. Socialt medieflöde kopplat till en aktivitet (Activity.socialFeed)",
            Requires: "youtube/twitter/facebook: ett handle/kanal-id. instagram: en enskild post/reel-permalänk (instagram.com/p/... eller /reel/...) — inget riktigt scrollande flöde, Meta erbjuder det inte utan Graph API-verifiering.",
            WhenHow: "Renderas per aktivitet så fort Activity.socialFeed finns, oavsett annan brief — sätt denna till false för att stänga av det helt i HELA packen, som quiz ovan. Litet plattformsmärke på kortet, faktiskt embed (bakom tap-to-load-samtycke, som \"youtube\"-content-block) i detaljvyn. Se Activity.cs's SocialFeedConfig-kommentar för exakt schema per plattform, och SocialFeedEmbed.razor."),
        new("audioPlayer", "Innehåll & media", "true",
            "Ljudspelare per aktivitet (Activity.audio)"),
        new("ambientSound", "Innehåll & media", "false",
            "Bakgrundsljud, växlas i headern",
            Requires: "Ett `ambientSound`-objekt på PACK.CONFIG-TOPPNIVÅ ({ file, labelKey, volume }) — den här flaggan är bara på/av-växeln, inte konfigurationen."),
        new("textToSpeech", "Innehåll & media", "true",
            "Text-till-tal på steg och beskrivning"),
        new("shareActivity", "Innehåll & media", "true",
            "Dela-knapp på aktiviteter (native dela-dialog eller kopiera länk)"),

        // ── Bilder & galleri (v11) ───────────────────────────────────────────
        new("imageGallery", "Bilder & galleri", "false",
            "Visa Activity.images som ett galleri i detaljvyns brödtext",
            Requires: "`images`-arrayen på aktiviteten i Fas 2: lista av `{ src, alt, caption, credit }` (samma form som gallery-content-block, se palett nedan)."),
        new("galleryLightbox", "Bilder & galleri", "false",
            "Helskärms-lightbox vid tryck på galleri- eller hero-bild",
            WhenHow: "Opt-in (default av). Naturligt tillägg när imageGallery och/eller heroImages redan är på."),
        new("galleryBrowse", "Bilder & galleri", "false",
            "Ett eget \"galleri\"-vyläge för hela aktivitetslistan (bildrutnät istället för kort/lista)",
            Requires: "INTE FÄRDIGBYGGD ÄNNU (2026-07): GalleryBrowseView.razor finns men instansieras aldrig i AppShell.razor, och PackContext.GetLayoutMode() accepterar bara \"card\"/\"list\"/\"mosaic\" som användarval — \"gallery\" faller tillbaka till kort-vyn precis som vilket okänt värde som helst. Sätt INTE denna flagga eller \"gallery\" i ui.availableLayouts för en riktig kundleverans än; ett halvfungerande vyläge är värre än inget alls. Fråga i konversationen om detta ska prioriteras och färdigställas innan du bygger ett paket som förlitar sig på det."),
        new("logbookMultiPhoto", "Bilder & galleri", "false",
            "Upp till 3 foton per loggbokspost istället för 1",
            Requires: "features.logbook=true."),

        // ── Guidat läge ──────────────────────────────────────────────────────
        new("guidedMode", "Guidat läge", "false",
            "Visar ett steg i taget med Nästa-knapp istället för hela steglistan på en gång"),
        new("slideshow", "Guidat läge", "false",
            "Slideshow-läge, tänkt för projicering/skärm i rummet"),

        // ── Klart-spårning & gamification ────────────────────────────────────
        new("doneTracking", "Klart & gamification", "true",
            "Markera aktiviteter som klara"),
        new("doneExpiryDays", "Klart & gamification", "7",
            "Antal dagar innan \"klar\"-status återställs (heltal, inte en bool)"),
        new("doneLabelKey", "Klart & gamification", "\"done.markDone\"",
            "Translation-nyckel för \"markera klar\"-knappen"),
        new("hideDoneLabelKey", "Klart & gamification", "\"done.hideRecent\"",
            "Translation-nyckel för \"dölj nyligen klara\"-växeln"),
        new("streakTracking", "Klart & gamification", "null",
            "Streak-räknare (null = inaktivt)",
            WhenHow: "Sätts som ett OBJEKT `{ unit: \"daily\"|\"weekly\"|\"monthly\", gracePeriodHours, showCounter, labelKey }`, aldrig bara true/false."),
        new("badges", "Klart & gamification", "false",
            "Badge-system med toast-notis vid upplåsning",
            Requires: "En `badges`-array på PACK.CONFIG-TOPPNIVÅ ({ id, emoji, nameKey, descriptionKey, trigger }) — den här flaggan är bara på/av-växeln. Trigger-typer: doneCount/streakCount/categoryDone/quizPerfect/quizCompleted (se BADGE-TRIGGERS nedan)."),
        new("weeklyGoal", "Klart & gamification", "0",
            "Veckomål, antal aktiviteter (0 = inaktivt)"),
        new("levelBadges", "Klart & gamification", "false",
            "Visuell nivå-indikator baserat på Activity.level (1–3)",
            Requires: "Fungerar bara meningsfullt när flera aktiviteter faktiskt har `level` satt till olika värden i Fas 2 — annars ligger alla på samma nivå."),

        // ── Favoriter ────────────────────────────────────────────────────────
        new("favorites", "Favoriter", "true",
            "Favoritmarkering på aktiviteter"),
        new("favoritesShelf", "Favoriter", "true",
            "Favoriternas hylla högst upp på startsidan"),
        new("longPressFavorite", "Favoriter", "true",
            "Långtryck på ett kort favoritmarkerar det direkt, utöver ev. synlig knapp"),
        new("favoritesLabelKey", "Favoriter", "\"common.favorites\"",
            "Translation-nyckel för favoritrubriken"),

        // ── Loggbok, röst & export ───────────────────────────────────────────
        new("logbook", "Loggbok, röst & export", "false",
            "Foto + anteckning sparas per avklarad aktivitet"),
        new("activityVoiceNotes", "Loggbok, röst & export", "false",
            "Personlig röstanteckning per AKTIVITET, oberoende av loggboken",
            AlternativeTo: "`voiceRecorder` (fristående inspelningslista i overflow-menyn utan koppling till någon aktivitet). Välj activityVoiceNotes/logbookVoiceNotes för packs byggda kring diskreta aktiviteter — voiceRecorder bara för packs UTAN aktivitetsmodell att fästa en inspelning vid (t.ex. ett rent reflektions- eller dagbokspack). Blanda inte båda modellerna i samma pack."),
        new("logbookVoiceNotes", "Loggbok, röst & export", "false",
            "Röstanteckningar (flera) kan bifogas en sparad LOGGBOKSPOST, när som helst — inte bara vid avklarande",
            Requires: "features.logbook=true."),
        new("printView", "Loggbok, röst & export", "true",
            "Utskrift av aktivitetskort"),
        new("export", "Loggbok, röst & export", "null",
            "PDF-export av loggboken (null = inaktivt)",
            WhenHow: "Sätts som objekt: `{ enabled, logbook: { photoLayout, includeNotes, includeDate }, branding: { primaryColor, accentColor, fontFamily } }`."),
        new("printColumns", "Loggbok, röst & export", "1",
            "1 eller 2 kolumner vid utskrift av kort"),

        // ── Tillgänglighet & anpassning ──────────────────────────────────────
        new("fontSizeScale", "Tillgänglighet & anpassning", "false",
            "Textstorleksreglage",
            WhenHow: "Rekommenderas starkt för packs med äldre målgrupp eller där tillgänglighet är uttalat viktigt."),
        new("ageAdaptedSteps", "Tillgänglighet & anpassning", "false",
            "Steg/beskrivning kan variera per åldersgrupp via Activity.stepsPerAge/descriptionPerAge",
            Requires: "Ett filter (t.ex. \"ageGroup\") vars options-`value` matchar nycklarna i stepsPerAge, t.ex. \"4-6\"/\"7-10\"."),
        new("showEmoji", "Tillgänglighet & anpassning", "true",
            "Visa emoji som ikon på kort/kategorier",
            WhenHow: "Sätt false för en renare, mer vuxen/seriös ton (rent textbaserat) — annars lämna på."),
        new("voiceRecorder", "Tillgänglighet & anpassning", "false",
            "Fristående röstinspelningslista i overflow-menyn, utan koppling till någon aktivitet",
            AlternativeTo: "Se activityVoiceNotes/logbookVoiceNotes ovan — välj EN av de två modellerna för röst, inte båda."),

        // ── Innehållsversioner ───────────────────────────────────────────────
        new("packVersioning", "Innehållsversioner", "false",
            "Visar en \"uppdaterad\"-badge när Activity.contentVersion ökat sedan senast avklarad"),

        // ── Layout & navigering (v6) ─────────────────────────────────────────
        new("defaultLayoutMode", "Layout & navigering", "\"card\"",
            "Standardvy: \"card\" | \"list\" | \"mosaic\" | \"timeline\" | \"table\"",
            WhenHow: "Använd ALDRIG \"grid\" i nya packs — det är en legacy-alias som normaliseras till \"card\" bakom kulisserna, inte ett eget läge. \"timeline\"/\"table\" sätts normalt per kategori via categoryLayouts snarare än som global default."),
        new("layoutUserToggle", "Layout & navigering", "true",
            "Låt användaren växla mellan lägena i `ui.availableLayouts`"),
        new("densityUserToggle", "Layout & navigering", "false",
            "Låt användaren växla radtäthet i listvy",
            WhenHow: "När false (default) styr pack-författarens `ui.density` ensam och kan inte ändras av användaren."),
        new("cardActions", "Layout & navigering", "false",
            "Snabbknappar (bara timer/link, en delmängd av activityActions) direkt på korten"),

        // ── Användardata (v6) ────────────────────────────────────────────────
        new("allowUserContent", "Användardata", "false",
            "Användaren kan skapa egna aktiviteter i appen",
            WhenHow: "Lagras lokalt, taggas `source: \"user\"`."),
        new("activityNotes", "Användardata", "false",
            "Fritextfält i detaljvyn, alltid synligt — oberoende av loggboken"),
        new("adminEditorFields", "Användardata", "null",
            "Begränsar vilka fält en admin-redigerare får ändra (null = alla)",
            WhenHow: "Lista av strängar ur EXAKT denna mängd: \"emoji\",\"title\",\"description\",\"category\",\"level\",\"prepTime\",\"requiresProps\",\"props\",\"steps\",\"eventDate\"."),

        // ── Progression (v6) ─────────────────────────────────────────────────
        new("progressionLock", "Progression", "false",
            "Moduler med låsta aktiviteter i ordning",
            Requires: "En `modules`-array på toppnivå ({ id, titleKey, emoji, activityIds, unlockAfter }), `unlockAfter: \"previous\"` på alla utom den första."),
        new("smartFilters", "Progression", "false",
            "Beräknade filter med uttryckssyntax",
            WhenHow: "`type: \"computed\"` + `expression`, t.ex. `not_done AND filter:requiresProps:false` eller `category:{id}` / `tag:{slug}`."),

        // ── Påminnelser (v6) ─────────────────────────────────────────────────
        new("reminders", "Påminnelser", "null",
            "Push-påminnelse (null = inaktivt)",
            WhenHow: "Sätts som objekt: `{ enabled, time: \"HH:mm\", frequency: \"daily\"|\"weekly\", titleKey, bodyKey }`."),

        // ── Data & backup (v6) ───────────────────────────────────────────────
        new("dataSync", "Data & backup", "\"none\"",
            "\"none\" | \"export-only\"",
            WhenHow: "Riktig molnsynk (delade listor, realtid) kräver ett `backends`-objekt på toppnivå (se EXTRA TOPPNIVÅFÄLT nedan), inte den här strängen — den styr bara export-fallback."),

        // ── Bakre poster / Backend Record Panel (v15) ───────────────────────
        new("backendRecordPanel", "Data & backup", "false",
            "Generisk panel i detaljvyn kopplad till en post i pack-teamets egen backend, per aktivitet",
            Requires: "Ett `backends`-objekt på toppnivå (se EXTRA TOPPNIVÅFÄLT nedan) OCH `backendRecordPanelFields` (nedan) — annars finns inget att rendera. Domänagnostisk: fältLISTAN är det som gör den passa en viss pack (kontaktuppgifter, medlemsstatus, journalanteckningar, vad som helst), inte klientkoden.",
            WhenHow: "Renderas för en aktivitet bara om den aktiviteten har `backendRef` satt OCH en session finns för den refererade backend:en. Fälten är redigerbara i appen och sparas tillbaka via samma backend."),
        new("backendRecordPanelPath", "Data & backup", "null",
            "URL-segment posterna ligger under, relativt backend:ens baseUrl, t.ex. \"roster\" → GET/PUT {baseUrl}/roster/{activityId}",
            WhenHow: "Standard \"records\" om utelämnad."),
        new("backendRecordPanelTitleKey", "Data & backup", "null",
            "Översättningsnyckel för panelens rubrik",
            WhenHow: "Faller tillbaka till en generisk \"Detaljer\"-sträng om utelämnad."),
        new("backendRecordPanelFields", "Data & backup", "null",
            "Fältdefinitionerna panelen faktiskt ritar upp och redigerar (null/tom = panelen renderas aldrig, även om backendRecordPanel är true)",
            WhenHow: "Lista av objekt: `{ key, label, multiline?, sensitive? }`. `key` är dictionary-nyckeln på tråden (BackendRecord.Fields[key]) och måste matcha vad pack-teamets egen server implementerar — hitta ALDRIG på fältnamn, fråga efter den riktiga server-kontraktet om den inte är given. `label` är en färdig sträng (INTE en översättningsnyckel — samma undantag som t.ex. QuickActionConfig.label). `multiline`/`sensitive` är bara visningshintar; `sensitive` maskerar INTE data på servern, det är enbart klientens ansvar att inte visa den slarvigt."),

        // ── Lägg till post via BackendRecordPanel (v16, 2026-07-12) ──────────
        // Tillägg till samma feature-familj som ovan, inte en ny — låter
        // pack-teamet skapa NYA poster (t.ex. "lägg till spelare"), inte
        // bara redigera befintliga. Upptäckt saknas här och i PackWizards
        // egen FeatureManifest.cs under 2026-07-13 års modell-synk — se
        // GAPS.md/README-LEVERANS.md för bakgrunden; porta gärna samma tre
        // fält dit också.
        new("backendRecordEntityCategory", "Data & backup", "null",
            "Category nya poster får när de skapas via tillägg-dialogen, så de dyker upp i pack:ets befintliga lista tillsammans med redan existerande poster",
            Requires: "backendRecordPanel (samma feature-familj) — annars finns ingen panel att lägga till poster ifrån.",
            WhenHow: "Sätts till en av pack:ets egna `categories`-id. Null/tom = tillägg-dialogen visas inte alls. Domänagnostisk precis som resten av familjen — inget här är specifikt för \"spelare\"."),
        new("backendRecordAddTitleKey", "Data & backup", "null",
            "Översättningsnyckel för tillägg-dialogens rubrik (t.ex. \"Lägg till spelare\")",
            WhenHow: "Faller tillbaka till en generisk \"Lägg till post\"-sträng om utelämnad."),
        new("backendRecordAddFieldLabelKey", "Data & backup", "null",
            "Översättningsnyckel för det enda fältets etikett i tillägg-dialogen (namn/titel, t.ex. \"Spelarens namn\")",
            WhenHow: "Faller tillbaka till en generisk \"Namn\"-sträng om utelämnad. Dialogen tar bara emot namnet — resten av posten (kontaktuppgifter, anteckningar, ...) fylls i efteråt via samma BackendRecordPanel-redigering som en redan existerande post."),
    ];

    // ── Content-block-typer (10 st, exakt verifierade mot Activity.cs/ContentBlockRenderer.razor) ──

    public static readonly IReadOnlyList<ContentBlockType> ContentBlockTypes =
    [
        new("youtube", "videoId, caption?",
            "Inbäddad YouTube-video med samtyckesspärr (laddar inte iframe förrän användaren trycker)"),
        new("image", "src, alt?, caption?",
            "En enskild bild.",
            "src kan vara en lokal sökväg ELLER en absolut https://-URL (hotlinkas direkt). Trasig bild får automatisk fallback-styling i appen, men hitta ändå ALDRIG på en påhittad URL."),
        new("gallery", "items: GalleryImage[], layout?",
            "Flera bilder i karusell (default) eller rutnät",
            "items är samma form som Activity.images: `{ src, alt, caption, credit }`. `layout`: \"carousel\" | \"grid\"."),
        new("link", "url, label, icon?, caption?",
            "Extern länk som en knapprad med pil-ikon"),
        new("audio", "src, caption?",
            "Ljudklipp med inbyggd spelare (skilt från Activity.audio/AudioPlayer-widgeten — det här är ett fristående block mitt i innehållet)"),
        new("pdf", "src, label",
            "Länk till ett PDF-dokument, öppnas i ny flik"),
        new("webembed", "url, caption?, label?",
            "Sandboxad iframe för valfri extern sida, samtyckesspärrad precis som youtube/map",
            "Använd hellre \"link\" om innehållet inte behöver visas inbäddat — webembed har högre integritets-/prestandakostnad."),
        new("text", "caption",
            "Ett fristående textstycke mitt i innehållsblocken (caption-fältet ÄR brödtexten här, inte en bildtext)"),
        new("divider", "(inga fält)",
            "Visuell avdelare (horisontell linje) mellan block"),
        new("map", "lat, lng, locationLabel?, mapProvider?",
            "Interaktiv karta (Leaflet/OpenStreetMap), samtyckesspärrad — laddar inte kartan förrän användaren trycker \"Visa karta\"",
            "ANVÄND DEN HÄR när en aktivitet handlar om en konkret verklig plats: en adress, en namngiven sevärdhet, strand, led, byggnad eller motsvarande. Fyll i ditt bästa kända lat/lng för platsen — ungefärligt på stadsdels-/sevärdhetsnivå om du inte är säker på exakta koordinater, men hitta ALDRIG på falskt precisa koordinater bara för att de ser trovärdiga ut. `locationLabel` är texten i kart-nålens popup (faller tillbaka till `caption` om utelämnad). `mapProvider` är valfri, default \"osm\"."),
    ];

    // ── CSS-variabler (verifierade mot den buntade referens-packen "demo") ──
    //
    // Källa: src/Playtypus.Content/wwwroot/packs/demo/theme.css + theme-dark.css
    // — den enda pack i repot som med säkerhet renderar korrekt (den är den
    // hand-kuraterade referensen bunten själv skeppar med). De ~102 unika
    // var(--x)-referenserna som förekommer NÅGONSTANS i Core/wwwroot/css/*.css
    // är en mycket brusigare, delvis inbördes oense superset (t.ex.
    // --color-bg/--color-text-primary/--radius-md vid sidan av de riktiga
    // --color-background/--color-text/--border-radius-md) — troligen rester
    // från olika CSS-filer skrivna vid olika tillfällen, flera med egna
    // var(--x, fallback)-defaultvärden så de kraschar inte om temat saknar
    // dem. demo-packens 50 variabler är den bekräftat KORREKTA, avsiktliga
    // kontraktet — det är den listan som skrivs ut i prompten.
    public static readonly IReadOnlyList<CssVariable> CssVariables =
    [
        // Färger — kontrastkritiska, se RenderCssVariableChecklist för parregler
        new("--color-primary",        "Primärfärg — knappar, aktiva flikar, ikoner",              true),
        new("--color-primary-dark",   "Mörkare nyans av primärfärgen — hover/aktiva tillstånd",    true),
        new("--color-primary-light",  "Ljusare nyans av primärfärgen — ghost-bakgrunder, chips",   true),
        new("--color-secondary",      "Sekundärfärg",                                              true),
        new("--color-secondary-light","Ljusare nyans av sekundärfärgen",                           true),
        new("--color-accent",         "Accentfärg — highlights, badges",                          true),
        new("--color-background",     "Appens bakgrund",                                           true),
        new("--color-surface",        "Kortytor, paneler",                                         true),
        new("--color-surface-alt",    "Alternativ ytfärg — omväxling i listor",                    true),
        new("--color-surface-warm",   "Varm ytvariant — t.ex. highlightade sektioner",              true),
        new("--color-text",           "Primär brödtext",                                           true),
        new("--color-text-muted",     "Dämpad/sekundär text — VANLIGASTE KONTRASTFELET, se regler", true),
        new("--color-text-on-primary","Text ovanpå --color-primary (t.ex. knapptext)",             true),
        new("--color-border",         "Ramar, avdelare",                                           true),
        new("--color-done",           "\"Klar\"-status, positiv bekräftelse",                       true),
        new("--color-warning",        "Varningstillstånd",                                         true),
        new("--color-danger",         "Destruktiva åtgärder, felmarkeringar",                      true),
        new("--color-error",          "Feltexter/felmeddelanden",                                  true),
        new("--color-panic-glow",     "Glow-effekt runt panikknappen",                             true),

        // Typografi — delas mellan ljust/mörkt (ärvs, redefiniera INTE i theme-dark.css)
        new("--font-family-heading", "Typsnitt för rubriker",                    false),
        new("--font-family-body",    "Typsnitt för brödtext",                    false),
        new("--font-size-xs",  "Minsta textstorleken i skalan",  false),
        new("--font-size-sm",  "Liten text",                     false),
        new("--font-size-md",  "Baständ (matchar Grundstorlek i brief)", false),
        new("--font-size-lg",  "Stor text",                      false),
        new("--font-size-xl",  "Större rubriktext",              false),
        new("--font-size-2xl", "Stor rubrik",                    false),
        new("--font-size-3xl", "Störst — hero/toppnivårubrik",   false),

        // Spacing — delas mellan ljust/mörkt
        new("--spacing-xs",  "Minsta avstånd",  false),
        new("--spacing-sm",  "Litet avstånd",   false),
        new("--spacing-md",  "Baständ",         false),
        new("--spacing-lg",  "Stort avstånd",   false),
        new("--spacing-xl",  "Större avstånd",  false),
        new("--spacing-2xl", "Störst avstånd",  false),

        // Radie — delas mellan ljust/mörkt
        new("--border-radius-sm",   "Liten hörnradie — chips, mindre element",       false),
        new("--border-radius-md",   "Baständ — kort, knappar",                       false),
        new("--border-radius-lg",   "Stor radie — paneler",                          false),
        new("--border-radius-xl",   "Större radie — modaler, sheets",                false),
        new("--border-radius-pill", "Helt rundad — piller-knappar, badges",          false),

        // Skuggor — OMDEFINIERAS i mörkt (mörka ytor behöver ofta en annan skuggbehandling)
        new("--card-shadow",       "Standardskugga på kort",        true),
        new("--card-shadow-hover", "Skugga vid hover/tryck på kort", true),

        // Layout — delas mellan ljust/mörkt
        new("--app-max-width", "Maxbredd på appens innehållsyta (desktop)", false),
    ];

    // ── App-typ-presets — vilka features hör ihop för vilken sorts pack ────

    public static readonly IReadOnlyList<AppTypePreset> AppTypePresets =
    [
        new("places-outdoor", "🗺️", "Platser / friluftsliv / utflykter",
            "packs där aktiviteter är knutna till konkreta fysiska platser (badplatser, vandringsleder, sevärdheter, kommun-/turistguider)",
            "contentBlocks + map-block per aktivitet, heroImages, imageGallery, galleryLightbox, shareActivity, cardThumbnails",
            "Användaren kan se VAR platsen är utan att lämna appen, och dela ett tips direkt. Se CONTENT-BLOCK-PALETT → \"map\" för hur koordinater ska anges.",
            "ui.homeLayout: \"map\" (se LAYOUT-STIL-KATALOGEN), ui.headerStyle: \"image\" med en stämningsbild, ui.cardStyle: \"default\" eller \"wall\" om många aktiviteter har foton."),
        new("recipes", "🍲", "Recept / matlagning",
            "steg-för-steg-instruktioner som ska följas i realtid i köket",
            "guidedMode, printView, doneTracking, favorites",
            "Guidat läge håller fokus på ett steg i taget med händerna fulla; utskrift är vanligt för recept man vill ha på köksbänken.",
            "ui.headerStyle: \"image\" (en aptitretande bild sätter tonen direkt), ui.cardStyle: \"wall\" eller \"magazine\" om varje rätt har ett foto."),
        new("city-guide", "🏙️", "Stadsguide / turism",
            "sevärdheter och rekommendationer i en stad eller region",
            "contentBlocks + map-block, heroImages, categoryTabs, situationPresets, multiLanguage",
            "Samma resonemang som Platser-presetet ovan, men ofta med fler kategorier (mat/kultur/natur) och flerspråkighet för besökare.",
            "ui.homeLayout: \"map\" eller \"sections\" (en hylla per kategori: mat/kultur/natur), ui.categoryNavPosition: \"pinned\" — många kategorier man vill växla mellan snabbt."),
        new("daily-routine", "🔁", "Vardagsrutin / vanor",
            "återkommande, ofta dagliga eller veckovisa aktiviteter (t.ex. städning, egenvård, familjerutiner)",
            "streakTracking, doneTracking, weeklyGoal, reminders, badges",
            "Streak och veckomål ger den där \"håll igång\"-känslan; påminnelser knuffar användaren att faktiskt öppna appen.",
            "ui.homeLayout: \"today\" — visar bara det som faktiskt är kvar att göra idag, vilket är hela poängen med den här app-typen."),
        new("learning-quiz", "🧠", "Lärande / quiz / fakta",
            "kunskapsbaserat innehåll där användaren ska lära sig något",
            "quiz, badges (quizPerfect/quizCompleted-triggers), progressionLock, textToSpeech",
            "Progression låser upp nästa modul efter föregående — bra pedagogisk ordning. Quiz-badges belönar både deltagande och prestation.",
            "ui.homeLayout: \"sections\" om ämnet delar naturligt in sig i moduler/kategorier, annars \"feed\" i progressionsordning — undvik \"dashboard\"/\"magazine\", de passar dåligt med progressionLock."),
        new("reflection-journal", "📓", "Reflektion / dagbok / mental hälsa",
            "packs utan diskreta \"aktiviteter\" i vanlig mening — snarare fria anteckningar/inspelningar över tid",
            "voiceRecorder (INTE activityVoiceNotes/logbookVoiceNotes), activityNotes, logbook",
            "Det här är det EXPLICITA undantagsfallet där voiceRecorder (fristående inspelningslista) är rätt val istället för de aktivitetskopplade röstflaggorna — se \"AlternativeTo\"-noten på voiceRecorder i feature-listan.",
            "ui.headerStyle: \"transparent\", ui.density: \"comfortable\", defaultLayoutMode: \"timeline\" — en lugn, textfokuserad känsla snarare än ett kortrutnät."),
        new("kids-family", "🧒", "Barn / familj",
            "aktiviteter riktade till barn eller familjer med blandade åldrar",
            "ageAdaptedSteps, showEmoji=true, fontSizeScale, panicButton (alltid på — se schema), guidedMode",
            "ageAdaptedSteps kräver ett åldersfilter vars values matchar stepsPerAge-nycklarna (se Requires på den flaggan).",
            "ui.cardStyle: \"default\" med showEmoji på (INTE \"wall\"/\"magazine\" — text-lätta, tydliga kort passar bättre för barn), ui.density: \"comfortable\"."),
        new("elderly-accessible", "🦻", "Äldre / tillgänglighet i fokus",
            "packs där målgruppen uttryckligen är äldre eller har tillgänglighetsbehov",
            "fontSizeScale=true, showEmoji (överväg false för en lugnare, mer textbaserad känsla), textToSpeech, densityUserToggle",
            "fontSizeScale rekommenderas starkt (se WhenHow på den flaggan) snarare än att bara vara ett alternativ bland andra.",
            "ui.density: \"comfortable\" (ALDRIG \"minimal\" eller \"compact\" för den här app-typen), ui.gridStyle: \"single\", ui.detailStyle: \"fullscreen\" — mindre att hålla reda på samtidigt."),
    ];

    // ── Rendering ────────────────────────────────────────────────────────────

    /// <summary>
    /// Renderar features-objektets JSON-block, grupperat i samma ordning som
    /// kategorierna först förekommer i Features-listan. Ersätter den tidigare
    /// hårdkodade blocket i PromptGenerator.WriteSystemSchema.
    /// </summary>
    public static string RenderFeatureFlagsBlock()
    {
        var sb = new StringBuilder();
        string? lastCategory = null;

        foreach (var f in Features)
        {
            if (f.Category != lastCategory)
            {
                if (lastCategory != null) sb.AppendLine();
                sb.AppendLine($"    // ── {f.Category} ──");
                lastCategory = f.Category;
            }

            var line = $"    \"{f.Id}\": {f.DefaultJson},";
            sb.AppendLine($"{line.PadRight(34)} // {f.What}");
        }

        return sb.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>
    /// Renderar en prosa-lista över features som har ETT eller flera av
    /// WhenHow/Requires/AlternativeTo — dvs. de som faktiskt har en nyans,
    /// ett beroende eller ett alternativ värt att känna till. Trivialt
    /// självförklarande flaggor (t.ex. darkMode) tas INTE med här — de
    /// räcker med raden i JSON-blocket. Håller prompten fokuserad på det
    /// som faktiskt behöver förklaras istället för att skriva ut alla 56
    /// i fulltext.
    /// </summary>
    public static string RenderFeatureNotes()
    {
        var sb = new StringBuilder();
        foreach (var f in Features)
        {
            if (f.WhenHow == null && f.Requires == null && f.AlternativeTo == null) continue;

            sb.Append($"- **`{f.Id}`**");
            if (f.WhenHow != null) sb.Append($" — {f.WhenHow}");
            if (f.Requires != null) sb.Append($" Kräver: {f.Requires}");
            if (f.AlternativeTo != null) sb.Append($" Alternativ: {f.AlternativeTo}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd('\n', '\r');
    }

    public static string RenderContentBlockPalette()
    {
        var sb = new StringBuilder();
        foreach (var c in ContentBlockTypes)
        {
            sb.AppendLine($"- **`{c.Type}`** ({c.Fields}) — {c.What}");
            if (c.WhenHow != null)
                sb.AppendLine($"  {c.WhenHow}");
        }
        return sb.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>
    /// Skriver ut den fullständiga, exakta variabellistan grupperad efter
    /// om den ska omdefinieras i theme-dark.css eller ärvs från theme.css.
    /// </summary>
    public static string RenderCssVariableChecklist()
    {
        var sb = new StringBuilder();

        sb.AppendLine("**Måste omdefinieras i theme-dark.css (under `html.dark { }`) med EGNA mörka värden:**");
        foreach (var v in CssVariables.Where(v => v.RedefineInDark))
            sb.AppendLine($"- `{v.Name}` — {v.Role}");

        sb.AppendLine();
        sb.AppendLine("**Definieras EN gång i theme.css (`:root`), ärvs automatiskt av mörkt läge — upprepa INTE i theme-dark.css:**");
        foreach (var v in CssVariables.Where(v => !v.RedefineInDark))
            sb.AppendLine($"- `{v.Name}` — {v.Role}");

        return sb.ToString().TrimEnd('\n', '\r');
    }

    public static string RenderAppTypePresets()
    {
        var sb = new StringBuilder();
        foreach (var p in AppTypePresets)
        {
            sb.AppendLine($"- **{p.Emoji} {p.Name}** (`{p.Id}`) — {p.For}: {p.Features}");
            sb.AppendLine($"  _{p.Rationale}_");
            sb.AppendLine($"  Layoutförslag: {p.SuggestedUi}");
        }
        return sb.ToString().TrimEnd('\n', '\r');
    }
}

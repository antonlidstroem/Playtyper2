using System.Text;
using Playtyper.Shared.Services;

using Playtyper.Shared.Models;

namespace Playtyper.Shared;

/// <summary>
/// Converts a PackBrief into a two-phase AI prompt.
///
/// Phase 1 — pack.config.json + theme files (structure + styling)
/// Phase 2 — activities + translations (content, uses Phase-1 config as context)
///
/// Source material collected during WizardInterview is injected as a
/// dedicated section — Claude uses it as primary content reference.
///
/// The full feature schema is ALWAYS included so Claude can freely choose
/// any feature, not just those that appear in example packs.
///
/// FIX v11 (format-kontrakt):
///   Prompten begär nu kodblock med filnamnet som language-tag (```pack.config.json)
///   istället för === FILENAME: === / === END ===.
///   PackFileParser läser bara kodblock — det gamla formatet tolkades aldrig.
///
/// FIX v12 (nästlade schema-exempel saknades helt):
///   Rotorsak till gislaved-kommun-packet som fick 13 valideringsfel: KOMPLETT
///   SCHEMA visade categories/filters/situationPresets/panicButton som TOMMA
///   platshållare (`[]`/`{}`), och readyNow fanns inte med alls. Utan ett enda
///   konkret exempel att utgå från föll Claude tillbaka på det enda "options"-
///   mönster som faktiskt syntes i prompten — quizfrågornas platta strängarray
///   — och återanvände det för filter-options (som egentligen kräver
///   { value, labelKey }-objekt). Av samma anledning gissade Claude fältnamn
///   som "label"/"buttonText"/"filterValues" istället för de riktiga
///   "labelKey"/"sublabelKey"/"filterBundle" — inget i prompten sa att dessa
///   fält är översättningsnycklar, inte brödtext. Fixat genom att (1) visa
///   konkreta, korrekt formade exempel för alla fem nästlade objekten inkl.
///   readyNow, (2) en uttrycklig "⚠ KRITISKT"-ruta om nyckel-vs-text-
///   konventionen, (3) samma regler upprepade i WriteStructureSection oavsett
///   om användaren angav egna filter/kategorier eller lät AI:n välja fritt,
///   och (4) nya checklistpunkter i både Fas 1- och Fas 2-instruktionerna.
///   Se även Validator.cs — samma incident avslöjade att flera av dessa fel
///   (labelKey/filterBundle/scalar-i-filterValues/readyNow-utan-objekt) inte
///   fångades av valideringen alls, bara de tre "13 fel"-typerna. Utökade
///   kontroller tillagda där så nästa AI-genererade pack med samma missar
///   stoppas direkt istället för att se "giltigt" ut och gå sönder tyst i
///   produktion.
///
/// FIX v13 (2026-07, gap-analys — features/bilder/teman/kartor):
///   Rotorsak: feature-kunskap låg på tre ställen som drivit isär (den här
///   filens hårdkodade text, en fristående referensfil aldrig inläst av
///   någon kod, och den faktiska C#-modellen). 12 av 55 FeatureFlags-fält
///   nämndes ALDRIG (imageGallery/galleryLightbox/galleryBrowse/
///   activityVoiceNotes/logbookVoiceNotes/adminEditorFields/shareActivity/
///   levelBadges/showEmoji/voiceRecorder/densityUserToggle/logbookMultiPhoto),
///   noll CSS-variabelnamn eller kontrastregler nämndes trots att prompten
///   bad om "alla obligatoriska variabler", noll mentioner av `map` trots
///   att det är en färdig content-block-typ, och WebFetcher kastade bort
///   alla bild-URL:er från hämtat källmaterial (bara textnoder extraherades).
///   Bevis: två redan skeppade packs (samfunden, badplatserisverige) hade
///   varsin egen, sinsemellan olika FEL CSS-variabelkonvention.
///   Fixat genom FeatureManifest.cs (ny, enda sanningskällan — se den filen)
///   plus WebFetcher.ExtractImageCandidates (nytt). Se CHANGES.md för full
///   lista över ändrade/nya filer.
/// </summary>
public static class PromptGenerator
{
    public static string Generate(PackBrief b)
    {
        var sb = new StringBuilder();
        WritePhase1(sb, b);
        sb.AppendLine();
        sb.AppendLine(new string('─', 80));
        sb.AppendLine();
        WritePhase2Intro(sb, b);
        return sb.ToString();
    }

    /// <summary>
    /// Generates Phase 2 prompt. Call after Fas 1 files have been written
    /// (pass the saved pack.config.json content as phase1ConfigJson).
    /// </summary>
    public static string GeneratePhase2(PackBrief b, string phase1ConfigJson)
    {
        var sb = new StringBuilder();
        WritePhase2(sb, b, phase1ConfigJson);
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PHASE 1 — Config + Themes
    // ─────────────────────────────────────────────────────────────────────────

    private static void WritePhase1(StringBuilder sb, PackBrief b)
    {
        sb.AppendLine("# PLAYTYPUS PACK WIZARD — FAS 1: KONFIGURATION & TEMA");
        sb.AppendLine();

        // FIX v11: Kodblock-format med filnamnet som language-tag.
        // PackFileParser (strategi 1) läser ```pack.config.json direkt.
        // Det gamla === FILENAME === / === END ===-formatet parsades ALDRIG.
        sb.AppendLine("""
Du skapar nu FAS 1 av ett Playtypus-pack.

Fas 1 levererar: pack.config.json, theme.css, theme-dark.css
Fas 2 (separat anrop) levererar: activities.{lang}.json, translations.{lang}.json

Returnera ALLA Fas-1-filer som separata kodblock.
Använd filnamnet exakt som language-tag direkt efter de tre backticks:

```pack.config.json
{ ... }
```

```theme.css
:root { ... }
```

```theme-dark.css
html.dark { ... }
```

Inga förklaringar. Inga rubriker utanför kodblock. Inga kommentarer utanför filerna.
Exakt tre kodblock i exakt den ordningen — inget annat.

---
""");

        WriteSystemSchema(sb);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        WriteSourceMaterials(sb, b);
        WriteFreeformSection(sb, b);
        WriteIdentitySection(sb, b);
        WriteThemeSection(sb, b);
        WriteStructureSection(sb, b);
        WriteFeaturesSection(sb, b);
        WritePhase1Instruction(sb, b);
    }

    /// <summary>
    /// v4 — Användarens fritextbeskrivning (från "Snabb"-läge, eller från
    /// "fritt"-eskap i enskilda avsnitt under "Guidad"-läge). Skrivs med
    /// hög prioritet och tydlig instruktion om att Claude själv ska besluta
    /// kategorier/filter/funktioner/ton där de strukturerade fälten nedan är
    /// tomma eller generiska.
    /// </summary>
    private static void WriteFreeformSection(StringBuilder sb, PackBrief b)
    {
        if (string.IsNullOrWhiteSpace(b.FreeformNotes)) return;

        sb.AppendLine("## ANVÄNDARENS FRITEXTBESKRIVNING (hög prioritet — tolkas av dig)");
        sb.AppendLine();
        sb.AppendLine("""
> Användaren har beskrivit (delar av) appen med egna ord istället för att
> svara på varje strukturerad fråga individuellt. Använd texten nedan som
> PRIMÄR källa för ton, målgrupp, innehåll och funktionsval.
>
> Där ett strukturerat fält längre ner i den här prompten är tomt, generiskt
> eller saknas helt (t.ex. inga kategorier/filter angivna, ingen panikknapp-
> text) — fyll i det själv med ett rimligt, konkret val baserat på texten
> nedan. Hitta gärna på bra, specifika svenska namn/texter snarare än att
> lämna något bokstavligt tomt.
>
> Om texten nämner eller antyder funktioner som matchar flaggor i feature-
> schemat ovan (t.ex. loggbok, streak, badges, lösenordsskydd, påminnelser)
> — aktivera och konfigurera dem rimligt, även om inget strukturerat fält
> nedan bekräftar det.
""");
        sb.AppendLine();
        sb.AppendLine(b.FreeformNotes.Trim());
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void WriteSystemSchema(StringBuilder sb)
    {
        sb.AppendLine("## KOMPLETT SCHEMA FÖR PACK.CONFIG.JSON");
        sb.AppendLine();
        sb.AppendLine("""
> Det här är det FULLSTÄNDIGA schemat. Alla features är tillgängliga —
> välj de som passar brief:en. Att ett exempelpack inte använder en feature
> betyder INTE att du ska utesluta den.

### pack.config.json — OBLIGATORISKA toppnivåfält (måste finnas, exakta nyckelnamn)

> ⚠ KRITISKT: Använd EXAKT dessa nyckelnamn. Fel nyckelnamn (t.ex. "id" istället
> för "packId", eller "name" istället för "appName") gör att valideringen misslyckas.

```json
{
  "appName":         "Visningsnamn på appen (från brief:en, t.ex. \"Mästarnas mästare\")",
  "packId":          "kebab-case-id som matchar pack-mappen (t.ex. \"mastarnasmastare\")",
  "tagline":         "Kort beskrivning, 5–12 ord (hitta på om ej angiven)",
  "emoji":           "🎯",
  "version":         "1.0",
  "themeFile":       "theme.css",
  "themeDarkFile":   "theme-dark.css",
  "defaultLanguage": "sv",
  "languages": [
    { "code": "sv", "label": "Svenska", "flag": "🇸🇪" }
  ],
  "activityLanguages": ["sv"],
  "features": { "...": "se nedan" },

  // ⚠ Nedan är KONKRETA exempel på formen för varje nästlat objekt —
  // inte tomma platshållare. Kopiera formen exakt, byt bara ut innehållet.

  "categories": [
    { "id": "kebab-case-id", "labelKey": "category.kebab-case-id", "emoji": "🏰" }
  ],

  "filters": [
    { "id": "requiresProps", "type": "toggle", "labelKey": "filter.requiresProps" },
    { "id": "exempelFilter", "type": "segmented", "labelKey": "filter.exempelFilter", "options": [
        { "value": "Alternativ A", "labelKey": "filter.exempelFilter.alternativA" },
        { "value": "Alternativ B", "labelKey": "filter.exempelFilter.alternativB" }
      ] }
  ],

  "situationPresets": [
    { "id": "kebab-case-id", "labelKey": "situationPreset.kebab-case-id", "emoji": "⏱️",
      "filterBundle": { "ettFilterId": "ett-options-value" } }
  ],

  "panicButton": {
    "enabled": true,
    "style": "calm",
    "labelKey": "panic.button",
    "sublabelKey": "panic.subtitle"
  },

  "readyNow": {
    "labelKey": "readyNow.label",
    "sublabelKey": "readyNow.sublabel",
    "count": 3
  },

  "quickActions": [
    { "id": "kebab-case-id", "emoji": "✨", "labelKey": "quickActions.kebab-case-id",
      "style": "calm", "behavior": "openActivity", "targetActivityId": "ett-aktivitets-id" }
  ],

  "ui": {
    "headerStyle":      "surface",
    "cardStyle":        "default",
    "gridStyle":        "single",
    "detailStyle":      "sheet",
    "density":          "comfortable",
    "timelineGrouping": "month",
    "availableLayouts": ["card", "list"],
    "homeLayout":       "feed"
  }
}
```

> Fälten `appName`, `packId`, `tagline`, `defaultLanguage` och `languages` är
> OBLIGATORISKA. Utan dem misslyckas valideringen. Lägg till `languages`-arrayen
> alltid — ett objekt per språk med `code`, `label` och `flag`.

> `ui.availableLayouts` styr vyväxlaren. Giltiga värden just nu: `"card"`,
> `"list"`, `"mosaic"`. ⚠ Sätt INTE `"gallery"` här ännu — `features.galleryBrowse`
> och vyn den beskriver finns i schemat men är inte färdigkopplade i appen
> (2026-07), så den skulle bara falla tillbaka till kortvyn. Utelämnad eller
> null → växlaren visar bara `["card", "list"]`. `"timeline"`/`"table"` sätts
> INTE här utan per kategori via `categoryLayouts` (se KOMPLETT SCHEMA →
> features-objektet, `defaultLayoutMode`).

> `quickActions` (valfri, se exempel ovan) — generaliserar `panicButton`
> ("slumpa EN aktivitet") och `situationPresets` ("sätt flera filter") med
> fyra möjliga `behavior`-värden: `"randomFromPool"` (som panicButton, men
> med egen `filterBundle`), `"applyFilter"` (som en situationPreset),
> `"openActivity"` (hoppar rakt till en specifik aktivitets detaljvy via
> `targetActivityId`) och `"openCategory"` (byter till en kategori via
> `targetCategoryId`) — de sista två kan varken panicButton eller
> situationPresets göra. Helt additivt: en pack kan ha panicButton OCH
> situationPresets OCH quickActions samtidigt, eller bara någon av dem.
> Rendas som en knapprad direkt under panikknappen (QuickActionBar.razor).

> `ui.homeLayout` (valfri) — `"feed"` (default, utelämna helt om osäker) är
> dagens beteende: panikknapp/quickActions + Redo nu + det rullande
> aktivitetsflödet. `"dashboard"` byter ut startskärmen mot enbart
> QuickActions + kategoriplattor (DashboardHome.razor) — tänkt för paket
> byggda kring en handfull distinkta genvägar (typiskt 6–10 QuickActions),
> INTE för paket med en lång aktivitetslista att bläddra i. Använd bara
> `"dashboard"` när brief:en uttryckligen beskriver den typen av app.

### pack.config.json — EXTRA valfria toppnivåfält

> Allihop är säkra att UTELÄMNA — appen använder då ett neutralt default.
> Lägg bara till dem när brief:en faktiskt ger anledning.

```json
{
  "mascot": { "emoji": "🦆", "animation": "wobble" },

  "tutorial": [
    { "emoji": "👋", "titleKey": "tutorial.step1.title", "bodyKey": "tutorial.step1.body" }
  ],

  "typography": {
    "headingFont": "Playfair Display SC",
    "bodyFont": "Cormorant Garamond",
    "baseFontSize": 16,
    "googleFontsUrl": "https://fonts.googleapis.com/css2?family=...",
    "localFonts": false
  },

  "customCss": "custom.css",

  "backends": []
}
```

> `mascot` — toppnivåobjekt, definierat i schemat men ännu inte kopplat till
> någon synlig komponent i appen. Kostar inget att sätta en rimlig emoji,
> men lägg ingen extra kreativ vikt vid "animation"-värdet — bara "wobble" är
> bekräftat i bruk.
>
> `tutorial` — en fristående, återöppningsbar "så funkar appen"-genomgång
> (via hjälpknappen), SKILD från `onboarding` (som körs en gång vid första
> starten och kan koppla ett svar till ett filter via `prefKey`). Använd
> `tutorial` för valfri fördjupning, `onboarding` för det obligatoriska
> första-intrycket.
>
> `typography` — bara om brief:en efterfrågar specifika Google Fonts eller
> annan finjustering utöver Font-preset/Grundstorlek (se Brief: Tema nedan).
> Utelämnad → theme.css egna font-family-variabler styr helt, vilket räcker
> i de allra flesta fall.
>
> `customCss` — namnet på EN EXTRA CSS-fil i pack-mappen (utöver theme.css/
> theme-dark.css) för avancerade overrides. Skriv i så fall ut den filens
> fullständiga innehåll som ett eget kodblock (```custom.css) i Fas 1-svaret.
> Använd bara om den obligatoriska variabellistan (se Brief: Tema & typografi)
> uttryckligen inte räcker.
>
> `backends` — kräver en riktig körande server (Playtypus.Server eller
> kompatibelt API) att peka mot. Lämna som tom array `[]` om inte brief:en
> uttryckligen beskriver en befintlig backend att koppla mot — sätt ALDRIG
> ihop en påhittad baseUrl.

> ⚠ KRITISKT — översättningsnycklar, INTE brödtext: ETT fält som heter
> `labelKey`, `sublabelKey`, `titleKey` eller `bodyKey` (någonstans — categories,
> filters, situationPresets, panicButton, readyNow, onboarding, allt) ska ALLTID
> innehålla en översättningsnyckel-STRÄNG (t.ex. `"category.sevardheter"`),
> ALDRIG den faktiska visningstexten. Den riktiga texten hör hemma i
> translations.{lang}.json under exakt den nyckeln — det skapas i Fas 2.
> Fältet heter aldrig `label`, `text`, `buttonText` eller liknande även om det
> känns naturligt — kolla alltid exaktnamnet i schemat ovan. Skriver du texten
> direkt i pack.config.json istället för en nyckel blir fältet tomt i appen
> (ingen krasch, bara osynlig/blank text) eftersom appen läser upp nyckeln,
> inte fältet, mot translations-filen.

> ⚠ KRITISKT — filter.type och filter.options: `type` måste vara EXAKT
> `"segmented"`, `"toggle"` eller `"computed"` — ALDRIG `"select"`, `"multi"`,
> `"dropdown"` eller något annat, oavsett hur naturligt det känns för ett
> flervalsfilter. `"toggle"` har ingen `options`-lista. `"segmented"` MÅSTE ha
> `options` som en lista av OBJEKT `{ "value": ..., "labelKey": ... }` —
> ALDRIG rena strängar (`"options": ["Sommar", "Vinter"]` är fel form och
> kraschar appen vid körning). Det enda stället i hela schemat där `options`
> verkligen ska vara rena strängar är quiz-frågornas svarsalternativ
> (se aktivitetsschemat längre ner) — blanda inte ihop de två.

### pack.config.json — features-objektet (alla möjliga flaggor)

> Fullständig lista — alla 56 flaggor som finns i Playtypus.Core.Models.FeatureFlags.
> De flesta är självförklarande av kommentaren på samma rad. Ett urval har
> EXTRA vägledning (beroenden, alternativ, icke-uppenbart beteende) — se
> "Hur features hänger ihop" direkt efter blocket. Att ett exempelpack inte
> använder en flagga betyder INTE att du ska utesluta den.

```json
{
  "features": {
""");
        sb.AppendLine(FeatureManifest.RenderFeatureFlagsBlock());
        sb.AppendLine("""
  },

  // ── ExportConfig (om features.export != null) ──────────────────────────
  "features.export": {
    "enabled": true,
    "logbook": {
      "coverTitleKey":    "export.coverTitle",
      "coverSubtitleKey": "export.coverSubtitle",
      "photoLayout":      "grid",        // grid | full
      "includeNotes":     true,
      "includeDate":      true
    },
    "branding": {
      "primaryColor": "#HEX",
      "accentColor":  "#HEX",
      "fontFamily":   "Georgia, serif"
    }
  },

  // ── AuthConfig (lösenordsskydd) ───────────────────────────────────────
  "auth": {
    "enabled":      true,
    "passwordHash": "sha256-hex-lowercase",
    "sessionHours": 8,
    "hintKey":      "auth.hint"          // valfri, visas på inloggningssidan
  },

  // ── ReminderConfig (om features.reminders != null) ─────────────────────
  "features.reminders": {
    "enabled":   true,
    "time":      "08:00",               // HH:mm lokal tid
    "frequency": "daily",               // daily | weekly
    "titleKey":  "reminder.title",
    "bodyKey":   "reminder.body"
  },

  // ── CategoryLayouts (per-kategori layoutöverskridning) ─────────────────
  "categoryLayouts": [
    { "categoryId": "equipment", "layoutMode": "list" }
  ],

  // ── Modules (progressionslås, om features.progressionLock=true) ────────
  "modules": [
    {
      "id":          "module-1",
      "titleKey":    "module.intro",
      "emoji":       "📚",
      "activityIds": ["activity-id-1", "activity-id-2"],
      "unlockAfter": "none"             // none | previous | specific-activity-id
    }
  ]
}
```

### Hur features hänger ihop — beroenden, alternativ, icke-uppenbart

> Bara de flaggor som faktiskt har en nyans listas här — resten räcker med
> kommentaren i JSON-blocket ovan.

""");
        sb.AppendLine(FeatureManifest.RenderFeatureNotes());
        sb.AppendLine();
        sb.AppendLine("""
### App-typ — vanliga feature-kombinationer

> Utgångspunkter, inte facit. Brief:ens egna ord väger tyngre om de pekar åt
> ett annat håll.

""");
        sb.AppendLine(FeatureManifest.RenderAppTypePresets());
        sb.AppendLine();
        sb.AppendLine("""

### Aktivitetsschema — alla möjliga fält

```json
{
  "id":               "kebab-case-unikt",
  "title":            "Kort rubrik",
  "description":      "1–3 meningar",
  "steps":            ["Börja med verb. Komplett mening. Max 120 tecken."],
  "category":         "måste matcha category.id",
  "tags":             ["string"],
  "emoji":            "🎣",
  "prepTimeMinutes":  0,
  "requiresProps":    false,
  "props":            [],
  "filterValues":     { "filterId": "value" },  // ALLTID sträng-värden — ALDRIG true/false/tal. Upprepa INTE "requiresProps" här; appen läser aktivitetens egen "requiresProps"-fält ovan för det filtret, så nyckeln är överflödig och om den råkar bli en boolean kraschar hela activities.json vid inläsning.
  "safetyNote":       "Bara vid verklig säkerhetsrisk",
  "metadata":         { "valfriNyckel": "valfritt värde" },  // fritt, oanvänt av appens UI — bara om brief:en uttryckligen ber om spårbar egen data per aktivitet
  "durationSeconds":  300,
  "heroImage":        "img/file.jpg",   // ELLER en absolut https://-URL — se TILLGÄNGLIGA BILDER nedan
  "thumbnail":        "img/thumb.jpg",  // samma sak — lokal fil eller absolut https://-URL
  "audio":            "audio/file.mp3",
  "audioCredit":      "Inspelat av ...", // valfri attribuering, visas under ljudspelaren
  "contentBlocks":    [],               // se CONTENT-BLOCK-PALETT nedan — 10 typer, inte bara youtube/bild/länk
  "images":           [],               // galleri i detaljvyn — kräver features.imageGallery=true, se GalleryImage-formen i CONTENT-BLOCK-PALETT ("gallery")
  "actions":          [],
  "repeat":           "yearly|monthly|weekly|never",
  "contentVersion":   1,
  "stepsPerAge":      { "4-6": ["..."], "7-10": ["..."] },
  "descriptionPerAge": { "4-6": "...", "7-10": "..." },  // samma nyckelschema som stepsPerAge — ersätter "description", inte "steps"
  "showAfter":        "YYYY-MM-DD",
  "showBefore":       "YYYY-MM-DD",
  "eventDate":        "YYYY-MM-DD",     // datumaxeln i timeline-vyn — SKILT från showAfter/showBefore som styr synlighet, inte placering
  "nextActivity":     "aktivitets-id-för-serie",
  "level":            1,
  "guided":           false,            // per-aktivitet override av features.guidedMode — utelämna om den globala flaggan räcker
  "layoutHint":       "featured",       // "featured" | "compact" | utelämnad. Bara effekt i mosaik-vyn (ui.availableLayouts måste innehålla "mosaic") — se v6-avsnittet nedan för hur många aktiviteter som bör få vardera värdet

  // ── v6 ──────────────────────────────────────────────────────────────
  "cardActions":      [],               // subset av actions: bara timer och link
  "source":           "pack",           // "pack" (default) | "user" (sätts av appen)

  // ── v7 ──────────────────────────────────────────────────────────────
  "backendRef":       "backend-id",     // matchar ett id i toppnivåns "backends"-array — utelämna helt om paketet inte har en riktig backend att peka mot

  // ── v7: quiz ────────────────────────────────────────────────────────
  "quiz": {
    "shuffleQuestions":   false,        // slumpa frågornas ordning per omgång
    "shuffleOptions":     false,        // slumpa svarsalternativens ordning per fråga
    "showExplanations":   true,         // visa "explanation" efter varje svar
    "markDoneOnComplete": false,        // avklarat quiz markerar automatiskt aktiviteten som klar (loggbok/streak/badges)
    "questions": [
      {
        "id":           "q1",
        "question":     "Frågetext, en mening.",
        "options":      ["Alternativ A", "Alternativ B", "Alternativ C"],
        "correctIndex": 0,
        "explanation":  "Kort, valfri förklaring som visas efter svar.",
        "emoji":        "🧠"
      }
    ]
  },

  // ── v14 ─────────────────────────────────────────────────────────────
  "cardTemplate":     "profile"         // utelämnad/"standard" (default) | "profile" — se förklaring nedan
}
```

> **Om `quiz` används:** `correctIndex` MÅSTE vara ett giltigt index i `options`
> (dvs 0 ≤ correctIndex < options.length). Minst 2 `options` per fråga, inga
> identiska alternativ inom samma fråga. `quiz` är en HELT separat sak från
> `contentBlocks`/`actions` — lägg inte quiz-frågor där. Kräver
> `features.quiz` (default true, se schema ovan) — sätt den till false i
> pack.config.json bara om packen inte ska ha quiz alls.

> **Om `cardTemplate: "profile"` används:** byter kortFORMEN för just den
> aktiviteten till en portträtt+namn+roll-layout (ProfileCardVariant.razor)
> istället för standardkortet — tänkt för personer/kontaktkatalog-poster
> (t.ex. en kommuns kontaktsida), INTE för vanliga aktiviteter. Återanvänder
> samma fält, bara med annan betydelse: `title` → namn, `description` →
> roll/undertext, `heroImage`/första `images`-posten → porträtt (utelämnad →
> emoji, precis som standardkortet), `actions` av typen `"link"` → upp till
> tre snabbknappar (t.ex. `mailto:`/`tel:`/webbplats). Det finns ingen egen
> detaljvy-mall — ActivityDetail renderar samma fält som vanligt när kortet
> trycks. Sätt `prepTimeMinutes` till något ANNAT än exakt `0` (t.ex. `1`) om
> aktiviteten INTE ska kunna dyka upp i "Redo nu" eller panikknappens
> slumppool — annars behandlas kontaktkortet som en 0-minuters aktivitet att
> "slumpa fram". Om flera profilkort hör ihop (en hel katalog), ge dem en
> EGEN kategori snarare än att blanda in dem i en vanlig aktivitetskategori —
> annars räknas de in i den kategorins `categoryDone`-baserade badge.

### CONTENT-BLOCK-PALETT — alla 10 typer för `contentBlocks[]`

> `type` styr vilka övriga fält som gäller. Blanda valfritt i samma aktivitet
> (t.ex. ett `map`-block följt av ett `gallery`-block är helt normalt).

""");
        sb.AppendLine(FeatureManifest.RenderContentBlockPalette());
        sb.AppendLine();
        sb.AppendLine("""
> **Kartor används med fördel** — se `map` ovan. En aktivitet som nämner en
> konkret verklig plats (adress, sevärdhet, strand, led, byggnad) ska så gott
> som alltid få ett `map`-block, inte bara en textbeskrivning av var den ligger.
""");
    }

    private static void WriteSourceMaterials(StringBuilder sb, PackBrief b)
    {
        var sources = b.SourceMaterials;
        if (sources.Count == 0) return;

        sb.AppendLine("## KÄLLMATERIAL");
        sb.AppendLine();
        sb.AppendLine("""
> Nedanstående material är insamlat från användaren (webbsidor, inklistrad text m.m.).
> Använd det som PRIMÄR referens för aktiviteter, kategorier, terminologi och ton.
> Aktivitetstitlar, beskrivningar och steg ska i första hand bygga på detta material.
""");

        foreach (var (label, content) in sources)
        {
            sb.AppendLine($"### Källmaterial: {label}");
            sb.AppendLine();
            sb.AppendLine(content);
            sb.AppendLine();
        }

        WriteAvailableImagesSection(sb, b);

        sb.AppendLine("---");
        sb.AppendLine();
    }

    /// <summary>
    /// FIX (bildlänkar, 2026-07): tidigare försvann alla bild-URL:er från
    /// hämtat källmaterial redan i WebFetcher (bara textnoder extraherades).
    /// AI:n visste därför aldrig att en länkad sida hade bilder, och
    /// heroImage/thumbnail/images/contentBlocks blev antingen tomma eller
    /// fyllda med påhittade filnamn ingen sedan ersatte. Nu skickas riktiga,
    /// redan hittade bild-URL:er med explicit — se WebFetcher.
    /// ExtractImageCandidates och PackBrief.ImageCandidates.
    /// </summary>
    private static void WriteAvailableImagesSection(StringBuilder sb, PackBrief b)
    {
        var images = b.ImageCandidates;
        if (images.Count == 0) return;

        sb.AppendLine("### TILLGÄNGLIGA BILDER (hittade i källmaterialet ovan)");
        sb.AppendLine();
        sb.AppendLine("""
> Riktiga, redan hittade bild-URL:er från källmaterialets webbsidor — INTE
> platshållare. Använd dem direkt (absoluta URL:er hotlinkas av appen, ingen
> nedladdning behövs) för `heroImage`/`thumbnail`/`images[]`/content-block av
> typen `image`/`gallery` i de aktiviteter de passar bäst för — matcha via
> alt-texten och vilken sida bilden kom från. Sätt `alt`/`credit` på
> gallery-bilder där det är rimligt (t.ex. `"credit": "Källa: {sidnamn}"`).
>
> Färre bilder än aktiviteter? Återanvänd de mest passande hellre än att
> tvinga in en bild per aktivitet. Ingen kandidat passar en viss aktivitet?
> Lämna hero/thumbnail-fältet tomt för just den — HITTA ALDRIG på en egen,
> påhittad bild-URL. En trasig hotlink är sämre för användaren än ingen bild.

""");
        foreach (var img in images)
        {
            var altPart = string.IsNullOrEmpty(img.Alt) ? "" : $" — alt: \"{img.Alt}\"";
            sb.AppendLine($"- `{img.Url}`{altPart} (från: {img.SourceLabel})");
        }
        sb.AppendLine();
    }

    private static void WriteIdentitySection(StringBuilder sb, PackBrief b)
    {
        sb.AppendLine("## Brief: Identitet");
        sb.AppendLine($"- **App-namn:** {b.AppName}");
        sb.AppendLine($"- **Pack-ID:** {b.PackId}");
        sb.AppendLine($"- **Tagline:** {(string.IsNullOrWhiteSpace(b.Tagline) ? "(ej angiven — hitta på en kort, fyndig tagline utifrån tonen i texten ovan)" : b.Tagline)}");
        sb.AppendLine($"- **Emoji:** {b.Emoji}");
        sb.AppendLine($"- **Syfte:** {b.Description}");
        sb.AppendLine($"- **Målgrupp:** {(string.IsNullOrWhiteSpace(b.TargetAudience) ? "(ej angiven separat — härled från fritextbeskrivningen ovan)" : b.TargetAudience)}");
        sb.AppendLine($"- **Kontext:** {(string.IsNullOrWhiteSpace(b.UsageContext) ? "(ej angiven separat — härled från fritextbeskrivningen ovan)" : b.UsageContext)}");
        sb.AppendLine($"- **Ton:** {(string.IsNullOrWhiteSpace(b.Tone) ? "(ej angiven separat — härled från fritextbeskrivningen ovan)" : b.Tone)}");
        sb.AppendLine();
        sb.AppendLine("### Språk");
        sb.AppendLine($"- Standardspråk: {b.DefaultLanguage}");
        foreach (var l in b.Languages)
            sb.AppendLine($"- {l.Flag} `{l.Code}` — {l.Label}");
        sb.AppendLine();
    }

    private static void WriteThemeSection(StringBuilder sb, PackBrief b)
    {
        sb.AppendLine("## Brief: Tema & typografi");
        sb.AppendLine($"- Primärfärg:   `{b.PrimaryColor}`");
        if (!string.IsNullOrEmpty(b.SecondaryColor))
            sb.AppendLine($"- Sekundärfärg: `{b.SecondaryColor}`");
        if (!string.IsNullOrEmpty(b.AccentColor))
            sb.AppendLine($"- Accentfärg:   `{b.AccentColor}`");
        sb.AppendLine($"- Font-preset:  {b.FontPreset}");
        sb.AppendLine($"- Grundstorlek: {b.BaseFontSize}px");
        sb.AppendLine();
        sb.AppendLine("""
> theme.css definierar variablerna under `:root { }`. theme-dark.css
> definierar EN DELMÄNGD av samma variabler under `html.dark { }` — se exakt
> vilka i listan nedan. Komplettera paletten med lämpliga nyanser av de
> angivna färgerna.

### Obligatoriska CSS-variabler (exakta namn — hitta INTE på alternativa namn)

""");
        sb.AppendLine(FeatureManifest.RenderCssVariableChecklist());
        sb.AppendLine();
        sb.AppendLine($$"""

### Kontrastregler (kontrollera INNAN du svarar)

> WCAG 2.1 AA. Beräkna eller uppskatta kontrastkvot (ljusaste/mörkaste
> relativa luminans) för varje par nedan — i BÅDE theme.css och theme-dark.css
> separat (mörkt läge är inte bara "invertera", det är en egen palett som kan
> missa lika lätt som den ljusa).

- `--color-text` mot `--color-background` OCH mot `--color-surface`: ≥ {{ContrastChecker.MinRatioNormalText}}:1 (brödtext)
- `--color-text-muted` mot `--color-background` OCH mot `--color-surface`: ≥ {{ContrastChecker.MinRatioNormalText}}:1 — DET HÄR ÄR DET VANLIGASTE FELET. En dämpad textfärg som såg lagom ut i huvudet blir ofta för ljus (ljust tema) eller för mörk (mörkt tema) mot sin bakgrund. Välj en dämpning som fortfarande klarar gränsen, inte bara "lite svagare än --color-text".
- `--color-text-on-primary` mot `--color-primary`: ≥ {{ContrastChecker.MinRatioNormalText}}:1 (knapptext måste gå att läsa ovanpå knappfärgen)
- `--color-primary` mot `--color-background`/`--color-surface` (ikoner, ramar, aktiva flikmarkörer): ≥ {{ContrastChecker.MinRatioLargeTextOrUi}}:1
- `--color-danger`/`--color-warning`/`--color-error` mot sin egen bakgrund: ≥ {{ContrastChecker.MinRatioNormalText}}:1 — dessa bär betydelse (fel/varning), svag kontrast här är värre än kosmetiskt

> Om en föreslagen nyans inte klarar sin gräns: mörkna/ljusna INTE bara texten
> på måfå — justera i den riktning som ökar avståndet mot bakgrunden (mörkare
> text på ljus bakgrund, ljusare text på mörk bakgrund) tills kvoten klarar
> gränsen ovan.
""");
    }

    private static void WriteStructureSection(StringBuilder sb, PackBrief b)
    {
        sb.AppendLine("## Brief: Struktur");
        sb.AppendLine();

        sb.AppendLine("### Kategorier");
        if (b.Categories.Count > 0)
        {
            sb.AppendLine("Skapa dessa kategorier (plus `all` som alltid är med):");
            foreach (var cat in b.Categories)
                sb.AppendLine($"- {cat}");
        }
        else
        {
            sb.AppendLine("Inga angivna av användaren — välj själv 4–6 kategorier som passar " +
                          "syftet/målgruppen (se fritextbeskrivningen ovan om sådan finns). " +
                          "`all` ska alltid finnas som en implicit extra flik.");
        }
        sb.AppendLine("Varje kategori: `{ \"id\": ..., \"labelKey\": \"category.<id>\", \"emoji\": ... }` " +
                      "— fältet heter `labelKey`, INTE `label` (se KOMPLETT SCHEMA ovan).");
        sb.AppendLine();

        sb.AppendLine("### Filter");
        sb.AppendLine("`requiresProps` (toggle) finns alltid.");
        if (b.Filters.Count > 0)
        {
            sb.AppendLine("Lägg även till dessa filter:");
            foreach (var f in b.Filters)
                sb.AppendLine($"- **{f}** — bestäm 3–5 lämpliga alternativvärden");
        }
        else
        {
            sb.AppendLine("Inga ytterligare filter angivna av användaren — välj själv 2–4 lämpliga " +
                          "filterdimensioner (t.ex. säsong, svårighetsgrad, ålder, tid) baserat på syftet.");
        }
        sb.AppendLine("Alla filter utöver `requiresProps` (toggle): `type: \"segmented\"`, `labelKey` " +
                      "(INTE `label`), och `options` som en lista av `{ \"value\": ..., \"labelKey\": ... }`-" +
                      "objekt — ALDRIG rena strängar och ALDRIG `type: \"select\"`/`\"multi\"`/`\"dropdown\"` " +
                      "(se KOMPLETT SCHEMA och kritisk-rutan ovan för exakt form).");
        sb.AppendLine();

        if (b.SituationPresets.Count > 0)
        {
            sb.AppendLine("### Situationspresets");
            foreach (var p in b.SituationPresets)
                sb.AppendLine($"- {p}");
            sb.AppendLine("Varje preset: `{ \"id\": ..., \"labelKey\": \"situationPreset.<id>\", \"emoji\": ..., " +
                          "\"filterBundle\": { \"filterId\": \"value\" } }` — fältet heter `filterBundle`, " +
                          "INTE `filterValues` (det namnet är reserverat för aktiviteters egna filtervärden " +
                          "i Fas 2).");
            sb.AppendLine();
        }

        sb.AppendLine("### Panikknapp");
        if (!string.IsNullOrWhiteSpace(b.PanicButtonLabel))
        {
            sb.AppendLine($"- Text:     `{b.PanicButtonLabel}`");
            sb.AppendLine($"- Undertext: `{b.PanicButtonSubtitle}`");
            sb.AppendLine($"- Stil:     `{b.PanicButtonStyle}`");
        }
        else
        {
            sb.AppendLine("Ingen text angiven av användaren — hitta på en kort, träffande knapptext " +
                          "och undertext som matchar appens ton (stil: \"calm\" om inget annat passar bättre).");
        }
        sb.AppendLine("Texten ovan hör hemma i translations.{lang}.json under nycklarna `panic.button` och " +
                      "`panic.subtitle`. I pack.config.json sätter du bara " +
                      "`\"labelKey\": \"panic.button\", \"sublabelKey\": \"panic.subtitle\"` — fälten " +
                      "`buttonText`/`subtitleText` finns INTE i schemat.");
        sb.AppendLine();

        sb.AppendLine("### Redo nu");
        sb.AppendLine("`features.readyNowSection` är `true` som standard (se schema ovan). Lämnas den " +
                      "`true` MÅSTE pack.config.json även ha ett `readyNow`-objekt på toppnivå (samma form " +
                      "som i KOMPLETT SCHEMA) — annars renderas sektionen aldrig alls, trots att flaggan " +
                      "är på. Sätt hellre `readyNowSection: false` om sektionen inte passar packet.");
        sb.AppendLine();

        if (b.UseOnboarding)
        {
            sb.AppendLine("### Onboarding");
            sb.AppendLine("- Slide 1: info-slide (välkommen)");
            sb.AppendLine($"- Slide 2: {(b.UseMultiSelectOnboarding ? "multi-select" : "age-picker/toggle-picker")} för: {b.OnboardingQuestion}");
            sb.AppendLine("  `prefKey` ska matcha ett `filter.id` så onboarding-svaret sätter filtret automatiskt");
            sb.AppendLine();
        }
    }

    private static void WriteFeaturesSection(StringBuilder sb, PackBrief b)
    {
        sb.AppendLine("## Brief: Feature-flags");
        sb.AppendLine();
        if (b.IsQuickMode)
        {
            sb.AppendLine("> Detta pack skapades i \"Snabb\"-läge — nedanstående är bara DEFAULTVÄRDEN.");
            sb.AppendLine("> Om fritextbeskrivningen ovan nämner eller antyder någon av dessa funktioner,");
            sb.AppendLine("> ändra flaggan därefter istället för att följa listan blint.");
            sb.AppendLine();
        }
        sb.AppendLine("Sätt **exakt** dessa värden i `features`-objektet:");
        sb.AppendLine();
        sb.AppendLine($"- `contentBlocks`:   {b.UseContentBlocks.ToString().ToLower()}");
        sb.AppendLine($"- `activityActions`: {b.UseActionButtons.ToString().ToLower()}");
        sb.AppendLine($"- `heroImages`:      {b.UseHeroImages.ToString().ToLower()}");
        sb.AppendLine($"- `doneTracking`:    {b.UseDoneTracking.ToString().ToLower()}");
        sb.AppendLine($"- `logbook`:         {b.UseLogbook.ToString().ToLower()}");
        sb.AppendLine($"- `printView`:       {b.UsePrintView.ToString().ToLower()}");
        sb.AppendLine($"- `fontSizeScale`:   {b.UseFontSizeScale.ToString().ToLower()}");
        sb.AppendLine($"- `packVersioning`:  {b.UsePackVersioning.ToString().ToLower()}");
        sb.AppendLine($"- `badges`:          {b.UseBadges.ToString().ToLower()}");
        sb.AppendLine($"- `weeklyGoal`:      0");

        if (b.Streak != null)
        {
            sb.AppendLine($"- `streakTracking`:  {{ unit: \"{b.Streak.Unit}\", gracePeriodHours: {b.Streak.GracePeriodHours}, showCounter: true, labelKey: \"streak.label\" }}");
        }
        else
        {
            sb.AppendLine($"- `streakTracking`:  null");
        }

        if (b.Export != null)
        {
            sb.AppendLine();
            sb.AppendLine("### export-konfiguration");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"enabled\": true,");
            sb.AppendLine($"  \"logbook\": {{ \"photoLayout\": \"{b.Export.PhotoLayout}\", \"includeNotes\": true, \"includeDate\": true }},");
            sb.AppendLine("  \"branding\": { \"primaryColor\": \"" + b.PrimaryColor + "\", \"fontFamily\": \"Georgia, serif\" }");
            sb.AppendLine("}");
            sb.AppendLine("```");
        }

        if (b.Auth != null)
        {
            sb.AppendLine();
            sb.AppendLine("### auth (lösenordsskydd) — kopiera exakt");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"auth\": {");
            sb.AppendLine("    \"enabled\": true,");
            sb.AppendLine($"    \"passwordHash\": \"{b.Auth.PasswordHash}\",");
            sb.AppendLine($"    \"sessionHours\": {b.Auth.SessionHours}" +
                          (b.Auth.HintKey != null ? "," : ""));
            if (b.Auth.HintKey != null)
                sb.AppendLine($"    \"hintKey\": \"{b.Auth.HintKey}\"");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("```");
        }

        if (b.UseBadges)
        {
            sb.AppendLine();
            sb.AppendLine("### Badge-definitioner");
            sb.AppendLine("Skapa 4–6 badges. Tillgängliga trigger-typer:");
            sb.AppendLine("- `doneCount`      — threshold: antal avklarade aktiviteter");
            sb.AppendLine("- `streakCount`    — threshold: antal streak-perioder (bara om streak aktiverat)");
            sb.AppendLine("- `categoryDone`   — category: kategori-id (alla i kategorin avklarade)");
            sb.AppendLine("- `quizPerfect`    — threshold: antal quiz avklarade med full poäng (bara om quiz-aktiviteter finns)");
            sb.AppendLine("- `quizCompleted`  — threshold: antal quiz spelade minst en gång (bara om quiz-aktiviteter finns)");
        }

        // ── v6 feature flags ──────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("### v6-flaggor");
        sb.AppendLine($"- `defaultLayoutMode`: \"{b.DefaultLayoutMode}\"");
        sb.AppendLine($"- `layoutUserToggle`:  {b.LayoutUserToggle.ToString().ToLower()}");
        if (b.OfferMosaic)
            sb.AppendLine("- `availableLayouts`:  [\"card\", \"list\", \"mosaic\"]  ← lägg denna i `ui` i pack.config.json. Sätt `layoutHint: \"featured\"` på 3–6 aktiviteter du vill lyfta (blir dubbelt så stora i mosaikvyn) och `\"compact\"` på några snabba (blir mindre, ingen beskrivning). Lämna resten utan `layoutHint` — de blir normalstora.");
        sb.AppendLine($"- `cardActions`:       {b.UseCardActions.ToString().ToLower()}");
        sb.AppendLine($"- `allowUserContent`:  {b.AllowUserContent.ToString().ToLower()}");
        sb.AppendLine($"- `activityNotes`:     {b.UseActivityNotes.ToString().ToLower()}");
        sb.AppendLine($"- `progressionLock`:   {b.UseProgressionLock.ToString().ToLower()}");
        sb.AppendLine($"- `smartFilters`:      {b.UseSmartFilters.ToString().ToLower()}");
        sb.AppendLine($"- `dataSync`:          \"{b.DataSync}\"");

        if (b.Reminder != null)
        {
            sb.AppendLine();
            sb.AppendLine("### reminder-konfiguration");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"enabled\":   true,");
            sb.AppendLine($"  \"time\":      \"{b.Reminder.Time}\",");
            sb.AppendLine($"  \"frequency\": \"{b.Reminder.Frequency}\",");
            sb.AppendLine("  \"titleKey\":  \"reminder.title\",");
            sb.AppendLine("  \"bodyKey\":   \"reminder.body\"");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine("Skapa även translations-nycklar: `reminder.title` och `reminder.body`.");
        }

        if (b.UseProgressionLock)
        {
            sb.AppendLine();
            sb.AppendLine("### Moduler (progressionslås)");
            sb.AppendLine("Skapa 2–4 moduler som delar in aktiviteterna i en logisk ordning.");
            sb.AppendLine("Sätt `unlockAfter: \"previous\"` för alla moduler utom den första.");
            sb.AppendLine("Varje modul ska ha ett `titleKey` i translations-filen.");
        }

        if (b.UseSmartFilters)
        {
            sb.AppendLine();
            sb.AppendLine("### Smarta filter");
            sb.AppendLine("Skapa 1–3 filter med `type: \"computed\"` och ett `expression`-fält.");
            sb.AppendLine("Syntax: `not_done AND category:{id}` | `tag:{slug}` | `filter:{id}:{value}`");
            sb.AppendLine("Exempel: `{ \"id\": \"readyNow\", \"type\": \"computed\", \"expression\": \"not_done AND filter:requiresProps:false\" }`");
        }

        sb.AppendLine();
    }

    private static void WritePhase1Instruction(StringBuilder sb, PackBrief b)
    {
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Instruktion för Fas 1");
        sb.AppendLine();
        sb.AppendLine("Generera nu dessa tre filer och INGET annat:");
        sb.AppendLine("1. `pack.config.json`");
        sb.AppendLine("2. `theme.css`");
        sb.AppendLine("3. `theme-dark.css`");
        sb.AppendLine();

        // FIX v11: Påminn om kodblock-formatet precis innan AI:n svarar.
        sb.AppendLine("**Formatregel (obligatorisk):** Varje fil i ett eget kodblock med filnamnet som language-tag:");
        sb.AppendLine("````");
        sb.AppendLine("```pack.config.json");
        sb.AppendLine("{ ... }");
        sb.AppendLine("```");
        sb.AppendLine("```theme.css");
        sb.AppendLine(":root { ... }");
        sb.AppendLine("```");
        sb.AppendLine("```theme-dark.css");
        sb.AppendLine("html.dark { ... }");
        sb.AppendLine("```");
        sb.AppendLine("````");
        sb.AppendLine();
        sb.AppendLine("Inga rubriker, inga förklaringar utanför kodblock. Exakt tre kodblock.");
        sb.AppendLine();
        sb.AppendLine("Kontrollera innan du svarar:");
        sb.AppendLine("- [ ] `appName` finns (INTE \"name\" eller \"title\")");
        sb.AppendLine("- [ ] `packId` finns (INTE \"id\" eller \"pack_id\")");
        sb.AppendLine("- [ ] `tagline` finns (kort mening, 5–12 ord)");
        sb.AppendLine("- [ ] `defaultLanguage` finns");
        sb.AppendLine("- [ ] `languages` är en array med minst ett objekt { code, label, flag }");
        sb.AppendLine("- [ ] Alla features matchar brief:en exakt");
        sb.AppendLine("- [ ] Alla translationsnycklar som refereras i pack.config FINNS noterade (de genereras i Fas 2)");
        sb.AppendLine("- [ ] theme.css definierar ALLA obligatoriska CSS-variabler, med EXAKT de namn som listas ovan (hitta inte på egna namn)");
        sb.AppendLine("- [ ] theme-dark.css omdefinierar minst alla färgvariabler (`--color-*` + skuggorna) under `html.dark { }` — men INTE typografi/spacing/radie, de ärvs");
        sb.AppendLine("- [ ] Kontrastparen under \"Kontrastregler\" ovan klarar sin gräns i BÅDA theme.css och theme-dark.css, särskilt `--color-text-muted`");
        sb.AppendLine("- [ ] filter.id-värden är camelCase, category.id-värden är kebab-case");
        sb.AppendLine("- [ ] categories/filters/situationPresets/panicButton/readyNow använder `labelKey`" +
                      "/`sublabelKey` (ALDRIG `label`/`text`/`buttonText`/`subtitleText`), och varje sådan " +
                      "nyckel är en STRÄNG-nyckel, inte den faktiska texten");
        sb.AppendLine("- [ ] Alla filter utöver `requiresProps` har `type: \"segmented\"` (ALDRIG " +
                      "\"select\"/\"multi\"/\"dropdown\") och `options` som `{ value, labelKey }`-objekt " +
                      "(ALDRIG rena strängar)");
        sb.AppendLine("- [ ] Varje post i `situationPresets` har `filterBundle` (INTE `filterValues`)");
        sb.AppendLine("- [ ] Om `features.readyNowSection` är true (standard): ett `readyNow`-objekt finns " +
                      "på toppnivå i pack.config.json");
        sb.AppendLine("- [ ] Om brief:en/källmaterialet nämner konkreta verkliga platser: minst någon aktivitet " +
                      "har ett `map`-content-block med ett rimligt lat/lng (se CONTENT-BLOCK-PALETT)");
        sb.AppendLine("- [ ] Om TILLGÄNGLIGA BILDER fanns: de faktiskt använda — inte lämnade orörda med tomma " +
                      "bildfält eller ersatta med påhittade filnamn");
        sb.AppendLine();
        sb.AppendLine("Avsluta med en kort sammanfattning av dina val: använda features, kategorier, filter, presets.");
        sb.AppendLine("Jag klistrar sedan in den sammanfattningen i Fas 2.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PHASE 2 — Activities + Translations
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FIX v11: WritePhase2Intro genererar nu en komplett, korrekt Fas 2-prompt
    /// inklusive TARGET_ACTIVITY_COUNT substituerat, och med kodblock-formatregel.
    /// Tidigare var denna metod en halvfärdig platshållare-text som visades i
    /// prompt-filen men aldrig kopplades till den riktiga GeneratePhase2()-metoden.
    /// NewPackMode anropar GeneratePhase2() direkt efter att Fas 1-svaret är sparat.
    /// </summary>
    private static void WritePhase2Intro(StringBuilder sb, PackBrief b)
    {
        var langList = string.Join(", ", b.Languages.Select(l => $"`{l.Code}`"));
        var fileList = string.Join("\n", b.Languages.SelectMany(l => new[]
        {
            $"- `activities.{l.Code}.json`",
            $"- `translations.{l.Code}.json`",
        }));

        sb.AppendLine($"""
## REFERENS — Fas 2-prompt (kopieras automatiskt av PackWizard efter Fas 1)

Fas 2-prompten genereras av PackWizard när du klistrar in Fas 1-svaret.
Den kopieras till urklipp och innehåller pack.config.json som kontext.
Klistra in den i samma Claude-konversation som ett nytt meddelande.

Förväntade Fas 2-filer: {fileList.Replace("\n", "  ")}
Antal aktiviteter: {b.TargetActivityCount}
Språk: {langList}
""");
    }

    private static void WritePhase2(StringBuilder sb, PackBrief b, string phase1Config)
    {
        sb.AppendLine("# PLAYTYPUS PACK WIZARD — FAS 2: AKTIVITETER & ÖVERSÄTTNINGAR");
        sb.AppendLine();
        sb.AppendLine("## Fas 1-konfiguration (referens)");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(phase1Config);
        sb.AppendLine("```");
        sb.AppendLine();

        WriteSourceMaterials(sb, b);

        sb.AppendLine("## Brief: Aktiviteter");
        sb.AppendLine($"- **Antal:** {b.TargetActivityCount}");
        sb.AppendLine("- **Fördelning:** jämn fördelning över alla kategorier");

        if (b.ActivityExamples.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Exempelaktiviteter (matcha stil och detaljnivå)");
            foreach (var ex in b.ActivityExamples)
                sb.AppendLine($"- {ex}");
        }

        if (!string.IsNullOrEmpty(b.ContentConstraints))
        {
            sb.AppendLine();
            sb.AppendLine("### Särskilda krav");
            sb.AppendLine(b.ContentConstraints);
        }

        sb.AppendLine();

        // FIX v11: Kodblock-formatregel för Fas 2.
        sb.AppendLine("## Formatregel (obligatorisk)");
        sb.AppendLine();
        sb.AppendLine("Varje fil i ett eget kodblock med filnamnet som language-tag. Exempel:");
        foreach (var l in b.Languages)
        {
            sb.AppendLine($"````");
            sb.AppendLine($"```activities.{l.Code}.json");
            sb.AppendLine("[...]");
            sb.AppendLine("```");
            sb.AppendLine($"```translations.{l.Code}.json");
            sb.AppendLine("{{...}}");
            sb.AppendLine("```");
            sb.AppendLine("````");
        }
        sb.AppendLine();
        sb.AppendLine("Inga rubriker, inga förklaringar utanför kodblock.");
        sb.AppendLine($"Exakt {b.Languages.Count * 2} kodblock — ett per fil.");
        sb.AppendLine();

        sb.AppendLine("## Instruktion");
        sb.AppendLine();
        sb.AppendLine($"Generera alla filer för {b.Languages.Count} språk:");
        foreach (var l in b.Languages)
            sb.AppendLine($"- `activities.{l.Code}.json`  och  `translations.{l.Code}.json`");
        sb.AppendLine();
        sb.AppendLine("Kör checklistan nedan. Returnera bara filerna, inget annat.");
        sb.AppendLine();
        sb.AppendLine("## Checklista");
        sb.AppendLine();
        sb.AppendLine("- [ ] Varje filterValues-nyckel matchar ett filter.id i pack.config");
        sb.AppendLine("- [ ] Alla filterValues-VÄRDEN är strängar — aldrig `true`/`false`/tal. Ta INTE med " +
                      "\"requiresProps\" i filterValues alls (appen läser aktivitetens egna toppnivåfält " +
                      "`requiresProps` för det filtret; en boolean där kraschar HELA activities.json vid " +
                      "inläsning, inte bara den aktiviteten)");
        sb.AppendLine("- [ ] Varje category-värde matchar ett category.id i pack.config");
        sb.AppendLine("- [ ] requiresProps=true → props-listan är inte tom");
        sb.AppendLine("- [ ] Minst 3 aktiviteter har requiresProps=false och prepTimeMinutes=0 (panikknappens pool)");
        sb.AppendLine("- [ ] Alla aktiviteter har unika id:n och minst 3 steg");
        sb.AppendLine($"- [ ] Antal aktiviteter = {b.TargetActivityCount}");
        sb.AppendLine("- [ ] Alla aktiviteter har `\"contentVersion\": 1`");
        sb.AppendLine("- [ ] Om en aktivitet har `quiz`: varje frågas `correctIndex` är ett giltigt index i den frågans `options` (0 ≤ correctIndex < options.length)");
        sb.AppendLine("- [ ] Om en aktivitet har `quiz`: minst 2 `options` per fråga, inga identiska alternativ inom samma fråga");
        sb.AppendLine("- [ ] Translations-filen täcker ALLA nycklar i pack.config och aktiviteter");
        sb.AppendLine("- [ ] Om progressionLock=true: modules täcker alla aktiviteter, unlockAfter satt korrekt");
        sb.AppendLine("- [ ] Om reminders!=null: reminder.title och reminder.body finns i translations");
        sb.AppendLine("- [ ] Om smartFilters=true: computed-filter har giltig expression-syntax");
        sb.AppendLine("- [ ] Varje `contentBlocks[].type` är EXAKT en av de 10 i CONTENT-BLOCK-PALETT " +
                      "(youtube/image/gallery/link/audio/pdf/webembed/text/divider/map) — aldrig en påhittad typ");
        sb.AppendLine("- [ ] Varje `map`-block har rimliga `lat`/`lng` (riktiga koordinater eller en " +
                      "medveten approximation) — aldrig 0/0 eller en gissning som ser mer exakt ut än den är");
        sb.AppendLine("- [ ] `heroImage`/`thumbnail`/galleri-`src` är antingen en verklig lokal sökväg eller en " +
                      "URL från TILLGÄNGLIGA BILDER — aldrig ett påhittat filnamn som `img/aktivitet-1.jpg` " +
                      "utan att en sådan fil faktiskt beskrivs/finns");
    }
}

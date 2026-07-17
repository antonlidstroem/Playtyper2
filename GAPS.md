# Playtyper — återstående arbete

**Syfte med det här dokumentet:** en komplett, verifierad (inte ur minnet
— korsläst mot faktisk kod) lista över allt från den ursprungliga
strategin som ännu inte är byggt. Tänkt att ges tillsammans med
kodbasen till vem eller vad som helst som ska fortsätta arbetet, utan
att behöva den ursprungliga strategikonversationen som kontext.

Kompileringsstatus är medvetet inte ämnet här (se `README.md` för det) —
det här dokumentet handlar om **funktionalitet**, inte buggar. Ett par
genuina buggar upptäcktes ändå av misstag under det här arbetspasset
(inte via en avsiktlig felsökningsrunda) — de är noterade under
respektive avsnitt nedan istället för att gömmas i en egen sektion,
eftersom de dök upp exakt där man skulle stöta på dem igen.

**Sedan förra versionen av det här dokumentet (2026-07-13):** det här är
nu den enskilt största uppdateringen. Sedan förra passet (riktig preview,
avsnitt 2, samt driftsättning, avsnitt 5) har följande hänt, i den
ordning kodbasens ägare bad om dem:

1. **Playtypus.Core konsumeras nu via NuGet, inte en syskonmapps-
   ProjectReference** — matchar äntligen strategins "alternativ A" rakt
   av. Se `Playtyper.Shared.csproj`, `NuGet.config`, och det nya
   `.github/workflows/publish-nuget.yml` i huvudrepot.
2. **`Models/PackConfig.cs`, `Activity.cs`, `AuthConfig.cs`,
   `BackendConfig.cs`, `SharedModels.cs`, `Typography.cs` (Playtypers
   egna handkopior) är borttagna.** Playtyper.Shared använder
   `Playtypus.Core.Models` direkt (project-wide `<Using>` i csproj:et).
   `PreviewAdapter.cs` förenklad i samma veva — se den filens egen
   kommentar för vad som ändrades och varför JSON-omvägen ändå kvarstår
   (CSS-isolering, inte typkonvertering).
3. **Mobilpreview** — `PreviewPanel` gick inte att nå under 1400px bredd
   (varken mobil eller de flesta laptops). Fixat i `AppShell.razor`: en
   "Förhandsvisning"-flik i botten-nav/sidopanel öppnar den befintliga
   preview-mekaniken i ett fullskärmsläge under 1400px.
4. **Alla 16 fält från avsnitt 4:s ursprungliga lista har nu en egen
   editor** (se avsnitt 4 nedan för exakt vilken fil vardera hamnade i)
   — inte bara nåbara via Avancerat-fliken längre.
5. **Bundle-hantering, Radera pack, och Licensstatus/förnya/återkalla är
   byggda.** Se avsnitt 5 nedan för exakt scope och vad som medvetet
   lämnades kvar till PackWizard (licens-SKAPANDE, superadmin).

Som vanligt: skrivet och korsläst mot faktisk kod, men **inte
kompilatorverifierat** — ingen .NET SDK i den här miljön heller, precis
som förra passet. Kör `dotnet restore`/`build` i båda repona innan ni
litar på nåt av ovanstående.

---

## 1. Orientering — arkitekturen i korthet

Fullständig beskrivning finns i `README.md`, men i korthet:

```
src/Playtyper.Shared/   Razor Class Library — all UI och all logik bor här
src/Playtyper.Web/      Blazor WASM-värd (browser, ingen backend)
src/Playtyper.App/      MAUI Blazor Hybrid-värd (Android/Windows, samma UI-kod)
```

Ingen egen backend — GitHub är datalagret, allt går via `GitHubRepoService.cs`.
Redigering sker i minnet (`PackDraft.cs`) tills man explicit "Spara till GitHub".

**Nytt sedan förra versionen (2026-07-13):** `Playtyper.Shared` konsumerar
`Playtypus.Core` via ett vanligt versionerat NuGet-paket nu, inte längre en
syskonmapps-ProjectReference — se avsnitt 2. Inget syskonrepo behöver
checkas ut för att bygga längre; se `NuGet.config` och README:ts
"Konsumera Playtypus.Core" för engångs-PAT-uppsättningen.

De filer man i praktiken behöver öppna för att förstå helheten:

| Fil | Roll |
|---|---|
| `Services/GitHubOperator.cs` | Orkestrerar repo-skapande, ansluter, scaffoldar, **driftsättning (nytt)** |
| `Services/PackDraftStore.cs` | Läser/skriver ett helt pack (5 filer) till/från GitHub |
| `Services/PackDraft.cs` | Det redigerbara in-memory-tillståndet, diff-logik |
| `Services/PreviewAdapter.cs` | **Nytt.** Bryggan mellan PackDraft och Playtypus.Core:s in-memory-API |
| `Services/FeatureFlagsReflection.cs` | Kopplar `FeatureManifest` mot `FeatureFlags` via reflection |
| `Components/Screens/PackEditorPage.razor` | Navet — laddar draft, routar mellan flikarna |
| `Components/Screens/PreviewFramePage.razor` | **Nytt.** Sidan som faktiskt kör `<AppShell>`, laddas i en iframe |
| `Components/Screens/DeployPage.razor` | **Nytt.** Driftsättning — `/apps/deploy` |
| `Components/Preview/PreviewPanel.razor` | **Ombyggd.** Värdar iframen, inte längre en fasad |
| `Models/PackConfig.cs` | Hela pack-schemat — **uppdaterad, se avsnitt 3** |

---

## 2. ✅ Byggt: Riktig preview mot Playtypus.Core

Det här var kärnan i den ursprungliga strategin ("alternativ A": ladda in
`Playtypus.Core` direkt, inte en återimplementation). `PreviewPanel.razor`
kör nu en RIKTIG, levande `<AppShell>` — samma komponent som Playtypus.Web
använder — med paketet man just redigerar, live, utan att spara till GitHub
först.

### Vad som byggdes, konkret

**I Playtypus.Core** (additiva ändringar, den befintliga HTTP-baserade vägen
är orörd och fungerar exakt som förut):
- `PackContext.LoadFromMemoryAsync(...)` + `LoadActivitiesFromMemory()` —
  in-memory-motsvarigheten till `LoadPackAsync`.
- `LanguageService.InitFromMemory(...)` + `TrySetInMemoryLanguage(...)` —
  samma för översättningar, inklusive språkbyte inifrån preview:n.
- `ThemeService.ApplyInMemoryThemeAsync(...)` — injicerar tema-CSS direkt
  som text (via det redan existerande `playtypusTheme.injectCss`) istället
  för att peka en `<link>` mot en URL som inte finns för ett osparat utkast.
- `AppShell.LoadPack(...)` — en riktad fix, inte ett tillägg: metoden körde
  *alltid* sin egen HTTP-baserade `Pack.LoadPackAsync`/`Theme.ApplyThemeAsync`
  vid första rendering, oavsett om `Pack` redan var laddad. Utan fixen hade
  AppShells egen uppstart skrivit över allt PreviewFramePage just laddat in.
  Nu körs bara HTTP-vägen om `Pack.IsLoaded` var `false` när `LoadPack`
  anropades — allt annat i metoden (manifest, dark mode, a11y, loggbok,
  historik, v5/v6-init) körs precis som förut, oavsett källa.
- `PlaytypusCoreServiceCollectionExtensions.AddPlaytypusCore()` — de ~30
  `AddScoped`-raderna som låg direkt i `Playtypus.Web/Program.cs` flyttades
  hit, så att Playtyper (en ANNAN värd som nu också kör `<AppShell>`) inte
  behöver en andra, drivande kopia. `Playtypus.Web/Program.cs` anropar nu
  bara den delade metoden.

**I Playtyper:**
- `Playtyper.Shared.csproj` — `PackageReference` mot `Playtypus.Core`
  (versionerat NuGet-paket sedan 2026-07-13 — se "Varför inte NuGet?" nedan,
  som nu heter så av historiska skäl snarare än att beskriva nuläget).
- `Services/PreviewAdapter.cs` — serialiserar en `PackDraft` till JSON och
  postar den över iframe-gränsen. Fram till 2026-07-13 konverterade den
  även mellan två separata, nominellt olika C#-typer med samma form (se
  avsnitt 3) — den delen är borta nu, Playtyper.Shared har inga egna
  modellkopior längre, `PackDraft.Config` ÄR en `Playtypus.Core.Models.
  PackConfig`. Kvar är bara CSS-isoleringsskälet, se rubriken nedan.
- `Components/Screens/PreviewFramePage.razor` (`/preview-frame`) — en helt
  ochromad sida som bara kör `<AppShell>`. Renderas ENDAST i en iframe.
- `Components/Preview/PreviewPanel.razor` — värdar `<iframe src="/preview-frame">`
  och skickar en fräsch JSON-payload varje gång draften ändras (postMessage).
- `wwwroot/js/preview-bridge.js` — postMessage-bryggan mellan de två.
- `Program.cs` (Web) / `MauiProgram.cs` (App) — registrerar `AddPlaytypusCore()`
  + en `HttpClient`.

### Varför en iframe, inte en direkt komponent?

Det HÄR var den enskilt viktigaste designfrågan i hela det här arbetspasset,
och svaret är inte uppenbart förrän man faktiskt läser båda CSS-filerna
sida vid sida (vilket gjordes — inte antaget):

Playtypus.Core:s `ui.css` deklarerar bara-element-regler:
`body { margin:0; background:var(--color-background); ... }`, `button {}`,
`a {}`, `input,textarea,select {}`. Playtypers EGEN `app.css` deklarerar
EXAKT samma bara-selektorer för sitt eget gränssnitt, med andra
variabelnamn (`--color-bg` vs `--color-background` osv). Laddar man båda
i samma dokument vinner den som laddas sist ALLA sådana regler för HELA
dokumentet — varje knapp i Playtypers egen sidopanel, inte bara de i
förhandsvisningen. Det finns ingen CSS-scoping-knep som begränsar en
bar `body {}`/`button {}`-regel till en subträd.

En `<iframe>` är den enda mekanismen här som är GARANTERAD av webbläsaren,
inte av försiktig selektor-skrivning på ena sidan. Den pekar mot en andra
instans av SAMMA Playtyper-app (samma wasm, samma `Program.cs`) på en
annan route — data korsar gränsen som en JSON-sträng via `postMessage`,
inte ett direkt C#-anrop, eftersom de två dokumenten har separata
DI-containrar.

**Känd begränsning, inte ett fel:** eftersom `/preview-frame` är samma
`index.html`/samma wasm-app som resten av Playtyper (bara en annan route),
laddas Playtypers EGEN `app.css` också in i iframens dokument (statiskt,
via `index.html`) — bredvid Playtypus.Core:s CSS (dynamiskt injicerad, se
`preview-bridge.js`s `injectPlaytypusAssets`). Eftersom Playtypus-CSS:en
laddas SENARE (efter Blazor-uppstart) vinner den för delade bara-selektorer
— korrekt resultat i praktiken — men någon enstaka egenskap Playtyper sätter
och Playtypus INTE sätter om (t.ex. en `font-size` på `body` som Playtypus
ui.css aldrig rör) kan teoretiskt läcka igenom INUTI förhandsvisningen. Inte
ett problem utanför iframen (det var hela poängen), bara en liten,
ospårad kosmetisk risk inuti den.

### Varför inte NuGet, som ursprungsplanen sa? (historik — löst 2026-07-13)

Ursprungsplanen (P0, tidigare version av det här dokumentet) bad om ett
publicerat NuGet-paket från Playtypus CI. Det krävde två saker som inte
fanns tillgängliga i det arbetspasset: skrivåtkomst till en riktig
CI-pipeline, och nätverksåtkomst till en paketkälla (nuget.org/GitHub
Packages). Eftersom BÅDA repona (Playtyper och Playtypus) fanns tillgängliga
som fullständig källkod gick det att uppnå samma sak — riktiga komponenter,
inte en återimplementation — genom en `ProjectReference` istället för en
`PackageReference`, som en medveten, uttalat temporär avvägning.

**2026-07-13: gjort klart.** `.github/workflows/publish-nuget.yml` finns nu
i Playtypus-repot (packar och publicerar `Playtypus.Core` till GitHub
Packages vid varje push till main som rör `src/Playtypus.Core/**`).
`Playtyper.Shared.csproj` konsumerar paketet via `PackageReference`, med en
`NuGet.config` i repo-roten som pekar på samma flöde. Precis som förutspått
ovan var det en enrads-ändring i `.csproj` — `PreviewAdapter.cs`/
`PreviewPanel.razor` rördes inte alls för DEN HÄR ändringen (de rördes av
en annan, separat anledning — se avsnitt 3).

**Kvarstår, och kan bara göras av repots ägare (inte av nån som bara har
källkoden att läsa):**
- Fylla i `DITT_ORG` i `NuGet.config` (Playtyper) mot den riktiga
  GitHub-orgen.
- Lägga en `PLAYTYPUS_TOKEN`-secret i Playtyper-repot (en PAT med
  `read:packages` på Playtypus-repot) — krävs av `deploy-pages.yml` för att
  CI ska kunna göra `dotnet restore`.
- Låta `publish-nuget.yml` köra minst en gång (push till main i
  Playtypus-repot, eller `workflow_dispatch` manuellt) INNAN Playtyper
  försöker bygga — paketet måste faktiskt existera i flödet först. Version
  `1.0.0` i `Playtyper.Shared.csproj` är en platshållare; byt till vad
  `publish-nuget.yml` faktiskt producerade (loggen visar `1.0.<run_number>`)
  efter första lyckade körningen.
- Skapa en egen `PLAYTYPUS_NUGET_PAT`-miljövariabel lokalt för `dotnet
  restore` utanför CI (se `NuGet.config`s egen kommentar).

**2026-07-15 tillägg:** PAT:en ovan har ett utgångsdatum — när den går ut
slutar restore/CI fungera igen, av samma skäl som innan den fanns alls.
README:ts "Konsumera Playtypus.Core"-avsnitt har nu en "Token gick ut?"-
guide direkt under engångs-setupen (förnyelse via **Regenerate token**,
inte en helt ny token — samma scope följer med) med korta pekare från
`NuGet.config` och `deploy-pages.yml`s egna kommentarer till den. Inte
byggt som ett separat dokument — samma resonemang som `README.md`s
"Läs det här avsnittet först" redan följer: en sanning, breadcrumbs vid
felkällan istället för dupliceras.

### Kvarstår (mindre saker)

- Preview:ns iframe+postMessage-mekanism är verifierad mot Playtyper.Web
  (Blazor WASM) genom noggrann läsning av hela kedjan, men inte körd i
  praktiken (ingen .NET-kompilator var tillgänglig i det här arbetspasset —
  se README). Bör fungera identiskt på Playtyper.App (MAUI) — samma
  standard-HTML/JS-mekanism, och BlazorWebView är en standardkompatibel
  motor under huven — men det finns ingen MAUI-byggmiljö tillgänglig här
  för att bekräfta det. Värt en uttrycklig kontroll där innan man litar på
  det, se `MauiProgram.cs`s kommentar.
- Ingen debounce på synkroniseringen (skickar en fräsch payload vid VARJE
  ändring, per GAPS.md:s ursprungliga instruktion). Sannolikt inget
  praktiskt problem (ingen I/O inblandad, bara JSON-serialisering + en
  postMessage), men värt att hålla koll på om paket blir mycket stora.
- Pack-lokala relativa bildsökvägar (inte fulla URL:er) renderas inte i
  förhandsvisningen — Playtyper hanterar ingen binär asset-uppladdning,
  så det finns inget för `AssetUrlResolver` att slå upp. Förväntat, inte
  en regression; fulla URL:er (http/https/data:) fungerar som vanligt.

---

## 3. Modelldrift mellan Playtyper och Playtypus.Core — hittades och fixades

Det här var den konkreta träffen från "titta i playtypus.core/components
och se om det finns fler features". `Models/PackConfig.cs` och
`Models/Activity.cs` var dokumenterade som "1:1-kopior" av Playtypus.Core —
det STÄMDE INTE LÄNGRE. Verifierat genom en fältdiff av båda filerna mot
Playtypus.Core, inte anteckningar ur minnet.

**Vad som saknades (v15-tillägg i Playtypus.Core, aldrig porterat hit):**

| Fält | Typ | Var |
|---|---|---|
| `Features.SocialFeed` | `bool` | `PackConfig.cs` |
| `Features.BackendRecordPanel` | `bool` | `PackConfig.cs` |
| `Features.BackendRecordPanelPath` | `string?` | `PackConfig.cs` |
| `Features.BackendRecordPanelTitleKey` | `string?` | `PackConfig.cs` |
| `Features.BackendRecordPanelFields` | `List<BackendRecordFieldConfig>?` | `PackConfig.cs` (ny klass) |
| `Activity.SocialFeed` | `SocialFeedConfig?` | `Activity.cs` (ny klass) |

**Det här var inte bara en "missad feature"** — det var en tyst
dataförlust-risk. Om ett pack redigerat direkt i Playtypus (eller av ett
äldre Playtyper-jobb efter att fälten fanns i huvudrepot) redan hade
`socialFeed`/`backendRecordPanel*` satt i sin `pack.config.json`, och någon
sedan öppnade och sparade om det paketet i Playtyper UTAN de här fälten på
C#-modellen, skulle System.Text.Json tyst DROPPA de fälten vid
serialisering. Fixat nu — modellerna är fält-för-fält identiska igen
(verifierat via diff, inte antaget).

**Byggt utöver själva fälten:**
- `FeatureManifest.cs` — manifest-poster för alla fem (matchar filens egen
  policy: reflection täcker bara bool/int/string, resten dokumenteras här
  i alla fall så att AI-genereringsflödet vet att de finns).
- Reflection (`FeatureFlagsReflection.cs`) plockar automatiskt upp
  `socialFeed`, `backendRecordPanel`, `backendRecordPanelPath` och
  `backendRecordPanelTitleKey` i UI:t utan någon egen kod — de är
  bool/string?, exakt vad den generiska griden redan hanterar.
- `backendRecordPanelFields` (en lista) har INGEN egen editor-kontroll än
  — samma mönster som `streakTracking`/`export`/`reminders`/
  `adminEditorFields` redan hade: nås via Avancerat-flikens råa JSON tills
  vidare. Se avsnitt 4 om ni vill bygga en riktig editor för den.

**Rekommendation:** kör om den här diffen (`Models/PackConfig.cs` och
`Models/Activity.cs` mot motsvarande i Playtypus.Core) med jämna mellanrum,
inte bara en gång — det är precis den här typen av tyst drift som orsakade
det här avsnittet. `FeatureManifest.cs`s egen klasskommentar beskriver ett
tidigare, liknande fall (`smartCardVisuals`, v14) — det här är andra gången,
inte första.

**2026-07-13: rekommendationen visade sig nödvändig samma dag den
skrevs.** Nästa diff (samma metod, inte antaget) hittade ett TREDJE
tillfälle: `Features.BackendRecordEntityCategory`, `BackendRecordAddTitleKey`,
`BackendRecordAddFieldLabelKey` (v16, 2026-07-12 — se README-LEVERANS.md i
huvudrepot, "möjlighet att lägga till spelare") hade landat i Core samma
dag som porten ovan gjordes, och missades av samma skäl. Portat nu
(`FeatureManifest.cs`, samma mönster som v15-fälten ovan — och samma tre
fält tillagda i **PackWizards egen** `FeatureManifest.cs` också, som det
visade sig sakna hela `backendRecordPanel*`-familjen, inte bara v16-delen).

**Det här mönstret — noggrann manuell synk som hinner ikapp men sen
glider isär igen inom samma dag — är den bästa tillgängliga bevisningen
för VARFÖR modellerna slogs ihop strukturellt idag (se filhuvudet ovan
och `PreviewAdapter.cs`s kommentar). `Playtyper.Shared/Models/PackConfig.cs`
och `Activity.cs` FINNS INTE LÄNGRE som separata filer — Playtyper använder
`Playtypus.Core.Models.PackConfig`/`Activity` direkt. Den här sektionens
rekommendation ("kör om diffen med jämna mellanrum") är fortfarande sund
praxis för ALLT som inte redan är sammanslaget (se avsnitt 7 för vad som
återstår, t.ex. PackWizards egna `PackBrief.cs`/`ActivityEditor.cs`, som
fortfarande är oberoende implementationer), men för PackConfig/Activity
specifikt är frågan inte längre relevant — det finns bara en sanning att
diffa MOT nu, ingen kopia att diffa MED.**

**Genomsökt men inget mer hittades:** `Components/`-mappen i sin helhet
skummades efter att modell-diffen var klar, som en bekräftande andra
kontroll snarare än den primära metoden (en komponent kan bara driva
beteende utifrån fält som redan finns på modellen, så modell-diffen är den
uttömmande källan för "vilka NYA konfigurerbara fält finns", medan
komponentgenomgången bara bekräftar att inget UI-beteende är kopplat till
ett fält som modell-diffen skulle ha missat). `BackendRecordPanel.razor`
och `SocialFeedEmbed.razor` — komponenterna som faktiskt konsumerar de nya
fälten — bekräftades finnas, som väntat.

---

## 4. ✅ Byggt (2026-07-13): Innehållsformulär som saknades

Alla 16 fält från förra versionens tabell har nu en egen editor. Ingen av
dem går längre bara via Avancerat-flikens råa JSON:

| Fält i `PackConfig` | Typ | Byggt i |
|---|---|---|
| `SituationPresets` | `List<SituationPreset>` | `Components/Editors/SituationEditor.razor` (flik "Läge & genvägar") |
| `QuickActions` | `List<QuickActionConfig>` | Samma |
| `Mascot` | `MascotConfig` | Samma |
| `ReadyNow` | `ReadyNowConfig?` | Samma |
| `Onboarding` | `List<OnboardingSlide>` | `Components/Editors/OnboardingEditor.razor` (flik "Introduktion") |
| `Tutorial` | `List<TutorialSlide>` | Samma |
| `Badges` | `List<BadgeDefinition>` | Samma |
| `Auth` | `AuthConfig?` | `Components/Editors/BackendsEditor.razor` (flik "Backend & inloggning") — inklusive inbyggd SHA-256-hashning, se filens egen kommentar om varför den måste hållas i synk med `tools/generate-hash/generate-hash.html` för hand |
| `Typography` | `TypographyConfig?` | `Components/Editors/AppearanceEditor.razor` (flik "Utseende") |
| `AmbientSound` | `AmbientSoundConfig?` | Samma |
| `CategoryLayouts` | `List<CategoryLayoutConfig>` | Samma |
| `Ui` | `UiConfig?` | Samma |
| `Modules` | `List<ProgressionModule>` | `Components/Editors/ActivityTable.razor` (ny sektion under aktivitetslistan — ligger här snarare än i en egen flik eftersom modul-ihopsättning behöver aktivitetslistan som ändå redan är laddad där) |
| `Backends` | `List<BackendConfig>` | `BackendsEditor.razor` |
| `Languages` | `List<LanguageOption>` | `Components/Editors/IdentityEditor.razor`, ny sektion (tidigare bara `DefaultLanguage`) |
| `StartView`, `CustomCss` | `string`/`string?` | `IdentityEditor.razor`, precis där förra versionen föreslog |

Nya delade byggstenar (inte i den ursprungliga scope-listan, men
tillkommer naturligt av att bygga sju listredigerare på en gång):
`Components/Editors/DictEditor.razor` — liten återanvändbar
nyckel/värde-lista för `filterBundle`/`criteria`-formade fält, istället för
att skriva samma rad/kolumn-UI fyra gånger.

**Vad som INTE är byggt än, för att vara exakt om gränsen:**
`ActivityTable.razor`/aktivitets-detaljmodalen täcker fortfarande inte quiz,
actions, contentBlocks, ljud, schemaläggning eller `SocialFeed` på enskilda
aktiviteter (~40 fält på `Activity`) — bara listvyn och den nya
Moduler-sektionen fick uppmärksamhet den här omgången, inte
detalj-redigeraren för en enskild aktivitet. `CreatePackPage.razor`s
formulär täcker fortfarande bara en delmängd av `PackBrief`s fält.
Prioritera de här näst, i den ordningen, om ni fortsätter härifrån.



## 5. Driftsättning, bundle, lösenord, licens

### ✅ Byggt: Driftsättning (var P2, första punkten)

`GitHubOperator.CreateDeployTagAsync` — som var namngiven i kontraktet men
aldrig implementerad — finns nu, tillsammans med två saker den visade sig
bero på:

- **`GitHubOperator.CheckDeployReadinessAsync`** — portad från
  `DeployReadiness.cs` (PackWizard). Samma "varna, blockera inte"-filosofi:
  returnerar en strukturerad `DeployReadinessResult` istället för att
  besluta åt den som anropar.
- **`GitHubOperator.EnsureDeployWorkflowAsync`** + `TryParseDeployWorkflowConfig`
  — genererar `.github/workflows/deploy.yml` (portad från `WorkflowMode.cs`s
  YAML-mall, samma jobbstruktur) och sätter secrets via den redan
  existerande `GitHubRepoService.SetSecretsAsync`. Visade sig krävas: en
  tagg utan ett deploy.yml som lyssnar på den gör ingenting alls, så
  ren tagg-skapande utan det här hade varit halvfärdigt.
- **`GitHubOperator.CreateDeployTagAsync`** + `SuggestNextDeployVersionAsync`
  — portad från `DeployMode.cs`. Samma tagg-namngivning (`web/v1.0.0` etc).
- **`Services/CloudflareApiService.cs`** — portad från PackWizards egen
  (verifiera token, skapa/hitta Pages-projekt, slå upp live-URL).
- **UI: `Components/Screens/DeployPage.razor`** (`/apps/deploy`) — länkad
  från `PackListPage.razor`. Beredskapskoll, arbetsflödesformulär
  (förifylld om `deploy.yml` redan finns — läser tillbaka och tolkar den
  egna, kända YAML-formen), tagg-skapande med versionsförslag.

**En genuin bugg hittades och fixades under porteringen, inte bara
kopierad vidare:** `WorkflowMode.cs`s YAML-mall skickar
`--platform android` till `scripts/inject-bundle.sh` för Android-jobbet —
men skriptet accepterar bara `web` eller `maui` och avslutar med fel på
allt annat (kontrollerat direkt i skriptet, inte antaget). Det hade fått
VARJE Android-driftsättning genererad från den mallen att krascha på det
steget. Playtypers egen generator (`GitHubOperator.BuildDeployWorkflowYaml`)
skickar `--platform maui` istället. Värt att fixa samma ställe i
`WorkflowMode.cs` i huvudrepot också, om ni vill hålla CLI:t och Playtyper
i synk — inte gjort här (utanför den här leveransens scope, och inte
`GAPS.md`s jobb att ändra CLI:t).

**Medvetet inte byggt i den här omgången:**
- Android-nyckel-generering i appen (`keytool` är ett OS-verktyg, inte
  tillgängligt i en webbläsare/WASM — samma avgränsning README redan
  dokumenterade). `DeployPage.razor` förväntar en redan-base64-kodad
  `.jks` inklistrad, genererad via CLI:t eller manuellt.
- Cloudflare-projektet skapas INTE automatiskt av `DeployPage.razor` än,
  bara verifieras/förväntas finnas — `CloudflareApiService.EnsureProjectAsync`
  finns porterad och redo att kopplas in, men UI:t anropar den inte än.
  Litet jobb att lägga till om ni vill ha det.
- ~~Ingen NuGet-CI-workflow för Playtypus.Core~~ — byggd 2026-07-13, se
  avsnitt 2.

### ✅ Byggt (2026-07-13): Bundle-hantering

`Components/Screens/BundlePage.razor` (`/apps/bundle`, länkad från
`PackListPage.razor`). Hanterar `RepoType.Customer` (vanliga fallet — en
`bundle/app-bundle.json` per repo) och `RepoType.PackLibrary` (äldre
modell, flera `bundles/{id}/`), inklusive att skapa filen första gången om
den saknas. Redigerar: `appId`/`appName`/`defaultPack`/`accentColor`,
`storeDescription` (sv/en), packlistan (kryssrutor mot alla packs i
repot — inte bara de som redan är med), bundle-nivåns lösenordsskydd
(`auth`, samma inbyggda SHA-256-hashning som `BackendsEditor.razor`), och
`licenseId`/`licenseServerUrl`. Jobbar direkt mot `JsonObject`, precis som
`BundleRepository.cs` redan gjorde — ingen ny stark typ infördes för
bundle-formen.

`CheckDeployReadinessAsync`s varning om att bundlen saknar packs har nu
faktiskt en plats att fixas ifrån, inte bara en varning man måste åtgärda
i en annan editor eller via Git direkt.

### ✅ Byggt (2026-07-13): Lösenord, licens

**Pack-lösenord (`AuthConfig`):** `Components/Editors/BackendsEditor.razor`,
se avsnitt 4:s tabell. Genererar SHA-256-hash direkt i webbläsaren
(`crypto`-fri — `System.Security.Cryptography.SHA256` i WASM, inte
JS-interop) med exakt samma algoritm som `tools/generate-hash/
generate-hash.html` (rått UTF8, ingen salt, gemener hex) — verifierat genom
att läsa det verktygets JS, inte antaget. De två implementationerna delar
ingen kod (ett fristående HTML-verktyg och en Blazor-komponent kan inte
det) och måste hållas i synk för hand om algoritmen någonsin ändras.

**Licens (`Services/LicenseApiService.cs`-motsvarighet):** byggd direkt i
`BundlePage.razor`, inte som en egen sida — licens hänger ihop med VILKEN
bundle den gäller för. Porterad från PackWizards `LicenseApiService.cs`
(samma DTO-form, samma endpoints, samma `https://license.playtypus.se`-
fallback när ingen `licenseServerUrl` är satt på bundlen). Statuskoll
(`GET /api/license/status`) kräver ingen admin-nyckel, enligt PackWizard-
kodens egen kommentar ("publik, ingen admin-nyckel") — byggd som en vanlig
knapp. Förnya och återkalla kräver `X-Admin-Key`; byggda bakom en
`<details>`-flik med ett nyckelfält som ALDRIG sparas (bara i minnet under
sessionen), matchande hur känsligt CLI:t redan behandlar samma nyckel.

**Medvetet INTE byggt, med skäl:**
- **Skapa en helt ny licens** (`LicenseApiService.CreateAsync`) — kräver
  mer kringinfo (kundnamn, pris, e-post, produkt-id) än vad ett
  bundle-formulär rimligen ska samla in, och är en händelse som sker en
  gång per kund snarare än nåt man gör medan man redigerar en bundle.
  Kvar som en CLI-uppgift (`LicenseMode.cs`, läge "skapa").
- **Superadmin** (`SuperAdminMode.cs`) — pratar mot en driftsatt
  `Playtypus.Server` för org/medlemshantering, INTE mot pack-innehåll
  eller ens ett GitHub-repo (dess egen klasskommentar säger det uttryckligen).
  Hör hemma i ett separat drifts-/adminverktyg, inte en pack-editor som
  Playtyper — bedömdes avsiktligt utanför scope, inte bara bortglömt.
- **CORS mot licensservern** är ett äkta beroende den här implementationen
  har som CLI-versionen aldrig behövde bry sig om (CLI:t kör lokalt, en
  webbläsare gör inte det). Licensservern måste tillåta Playtypers origin.
  Inget att fixa i Playtyper självt — flaggat i UI:t som en trolig
  felorsak om anropet misslyckas, med en tydlig text om att det är ett
  driftskrav.

---

## 6. P3/P4 — Medvetet uppskjutet (vänta på klartecken innan ni bygger)

Oförändrat sedan förra versionen:

- **Integrerat AI-flöde** (direkta API-anrop istället för kopiera-
  klistra-prompt). Bygg inte om det här utan uttryckligt godkännande.
- **GitHub OAuth Device Flow** som alternativ till att klistra in ett PAT.
- **Två separata token-nivåer.** `ICredentialStore` hanterar fortfarande
  bara ett enda token.

---

## 7. Teknisk skuld och övrigt värt att känna till

Oförändrat från förra versionen, plus två nya observationer (inte
åtgärdade — utanför scope, se resonemang under respektive punkt):

- **Ingen automatiserad testtäckning alls.** Oförändrat. Gäller nu även
  `PreviewAdapter.cs`, `GitHubOperator`s nya driftsättningsmetoder och
  `CloudflareApiService.cs` — allihop skrivna utan möjlighet att
  kompilera eller köra dem i det här arbetspasset (se README), så ett
  första enhetstest mot dem är extra värdefullt innan de litas på i
  produktion.
- **Diff är fil-nivå, inte fält-nivå.** Oförändrat.
- **App-ikonen.** Oförändrat.
- **`Platforms/Android/Resources/`.** Oförändrat.
- **Ingen offline-hantering utöver draft-cachen.** Oförändrat.
- **NYTT: dark-mode-CSS-kaskaden ser misstänkt ut.** Upptäckt av misstag
  under preview-arbetet (avsnitt 2), inte via en avsiktlig granskning:
  `theme.css` och `theme-dark.css` laddas båda som `:root { }`-block, i
  fast DOM-ordning (`#pack-theme` före `#pack-theme-dark`) — vilket borde
  betyda att den mörka variantens värden ALLTID vinner kaskaden för
  delade variabler, oavsett `html.dark`-klassen, eftersom CSS custom
  properties kaskaderar per dokumentordning för lika specificitet.
  Samtidigt säger `FeatureManifest.cs`s egen dokumentation till
  AI-genereringsflödet att skriva mörkt tema under en `html.dark { }`-
  selektor, INTE `:root` — vilket antyder att `:root` i de faktiska
  exempel-packen (demo/lagledaren) kan vara fel format, inte det tänkta.
  Inte utrett vidare eller fixat här — preview:n (avsnitt 2) replikerar
  det EXAKTA nuvarande beteendet medvetet (bugg-för-bugg), eftersom hela
  poängen med en riktig preview är att visa vad som faktiskt kommer hända
  vid driftsättning, inte en förbättrad version av det. Värt en riktig
  utredning i Playtypus-repot om dark mode faktiskt fungerar i produktion
  idag.
- **NYTT: `WorkflowMode.cs`s `--platform android`-bugg.** Se avsnitt 5.
- **NYTT: befintlig `deploy.yml` i Playtypus-repot pekar på
  `dotnet-version: '9.0.x'`** trots att alla `.csproj`-filer i samma repo
  target `net10.0`. Sett i förbigående (inte utrett varför), inte rört —
  Playtypers EGEN nygenererade `deploy.yml`-mall (avsnitt 5) använder
  korrekt `10.0.x`.
- **NYTT (2026-07-13): `<button>` nästlad i `<button>`, ogiltig HTML.**
  Två ställen hade en klickbar "kort/rad"-yta byggd som en `<button>` med
  en egen liten `✕`-knapp (`@onclick:stopPropagation`) inuti — HTML tillåter
  inte det (webbläsare hanterar det oförutsägbart, oftast genom att bryta
  upp nästlingen på egna villkor snarare än att respektera den avsedda
  klickytan). Fixat i `ActivityTable.razor` (aktivitetsraderna, fanns redan
  innan den här omgången) och i det nya `PackListPage.razor`
  (radera-knappen på varje pack-kort) — bytt till `<div role="button"
  tabindex="0">` med en egen `@onkeydown`-hanterare för Enter/Space, så
  tangentbordsanvändning inte tappas bort. Genomsökt (ett litet Python-
  skript, inte bara ögonmått) för fler instanser — hittade inga fler.
- **NYTT (2026-07-13): `.editor-pane`/`.field-grid` fanns bara i
  `IdentityEditor.razor`s egen `<style>`-tagg**, som flera andra
  editor-flikar (`ContentEditor`/`TokenEditor`/`AdvancedJsonEditor`, plus
  `ActivityTable`) använde utan att själva definiera. En inline
  `<style>`-tagg finns bara i DOM:en medan komponenten som skrev den är
  monterad — fungerade av ren tur eftersom Identitet råkar vara första
  fliken (`_activeTab = "identity"`), så dess stil hann alltid monteras
  före man bytte till nån annan flik. Skulle sluta fungera om det
  förvalda första fliken nånsin ändrades. Flyttat till `app.css` som en
  riktig global bas nu (`.card`s granne), så det inte längre spelar roll
  vilken flik som laddas först.

---

## 8. Rekommenderad arbetsordning

Alla åtta punkter från förra versionen av den här listan är nu gjorda.
Ny lista, för vad som är rimligt att ta itu med härnäst:

1. Få kodbasen att bygga rent (`dotnet restore && dotnet build` i båda
   repona) — fortfarande steg ett, och fortfarande inte gjort av oss:
   det här arbetspasset kunde precis lika lite kompilera nåt som förra
   (samma miljöbegränsning, se README). Allt i det här dokumentet är
   verifierat genom läsning, fältdiffar mot faktiska modellfiler, och ett
   par enkla syntax-sanity-skript — inte genom att faktiskt bygga det.
2. Fyll i `DITT_ORG` i `NuGet.config` och lägg `PLAYTYPUS_TOKEN`-secreten
   (avsnitt 2) — utan det bygger inte Playtyper alls, oavsett hur rätt
   koden i övrigt är.
3. Låt `publish-nuget.yml` köra minst en gång innan ni försöker bygga
   Playtyper mot den, och uppdatera versionsnumret i
   `Playtyper.Shared.csproj` från platshållaren `1.0.0`.
4. `ActivityTable.razor`s detalj-redigerare för en enskild aktivitet
   (quiz/actions/contentBlocks/ljud/schemaläggning/SocialFeed) — den enda
   återstående större luckan från avsnitt 4:s ursprungliga lista.
5. `CreatePackPage.razor`s täckning av `PackBrief`s fält.
6. Avsnitt 6 väntar fortfarande på uttryckligt klartecken innan ni bygger
   nåt av det.

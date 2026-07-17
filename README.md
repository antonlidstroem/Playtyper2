# Playtyper

Ett webb- och apptillgängligt verktyg för att skapa, redigera och driftsätta
Playtypus kund-appar — efterträdaren till PackWizard-CLI:t, byggt enligt
strategin ni kom fram till innan kodningen började. Ingen egen backend:
allt läses och skrivs direkt mot GitHubs REST-API med ett token du själv
kontrollerar.

## Läs det här avsnittet först

Den här lösningen är **skriven för hand, fil för fil, men aldrig kompilerad**.
Byggmiljön som skapade den hade varken .NET SDK installerat eller
nätverksåtkomst till nuget.org (kontrollerat och bekräftat, se nedan) —
alltså gick det inte att köra `dotnet restore`/`build` eller fånga vanliga
saker en kompilator annars hittar direkt (fel typnamn, en bortglömd
`using`, en parameter som stavas fel mellan två filer).

Det jag KUNDE göra för att kompensera:

- Där PackWizards egen kod kunde återanvändas rakt av (datamodeller,
  `GitHubRepoService`, `Validator`, `FeatureManifest`, `PromptGenerator` m.fl.)
  har jag **kopierat filerna direkt** från er uppladdade zip och bara
  justerat namnrymd/async — inte skrivit av dem för hand. Det tar bort
  nästan all risk för avskrivningsfel i den koden.
- Alla korsreferenser mellan komponenter (parameternamn, metodsignaturer,
  modellernas faktiska fältnamn) har jag verifierat genom att faktiskt läsa
  källfilerna och `grep`:a igenom hela lösningen efteråt — inte gissat.
- Krypteringen för GitHub Secrets (`SecretsCrypto.cs`, som ersätter det
  native-only paketet `Sodium.Core`) är den enda riktigt nyskrivna,
  algoritmiskt känsliga koden. Den är verifierad **byte-för-byte** mot en
  riktig libsodium-installation (via Pythons PyNaCl, som fanns tillgängligt
  att installera i byggmiljön) — se klassdoc-kommentaren i filen för hur,
  och kör `SecretsCrypto.SelfTest()` själv som allra första steg när
  projektet väl bygger.

**Gör det här innan du litar på lösningen för riktiga secrets/repos:**

1. `dotnet workload install maui` (första gången du bygger MAUI-projekt)
2. `dotnet restore` i repo-roten — detta är det första riktiga testet av
   om allt faktiskt hänger ihop.
3. Åtgärda de kompilatorfel som (rimligen få, men möjligen några) dyker upp.
   Om du klistrar in felen till mig kan jag rätta dem direkt.
4. Kör `SecretsCrypto.SelfTest()` (t.ex. tillfälligt från en testsida eller
   `dotnet run` + brytpunkt) innan du sätter något riktigt GitHub-token i
   omlopp.

**Uppdatering — samma begränsning gällde för det här arbetspasset också**
(preview mot Playtypus.Core + driftsättning, se `GAPS.md` §2 och §5): inte
heller den här gången fanns .NET SDK eller nätverksåtkomst till nuget.org
tillgängligt. Samma kompensation som ovan gällde, förstärkt eftersom det
här passet dessutom skriver kod i EN ANDRA repo (Playtypus.Core) som måste
stämma exakt överens med vad Playtyper förväntar sig på andra sidan en
JSON-gräns (se `PreviewAdapter.cs`s klasskommentar): varje metodsignatur
som anropas mellan `GitHubRepoService`/`RemoteRepo`/`BundleRepository`/
`Validator`/`PackContext`/`ThemeService`/`AppShell` lästes om och verifierades
en sista gång direkt i källkoden precis innan den användes, inte bara en
gång tidigt i arbetet — det fångade minst två faktiska fel (ett i hur
`GitHubRepoService.CreateTagAsync` faktiskt tar sina parametrar, ett i
`RemoteRepo.WriteFileAsync`s throw-istället-för-returnera-fel-kontrakt)
innan de hann bli permanenta. Punkt 1–4 ovan gäller lika mycket för allt
nytt i det här passet som för resten av kodbasen — kör `dotnet restore`
och åtgärda det som dyker upp innan ni litar på det.

**2026-07-13:** ProjectReference-nödlösningen som beskrevs här tidigare är
borttagen. Se "Snabbstart" nedan för NuGet-upplägget den ersattes med.

## Snabbstart

**Konsumera Playtypus.Core.** Sedan den riktiga preview:n mot
Playtypus.Core byggdes (se `GAPS.md` §2) har `Playtyper.Shared` ett
beroende på `Playtypus.Core`, inte bara sina egna filer — men sedan
2026-07-13 är det ett vanligt versionerat NuGet-paket
(`<PackageReference Include="Playtypus.Core" Version="..." />`), publicerat
till GitHub Packages av huvudrepots `.github/workflows/publish-nuget.yml`.
Inget syskonrepo behöver checkas ut längre.

Engångs-setup för `dotnet restore` lokalt:

1. Skapa en GitHub PAT (classic) med scope `read:packages`.
2. `export PLAYTYPUS_NUGET_PAT=ghp_xxx` (eller motsvarande på Windows) —
   `NuGet.config` i repo-roten läser den automatiskt.
3. Byt ut `DITT_ORG` i `NuGet.config` mot er riktiga GitHub-org.

Vill du bygga mot en Core-ändring som ännu inte är publicerad: kör
`dotnet pack src/Playtypus.Core/Playtypus.Core.csproj -o ../lokal-feed` i
huvudrepot och lägg till `../lokal-feed` som ytterligare en `<add key=...>`
i `NuGet.config` lokalt (checka inte in den raden) — samma trick som ett
lokalt npm-`file:`-beroende.

**Token gick ut? Så förnyar du den.** PAT:en du skapade ovan har ett
utgångsdatum du själv valde. När det datumet passeras slutar BÅDE lokal
`dotnet restore` OCH `deploy-pages.yml` fungera — inte för att något är
fel i koden, utan för att autentiseringen mot `playtypus-github`-källan i
`NuGet.config` inte längre är giltig.

*Hur du märker det:* GitHub mejlar kontot som skapade token:en i förväg
("your personal access token (classic) is about to expire") — läs det
mejlet, skumma inte förbi det. Missar du det ändå syns det genom att
`dotnet restore`/`deploy-pages.yml` plötsligt ger ett
autentiseringsfel (401/403) specifikt mot `nuget.pkg.github.com`, medan
paket från `nuget.org` fortsätter fungera som vanligt — så skiljer du
det här felet från ett riktigt byggfel.

*Så förnyar du, samma sak lokalt som i CI förutom sista steget:*

1. Profilbild → **Settings** → **Developer settings** →
   **Personal access tokens** → **Tokens (classic)**.
2. Hitta token:n på namnet (Note) du gav den när du skapade den, t.ex.
   `playtyper-nuget-restore`.
3. Klicka in på den → **Regenerate token** — enklare än att skapa en helt
   ny, eftersom scopet (`read:packages`) redan är rätt och följer med.
4. Välj ett nytt utgångsdatum → **Regenerate token** längst ner.
5. **Kopiera värdet direkt** — visas bara den här enda gången, precis som
   förra gången.
6. Uppdatera VARJE ställe som har den gamla strängen:
   - Lokalt: `export PLAYTYPUS_NUGET_PAT=<nya värdet>` — samma variabel
     som i steg 2 ovan, bara ett nytt värde.
   - CI: Playtyper-repot → **Settings** → **Secrets and variables** →
     **Actions** → klicka på `PLAYTYPUS_TOKEN` → klistra in det nya
     värdet → spara. Skriver över det gamla, inget att radera separat.

Det du INTE behöver göra om token:en bara gått ut (till skillnad från
engångs-setupen ovan): `DITT_ORG` i `NuGet.config` är oförändrat — det är
knutet till organisationen, inte till token:en — och `publish-nuget.yml`
behöver inte köras igen. Den nya token:en autentiserar bara LÄSNING mot
ett paket som redan är publicerat; den påverkar inte vilken version som
faktiskt ligger i flödet. Ett kalenderpåminnelse på utgångsdatumet du
valde i steg 4 är ett enkelt sätt att slippa förlita dig enbart på att
mejlet från GitHub inte hamnar i skräpposten.

```bash
# Webbversionen (Blazor WASM) - körs i valfri browser
cd src/Playtyper.Web
dotnet run

# Apphuvudet (MAUI Blazor Hybrid) - Android
cd src/Playtyper.App
dotnet build -t:Run -f net10.0-android

# Apphuvudet - Windows (bara om du bygger på en Windows-maskin)
dotnet build -t:Run -f net10.0-windows10.0.19041.0
```

Paketversionerna i alla `.csproj`-filer är satta till `10.0.0` som en
platshållare — exakt samma anmärkning som redan finns i huvudrepots
`Playtypus.App.csproj` (byggmiljön hade av samma skäl inget sätt att slå
upp senaste patch-version). Kör `dotnet list package --outdated` efter
första restore.

## Arkitektur, kort

```
src/
  Playtyper.Shared/    Razor Class Library - all UI, all logik, delas av båda huvudena
  Playtyper.Web/        Blazor WASM-värd (statisk, ingen backend, PWA-installerbar)
  Playtyper.App/        MAUI Blazor Hybrid-värd (Android + Windows, samma UI-kod)
```

Samma mönster som `Playtypus.Core`/`Playtypus.Web`/`Playtypus.App` i
huvudrepot — inget nytt tekniskt beslut, bara samma struktur applicerad på
ett nytt verktyg.

**Ingen backend, GitHub är datalagret.** Ett Personal Access Token (klistras
in i appen, aldrig hårdkodat) autentiserar direkt mot `api.github.com` för
allt: läsa/skriva pack-filer, skapa nya kund-repon, sätta Actions-secrets.
PAT:et måste ha "All repositories"-scope + Contents/Workflows/
Administration/Secrets (Read and write) — samma fyra rättigheter
PackWizards egen `RepoConnector.cs` redan kräver, av samma skäl (att skapa
NYA repon kräver kontonivå-behörighet, går inte att förscopea till repon
som ännu inte finns). Förklaras i klartext för användaren på
anslutningsskärmen (`ConnectRepoPage.razor`).

**Två olika lagringsstrategier för token**, medvetet:
- Web: `sessionStorage` (rensas när fliken stängs)
- App: `Microsoft.Maui.Storage.SecureStorage` (OS-krypterad nyckelring —
  striktare säkert än något webbläsar-API, inte bara en bekvämlighet)

Se `ICredentialStore.cs` för resonemanget i sin helhet.

## Vad som faktiskt är byggt (MVP enligt den överenskomna fasplanen)

- Anslut till GitHub (token, validering, förklaring av rättigheter)
- Mina appar: lista, skapa nytt kund-repo (full scaffolding), anslut till befintligt
- Redigera pack: Identitet, Tokens (färg + live WCAG-kontrastkoll),
  Aktiviteter (tabell + detaljpanel för kärnfälten), Innehåll (kategorier,
  filter, och en **schema-driven** funktionsflagg-grid genererad direkt ur
  `FeatureManifest` via reflection — se `FeatureFlagsReflection.cs`)
- Avancerat: rå JSON/CSS per fil — garanterar 100% schematäckning även för
  fält som inte fått en egen kontroll än
- Diff innan spara (fil-nivå, se motivering i `PackDraft.cs`) + validering
  (samma `Validator.cs` som CLI:t, portad till async)
- Skydd mot dataförlust: utkast speglas till IndexedDB, "osparat"-varning
  vid stängning, återuppta-dialog om ett utkast aldrig hann sparas
- Responsiv layout: samma markup, tre brytpunkter (< 900 / 900–1400 / ≥
  1400px, samma skala som redan används i `Playtypus.Core`s CSS) — botten-
  navigering på mobil, full tre-panels-layout på desktop
- AI-genereringsflödet, oförändrat: generera prompt → klistra in i Claude →
  klistra tillbaka svaret → `PackFileParser` packar upp filerna. Ingen
  egen API-integration (som beslutat: tidigast v3/v4)

## Vad som INTE är byggt än — medvetna avgränsningar

- **Förhandsvisningen är en fasad, inte riktiga Playtypus.Core-komponenter.**
  Det var alltid planen (strategins "alternativ A") att koppla in det
  riktiga `AppShell` via ett NuGet-paket publicerat från huvudrepots CI —
  men det steget ligger i huvudrepot, utanför den här leveransen, och
  paketet finns inte publicerat än. `PreviewPanel.razor` har en utförlig
  kommentar överst som beskriver exakt vad som behöver göras i huvudrepot
  och här för att koppla in den riktiga komponenten.
- Bundle-hantering, driftsättning (tagg/workflow_dispatch), lösenordsskydd,
  licenshantering — allt enligt plan för v1/v2, inte med i den här omgången.
- Activity har ~40 fält totalt; tabellvyn täcker de vanligaste (titel,
  beskrivning, steg, kategori, taggar, nivå, förberedelsetid, rekvisita,
  säkerhetsnotis). Quiz/actions/contentBlocks/ljud/schemaläggning når du via
  Avancerat-fliken tills de får egna kontroller.
- Filter-alternativ (`FilterConfig.Options`) redigeras bara via Avancerat
  tills vidare — filtret själv (id/typ/etikett) har en egen kontroll.
- Diffen är fil-nivå, inte fält-nivå — se motivering i `PackDraft.cs`.
- Android/iOS-nyckelsignering (`keytool`) kräver native OS-verktyg och kan
  inte flytta till webbläsaren eller en ren .NET-runtime — stannar en
  separat, sällan körd process (t.ex. kvar i CLI:t, eller manuellt).
- `Platforms/Android/Resources/` innehåller inga handskrivna
  `colors.xml`/`styles.xml` — MAUI:s egen resizetizer-tooling genererar
  splash/ikon-resurserna automatiskt från `MauiIcon`/`MauiSplashScreen`-
  posterna i `.csproj`, så det behövdes inte, men värt att veta att mappen
  är tom med flit, inte av misstag.
- App-ikonen är hela den fyrkantiga loggan (med "Playtyper"-texten) rakt av
  — ser bra ut i appen, men en beskuren version med bara maskoten skulle bli
  tydligare i de riktigt små ikonstorlekarna (hemskärm). Byt gärna ut
  `Resources/AppIcon/appicon.png` mot en beskuren variant senare.

## Tre saker att verifiera tidigt (från strategifasen, fortfarande relevanta)

1. **`X-GitHub-Api-Version`-headern** som `GitHubRepoService.cs` skickar —
   testa mot en riktig CORS-preflight i webbläsaren (Playtyper.Web). Fanns
   kända rapporter om att den inte alltid är tillåten där.
2. **Fine-grained PAT + "All repositories" ger verkligen repo-skapande** —
   ärvt antagande från PackWizards egen, redan fungerande onboarding-text;
   inte omtestat här, men inte heller en ny risk den här omgången införde.
3. **`SecretsCrypto.SelfTest()`** — kör den, se ovan.

## Namngivning

Repo/produktnamn: **Playtyper**. Loggan som bifogades användes rakt av för
favicon (Web) och app-ikon (App) — se avsnittet ovan om att en beskuren
variant vore snyggare för de små ikonstorlekarna.

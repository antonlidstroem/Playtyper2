using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playtyper.Shared.Services;

/// <summary>
/// Porterad från PackWizards RepoConnector.cs. Samma affärslogik och samma
/// kantfall (repo finns men saknar main-branch, personkonto vs organisation,
/// namn-sanering för å/ä/ö) — men omskriven från en interaktiv konsol-loop
/// (Console.WriteLine + UI.Ask i en while-loop) till fristående async-metoder
/// som returnerar strukturerade resultat. En Razor-komponent binder mot
/// dessa och visar dem i UI istället för i terminalen.
///
/// Det här är ENDA platsen i Playtyper som orkestrerar flera
/// GitHubRepoService-anrop för att uppnå ett mål ("skapa ett komplett,
/// redo-att-fyllas-i kund-repo"). Enskilda fil-operationer går fortfarande
/// via RemoteRepo — den här klassen bygger ovanpå den, inte istället för den.
/// </summary>
public static class GitHubOperator
{
    public sealed record TokenValidation(bool Valid, string? Login, string? Error);

    public sealed record NewAppRequest(
        string CustomerId,
        string AppName,
        string AppId,
        string AccentColor,
        string RepoName,
        string? Org,
        bool Private);

    public enum CreateOutcome { Created, ConnectedToExisting, RecoveredIncompleteRepo, Failed }

    public sealed record CreateRepoResult(CreateOutcome Outcome, RemoteRepo? Repo, string? Error);

    public sealed record ConnectResult(bool Success, RemoteRepo? Repo, string? Error);

    /// <summary>
    /// Validerar ett GitHub-token och hämtar inloggat användarnamn. Anropa
    /// den här innan token sparas i credential-lagret (session/secure storage)
    /// — precis som RepoConnector.EnsureToken gjorde innan den skrev till
    /// ~/.packwizard/settings.json.
    /// </summary>
    public static async Task<TokenValidation> ValidateTokenAsync(string token)
    {
        var (valid, login, error) = await GitHubRepoService.ValidateTokenAsync(token);
        return new TokenValidation(valid, login, error);
    }

    /// <summary>
    /// Ansluter till ett repo vars typ inte redan är känd (måste avgöras via
    /// två extra API-anrop). Används för "anslut till befintligt repo (org/repo)".
    /// Använd ConnectKnownAsync istället när typen redan finns i "mina appar"-listan.
    /// </summary>
    public static async Task<ConnectResult> ConnectExistingAsync(string token, string ownerRepo)
    {
        if (!ownerRepo.Contains('/'))
            return new ConnectResult(false, null, "Måste vara på formatet org/repo.");

        var exists = await GitHubRepoService.RepoExistsAsync(token, ownerRepo);
        if (!exists)
            return new ConnectResult(false, null,
                $"Hittar inte \"{ownerRepo}\" — kontrollera namnet, och att token har åtkomst till det.");

        var type = await RemoteRepo.DetectTypeRemoteAsync(token, ownerRepo);
        return new ConnectResult(true, new RemoteRepo(token, ownerRepo, type), null);
    }

    /// <summary>
    /// Återansluter till ett repo vars typ redan är känd sedan tidigare
    /// (sparad i "mina appar"-listan) — sparar in DetectTypeRemoteAsyncs
    /// extra API-anrop, kontrollerar bara att repot fortfarande är nåbart.
    /// </summary>
    public static async Task<ConnectResult> ConnectKnownAsync(string token, string ownerRepo, string cachedTypeRaw)
    {
        if (!Enum.TryParse<RepoType>(cachedTypeRaw, out var type))
            return await ConnectExistingAsync(token, ownerRepo);

        var exists = await GitHubRepoService.RepoExistsAsync(token, ownerRepo);
        if (!exists)
            return new ConnectResult(false, null, $"\"{ownerRepo}\" verkar inte finnas kvar, eller token saknar åtkomst.");

        return new ConnectResult(true, new RemoteRepo(token, ownerRepo, type), null);
    }

    /// <summary>
    /// Skapar (eller återansluter/återupptar) ett nytt kund-repo. Motsvarar
    /// RepoConnector.CreateNewRepo + ScaffoldNewRepo i ett enda anrop.
    ///
    /// Hanterar tre fall precis som originalet:
    ///   1. Repot finns inte alls  → skapas, main-branch initieras, scaffoldas.
    ///   2. Repot finns men saknar main-branch (ett tidigare avbrutet försök)
    ///      → main-branch initieras och scaffoldingen görs klart.
    ///   3. Repot finns redan och är komplett → ansluter till det istället,
    ///      rör INGET (ingen skrivning) — CreateOutcome.ConnectedToExisting
    ///      så anroparen kan visa "det här fanns redan" tydligt i UI:t.
    /// </summary>
    public static async Task<CreateRepoResult> CreateCustomerRepoAsync(string token, string login, NewAppRequest request)
    {
        var repoName = SanitizeRepoName(request.RepoName);
        if (string.IsNullOrWhiteSpace(repoName))
            return new CreateRepoResult(CreateOutcome.Failed, null,
                "Repo-namnet blev tomt efter sanering (bokstäver, siffror, bindestreck krävs).");

        // GitHub skiljer på POST /user/repos (personkonto) och
        // POST /orgs/{org}/repos (organisation) — att skicka personkontots
        // eget användarnamn som "org" ger 404 Not Found, så det tolkas
        // istället som "inget org angivet".
        var org = request.Org?.Trim() ?? "";
        var isPersonalAccount = string.IsNullOrWhiteSpace(org)
            || string.Equals(org, login, StringComparison.OrdinalIgnoreCase);

        var orgForApi = isPersonalAccount ? "" : org;
        var owner     = isPersonalAccount ? login : org;
        var ownerRepo = $"{owner}/{repoName}";

        var alreadyExists = await GitHubRepoService.RepoExistsAsync(token, ownerRepo);

        if (alreadyExists)
        {
            var mainSha = await GitHubRepoService.GetBranchShaAsync(token, ownerRepo, "main");

            if (mainSha == null)
            {
                // Repot finns men saknar main — troligen ett tidigare avbrutet
                // CreateCustomerRepoAsync-anrop. Slutför det istället för att
                // ge upp eller be användaren städa manuellt.
                var (initOk, initErr) = await GitHubRepoService.EnsureMainBranchAsync(token, ownerRepo);
                if (!initOk)
                    return new CreateRepoResult(CreateOutcome.Failed, null,
                        $"\"{ownerRepo}\" finns men saknar main-branch, och kunde inte initieras: {initErr}");

                var recovered = new RemoteRepo(token, ownerRepo, RepoType.Customer);
                await ScaffoldAsync(recovered, request.CustomerId, request.AppId, request.AppName, request.AccentColor);
                return new CreateRepoResult(CreateOutcome.RecoveredIncompleteRepo, recovered, null);
            }

            // Repot finns och är komplett — rör det inte, bara anslut.
            var type = await RemoteRepo.DetectTypeRemoteAsync(token, ownerRepo);
            return new CreateRepoResult(CreateOutcome.ConnectedToExisting, new RemoteRepo(token, ownerRepo, type), null);
        }

        var createResult = await GitHubRepoService.CreateRepoAsync(
            token, orgForApi, repoName,
            description: $"Playtypus kund-app — {request.AppName}",
            isPrivate: request.Private);

        if (!createResult.Success)
            return new CreateRepoResult(CreateOutcome.Failed, null, createResult.Error);

        var (branchOk, branchErr) = await GitHubRepoService.EnsureMainBranchAsync(token, ownerRepo);
        if (!branchOk)
            return new CreateRepoResult(CreateOutcome.Failed, null,
                $"Repot skapades ({createResult.HtmlUrl}) men kunde inte initieras: {branchErr}. " +
                "Öppna repot manuellt och lägg till en första fil, försök sedan igen.");

        var repo = new RemoteRepo(token, ownerRepo, RepoType.Customer);
        await ScaffoldAsync(repo, request.CustomerId, request.AppId, request.AppName, request.AccentColor);
        return new CreateRepoResult(CreateOutcome.Created, repo, null);
    }

    /// <summary>
    /// Skriver grundstrukturen (bundle/app-bundle.json, .gitignore, README.md)
    /// till ett tomt, precis skapat repo. Skapar INGEN packs/-platshållare —
    /// ett genuint tomt bundle (packs: []) är ett ärligare starttillstånd än
    /// en mapp som ser klar ut men bara innehåller en platshållarfil; Playtypers
    /// egen "0 packs"-varning (DeployReadiness-motsvarigheten) tar över härifrån.
    /// </summary>
    public static async Task ScaffoldAsync(RemoteRepo repo, string customerId, string appId, string appName, string accentColor)
    {
        var bundleJson = BundleRepository
            .Create(customerId, appId, appName, defaultPack: "", packs: Array.Empty<string>(), accentColor: accentColor)
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        // CreateFileAsync (inte WriteFileAsync) — vi vet att repot precis
        // skapades och filerna inte finns än, så vi sparar en GET per fil.
        // Faller ändå tillbaka till vanlig upsert om ScaffoldAsync av någon
        // anledning körs två gånger på samma repo (se GitHubRepoService).
        await repo.CreateFileAsync(repo.CustomerBundleFile, bundleJson, "init: scaffold bundle (Playtyper)");
        await repo.CreateFileAsync(".gitignore", BuildGitignore(), "init: scaffold .gitignore (Playtyper)");
        await repo.CreateFileAsync("README.md", BuildReadme(customerId, appName, appId), "init: scaffold README (Playtyper)");
    }

    private static string BuildGitignore() => """
        # macOS
        .DS_Store

        # Windows
        Thumbs.db

        # Android keystore — checka aldrig in en .jks om du klonar ner repot lokalt
        *.jks
        release.jks

        # Secrets
        appsettings.Production.json
        """;

    private static string BuildReadme(string customerId, string appName, string appId) => $"""
        # {appName}

        Playtypus kund-repo för **{appName}** (`{customerId}`).

        Det här repot hanteras av Playtyper — alla ändringar (packs, bundle,
        CI-workflow) görs direkt mot GitHub via Playtyper, oavsett vilken
        webbläsare eller enhet du kör det ifrån.

        ## Struktur

        ```
        bundle/
          app-bundle.json     ← app-inställningar och vilka packs som ingår
        packs/
          {customerId}/       ← ditt första pack
            pack.config.json
            activities.sv.json
            theme.css
            theme-dark.css
        .github/workflows/
          deploy.yml
        ```

        ## GitHub Secrets som krävs för driftsättning

        | Secret                  | Varifrån                                                                     |
        |--------------------------|-------------------------------------------------------------------------------|
        | `PLAYTYPUS_TOKEN`       | github.com/settings/tokens → Fine-grained → Playtypus-repot → Contents Read   |
        | `CLOUDFLARE_API_TOKEN`  | dash.cloudflare.com → My Profile → API Tokens → "Edit Cloudflare Workers"      |
        | `CLOUDFLARE_ACCOUNT_ID` | dash.cloudflare.com → Workers & Pages → höger kolumn (32 hex-tecken)          |

        App-id: `{appId}`
        """;

    private static readonly Dictionary<char, char> SwedishTransliteration = new()
    {
        ['å'] = 'a', ['ä'] = 'a', ['ö'] = 'o',
        ['Å'] = 'A', ['Ä'] = 'A', ['Ö'] = 'O',
        ['é'] = 'e', ['è'] = 'e', ['ü'] = 'u',
    };

    /// <summary>Gör om ett fritext-namn till ett giltigt GitHub-repo-namn.</summary>
    public static string SanitizeRepoName(string raw)
    {
        var s = raw.Trim();
        s = new string(s.Select(c => SwedishTransliteration.TryGetValue(c, out var r) ? r : c).ToArray());
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"[^a-zA-Z0-9\-_.]", "");
        s = s.Trim('.', '-');
        return s;
    }

    public static string SuggestAppId(string customerId) =>
        $"se.playtypus.{customerId.Replace("-", "").ToLowerInvariant()}";

    public static string SuggestRepoName(string customerId) => $"{customerId}-app";

    // ═══════════════════════════════════════════════════════════════════════
    // Driftsättning (GAPS.md §5, first bullet) — CreateDeployTagAsync was
    // named in IGitHubOperator's original contract but never implemented;
    // this section is that implementation, plus the two things it turned out
    // to depend on (see each method's own doc comment for why they're here
    // too, not just the tag call GAPS.md named). Ported from three files in
    // the Playtypus repo's tools/PackWizard CLI — DeployReadiness.cs,
    // WorkflowMode.cs, DeployMode.cs — same business rules and tag-naming
    // convention, rewritten from an interactive console wizard into
    // structured async methods a Razor page binds against, same rewrite
    // style as the rest of this class.
    // ═══════════════════════════════════════════════════════════════════════

    public sealed record DeployReadinessResult(
        bool Ready,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<BrokenPack> BrokenPacks);

    public sealed record BrokenPack(string PackId, IReadOnlyList<string> Errors);

    /// <summary>
    /// Pre-flight check shown before either generating deploy.yml or
    /// creating a deploy tag — ported from DeployReadiness.cs's
    /// RepoType.Customer path only (Playtyper never works with PackWizard's
    /// separate pack-library-repo concept, so that branch has no equivalent
    /// here). Same "warn, don't block" philosophy as the original: this
    /// returns what it found rather than deciding FOR the caller whether to
    /// proceed. DeployPage shows Warnings/BrokenPacks and lets the person
    /// explicitly continue anyway, same as the CLI's AskYesNo prompts did —
    /// there are legitimate reasons to generate/tag ahead of finished
    /// content (e.g. testing the CI setup itself).
    ///
    /// Checks the bundle's packs list UNION defaultPack, not repo.GetPackIds():
    /// inject-bundle.sh reads defaultPack straight out of the bundle
    /// regardless of whether it also appears in the packs array, so that's
    /// the actual set of packs a deploy depends on.
    /// </summary>
    public static async Task<DeployReadinessResult> CheckDeployReadinessAsync(RemoteRepo repo)
    {
        var warnings = new List<string>();

        if (!await repo.FileExistsAsync(repo.CustomerBundleFile))
        {
            warnings.Add("Ingen bundle hittades — appen vet inte vilka packs som ska ingå. Lägg till minst ett pack i bundlen innan driftsättning.");
            return new DeployReadinessResult(false, warnings, Array.Empty<BrokenPack>());
        }

        var bundle = await BundleRepository.LoadAsync(repo, repo.CustomerBundleFile);
        var bundlePacks = bundle != null ? BundleRepository.GetPacks(bundle) : new List<string>();
        if (bundlePacks.Count == 0)
        {
            warnings.Add("Bundlen finns men innehåller inga packs än.");
            return new DeployReadinessResult(false, warnings, Array.Empty<BrokenPack>());
        }

        var defaultPack = bundle?["defaultPack"]?.GetValue<string>();
        var packsToCheck = bundlePacks.ToHashSet();
        if (!string.IsNullOrEmpty(defaultPack)) packsToCheck.Add(defaultPack);

        var broken = new List<BrokenPack>();
        foreach (var packId in packsToCheck)
        {
            var result = await Validator.ValidateAsync(repo, packId);
            if (!result.IsValid) broken.Add(new BrokenPack(packId, result.Errors));
        }

        if (broken.Count > 0)
            warnings.Add($"{broken.Count} pack som bundlen beror på validerar inte rent — se detaljer nedan.");

        return new DeployReadinessResult(broken.Count == 0, warnings, broken);
    }

    public sealed record DeployWorkflowConfig(
        bool DeployWeb,
        bool DeployAndroid,
        string PlaytypusRepo,      // "org/repo"
        string PlaytypusRef,       // branch/tag, t.ex. "main"
        string CloudflareProjectName,
        string? AndroidPackageName,
        string? PlaytypusToken,
        string? CloudflareApiToken,
        string? CloudflareAccountId,
        string? AndroidKeystoreBase64,
        string? AndroidKeyAlias,
        string? AndroidKeyPassword,
        string? AndroidKeystorePassword);

    public sealed record DeployWorkflowResult(bool Success, string? Error, IReadOnlyList<GitHubRepoService.SetSecretResult> SecretResults);

    /// <summary>
    /// Writes .github/workflows/deploy.yml and sets whichever secrets the
    /// caller actually supplied (empty/null fields are skipped rather than
    /// overwriting a secret already set on GitHub with a blank — see the
    /// filter below). Safe to re-run: PutFileAsync upserts the workflow file,
    /// and re-setting an unchanged secret is a no-op on GitHub's side.
    ///
    /// No pack-library support (PackWizard's --packs-lib): nothing else in
    /// Playtyper has a concept of a shared pack library separate from a
    /// customer's own bundle, so there is nothing to wire a toggle for yet.
    /// Straightforward to add later — see BuildDeployWorkflowYaml, which
    /// already threads a usePackLib bool through from the CLI original,
    /// just always false here.
    /// </summary>
    public static async Task<DeployWorkflowResult> EnsureDeployWorkflowAsync(RemoteRepo repo, DeployWorkflowConfig config)
    {
        if (!config.DeployWeb && !config.DeployAndroid)
            return new DeployWorkflowResult(false, "Välj minst en plattform (Web eller Android).", Array.Empty<GitHubRepoService.SetSecretResult>());

        var yaml = BuildDeployWorkflowYaml(
            config.PlaytypusRepo, config.PlaytypusRef,
            usePackLib: false, packLibRepo: "", packLibRef: "",
            config.DeployWeb, config.CloudflareProjectName,
            config.DeployAndroid, config.AndroidPackageName ?? "se.playtypus.app");

        // WriteFileAsync throws RemoteWriteException rather than returning an
        // error (see RemoteRepo.cs) — unlike the lower-level GitHubRepoService
        // calls elsewhere in this class that return (bool, string?)/string?.
        try
        {
            await repo.WriteFileAsync(repo.WorkflowFile, yaml, "driftsättning: uppdatera deploy.yml (Playtyper)");
        }
        catch (RemoteWriteException ex)
        {
            return new DeployWorkflowResult(false, ex.GitHubError, Array.Empty<GitHubRepoService.SetSecretResult>());
        }

        var secrets = new List<(string Name, string Value)>();
        void AddIfSet(string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) secrets.Add((name, value)); }

        AddIfSet("PLAYTYPUS_TOKEN", config.PlaytypusToken);
        if (config.DeployWeb)
        {
            AddIfSet("CLOUDFLARE_API_TOKEN", config.CloudflareApiToken);
            AddIfSet("CLOUDFLARE_ACCOUNT_ID", config.CloudflareAccountId);
        }
        if (config.DeployAndroid)
        {
            AddIfSet("ANDROID_KEYSTORE_BASE64", config.AndroidKeystoreBase64);
            AddIfSet("ANDROID_KEY_ALIAS", config.AndroidKeyAlias);
            AddIfSet("ANDROID_KEY_PASSWORD", config.AndroidKeyPassword);
            AddIfSet("ANDROID_KEYSTORE_PASSWORD", config.AndroidKeystorePassword);
        }

        var secretResults = secrets.Count > 0
            ? await GitHubRepoService.SetSecretsAsync(repo.Token, repo.OwnerRepo, secrets)
            : new List<GitHubRepoService.SetSecretResult>();

        return new DeployWorkflowResult(true, null, secretResults);
    }

    /// <summary>
    /// Ported near-verbatim from WorkflowMode.cs's BuildYaml — same job
    /// structure, same step order, so a Playtyper-generated deploy.yml reads
    /// identically to a PackWizard-generated one for anyone who already
    /// knows that format. One deliberate correction, not a faithful bug-for-
    /// bug port: the Android job below passes `--platform maui` to
    /// inject-bundle.sh, not `--platform android`. Checked
    /// scripts/inject-bundle.sh directly — it only ever accepts "web" or
    /// "maui" and hard-exits on anything else, so a workflow generated with
    /// the CLI's own `--platform android` would fail at that step on every
    /// Android deploy. Not something to silently carry forward into a file
    /// this method is generating fresh.
    /// </summary>
    private static string BuildDeployWorkflowYaml(
        string playtypusRepo, string playtypusRef,
        bool usePackLib, string packLibRepo, string packLibRef,
        bool deployWeb, string cfProject,
        bool deployAndroid, string androidPackageName)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# Playtypus — Deploy workflow");
        sb.AppendLine("# Genererad av Playtyper (Driftsättning)");
        sb.AppendLine("#");
        if (deployWeb) sb.AppendLine("#   web/v1.0.0     → Cloudflare Pages (webb)");
        if (deployAndroid) sb.AppendLine("#   android/v1.0.0 → GitHub Release (.aab)");
        sb.AppendLine("#");
        sb.AppendLine($"#   Playtypus-ramverk: {playtypusRepo}  @{playtypusRef}");
        if (usePackLib) sb.AppendLine($"#   Pack-bibliotek:   {packLibRepo}  @{packLibRef}");
        sb.AppendLine("#");
        sb.AppendLine("# Secrets (satta automatiskt av Playtyper Driftsättning):");
        sb.AppendLine("#   PLAYTYPUS_TOKEN        — GitHub PAT Contents:Read för ramverksrepot");
        if (deployWeb)
        {
            sb.AppendLine("#   CLOUDFLARE_API_TOKEN   — Cloudflare API Token");
            sb.AppendLine("#   CLOUDFLARE_ACCOUNT_ID  — Cloudflare Account ID");
        }
        if (deployAndroid)
        {
            sb.AppendLine("#   ANDROID_KEYSTORE_BASE64");
            sb.AppendLine("#   ANDROID_KEY_ALIAS / ANDROID_KEY_PASSWORD / ANDROID_KEYSTORE_PASSWORD");
        }
        sb.AppendLine();
        sb.AppendLine("on:");
        sb.AppendLine("  push:");
        sb.AppendLine("    tags:");
        if (deployWeb) sb.AppendLine("      - 'web/v*'");
        if (deployAndroid) sb.AppendLine("      - 'android/v*'");
        sb.AppendLine("  workflow_dispatch:");
        sb.AppendLine();
        sb.AppendLine("jobs:");

        if (deployWeb)
        {
            sb.AppendLine("  deploy-web:");
            sb.AppendLine("    if: startsWith(github.ref, 'refs/tags/web/') || github.event_name == 'workflow_dispatch'");
            sb.AppendLine("    runs-on: ubuntu-latest");
            sb.AppendLine("    steps:");
            sb.AppendLine("      - name: Checkout customer repo");
            sb.AppendLine("        uses: actions/checkout@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          path: customer");
            sb.AppendLine();
            sb.AppendLine($"      - name: Checkout Playtypus framework ({playtypusRepo}@{playtypusRef})");
            sb.AppendLine("        uses: actions/checkout@v4");
            sb.AppendLine("        with:");
            sb.AppendLine($"          repository: {playtypusRepo}");
            sb.AppendLine($"          ref: {playtypusRef}");
            sb.AppendLine("          token: ${{ secrets.PLAYTYPUS_TOKEN }}");
            sb.AppendLine("          path: playtypus-core");
            if (usePackLib)
            {
                sb.AppendLine();
                sb.AppendLine($"      - name: Checkout pack library ({packLibRepo}@{packLibRef})");
                sb.AppendLine("        uses: actions/checkout@v4");
                sb.AppendLine("        with:");
                sb.AppendLine($"          repository: {packLibRepo}");
                sb.AppendLine($"          ref: {packLibRef}");
                sb.AppendLine("          token: ${{ secrets.PLAYTYPUS_TOKEN }}");
                sb.AppendLine("          path: pack-library");
            }
            sb.AppendLine();
            sb.AppendLine("      - name: Setup .NET");
            sb.AppendLine("        uses: actions/setup-dotnet@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          dotnet-version: '10.0.x'");
            sb.AppendLine();
            sb.AppendLine("      - name: Inject bundle");
            sb.AppendLine("        run: |");
            sb.AppendLine("          bash playtypus-core/scripts/inject-bundle.sh \\");
            sb.AppendLine("            --packs-cust customer/packs \\");
            if (usePackLib) sb.AppendLine("            --packs-lib  pack-library/packs \\");
            sb.AppendLine("            --bundle customer/bundle/app-bundle.json \\");
            sb.AppendLine("            --target playtypus-core \\");
            sb.AppendLine("            --platform web");
            sb.AppendLine();
            sb.AppendLine("      - name: Rebuild content library");
            sb.AppendLine("        # Packs injiceras i Playtypus.Content/wwwroot EFTER checkout (steget ovan).");
            sb.AppendLine("        # RCL:ns static-web-assets-manifest byggs vid kompilering — utan denna");
            sb.AppendLine("        # explicita rebuild kan dotnet publish nedan använda ett manifest som");
            sb.AppendLine("        # inte känner till de nyss injicerade pack-filerna.");
            sb.AppendLine("        run: |");
            sb.AppendLine("          dotnet build playtypus-core/src/Playtypus.Content/Playtypus.Content.csproj -c Release");
            sb.AppendLine();
            sb.AppendLine("      - name: Build Blazor WASM");
            sb.AppendLine("        run: |");
            sb.AppendLine("          dotnet publish playtypus-core/src/Playtypus.Web/Playtypus.Web.csproj \\");
            sb.AppendLine("            -c Release -o publish/web");
            sb.AppendLine();
            sb.AppendLine("      - name: Deploy to Cloudflare Pages");
            sb.AppendLine("        uses: cloudflare/wrangler-action@v3");
            sb.AppendLine("        with:");
            sb.AppendLine("          apiToken: ${{ secrets.CLOUDFLARE_API_TOKEN }}");
            sb.AppendLine("          accountId: ${{ secrets.CLOUDFLARE_ACCOUNT_ID }}");
            sb.AppendLine($"          command: pages deploy publish/web/wwwroot --project-name={cfProject}");
            sb.AppendLine();
        }

        if (deployAndroid)
        {
            sb.AppendLine("  deploy-android:");
            sb.AppendLine("    if: startsWith(github.ref, 'refs/tags/android/') || github.event_name == 'workflow_dispatch'");
            sb.AppendLine("    runs-on: ubuntu-latest");
            sb.AppendLine("    steps:");
            sb.AppendLine("      - name: Checkout customer repo");
            sb.AppendLine("        uses: actions/checkout@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          path: customer");
            sb.AppendLine();
            sb.AppendLine($"      - name: Checkout Playtypus framework ({playtypusRepo}@{playtypusRef})");
            sb.AppendLine("        uses: actions/checkout@v4");
            sb.AppendLine("        with:");
            sb.AppendLine($"          repository: {playtypusRepo}");
            sb.AppendLine($"          ref: {playtypusRef}");
            sb.AppendLine("          token: ${{ secrets.PLAYTYPUS_TOKEN }}");
            sb.AppendLine("          path: playtypus-core");
            if (usePackLib)
            {
                sb.AppendLine();
                sb.AppendLine($"      - name: Checkout pack library ({packLibRepo}@{packLibRef})");
                sb.AppendLine("        uses: actions/checkout@v4");
                sb.AppendLine("        with:");
                sb.AppendLine($"          repository: {packLibRepo}");
                sb.AppendLine($"          ref: {packLibRef}");
                sb.AppendLine("          token: ${{ secrets.PLAYTYPUS_TOKEN }}");
                sb.AppendLine("          path: pack-library");
            }
            sb.AppendLine();
            sb.AppendLine("      - name: Setup .NET");
            sb.AppendLine("        uses: actions/setup-dotnet@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          dotnet-version: '10.0.x'");
            sb.AppendLine();
            sb.AppendLine("      - name: Setup Java");
            sb.AppendLine("        uses: actions/setup-java@v4");
            sb.AppendLine("        with:");
            sb.AppendLine("          distribution: 'temurin'");
            sb.AppendLine("          java-version: '17'");
            sb.AppendLine();
            sb.AppendLine("      - name: Inject bundle");
            sb.AppendLine("        run: |");
            sb.AppendLine("          bash playtypus-core/scripts/inject-bundle.sh \\");
            sb.AppendLine("            --packs-cust customer/packs \\");
            if (usePackLib) sb.AppendLine("            --packs-lib  pack-library/packs \\");
            sb.AppendLine("            --bundle customer/bundle/app-bundle.json \\");
            sb.AppendLine("            --target playtypus-core \\");
            sb.AppendLine("            --platform maui");
            sb.AppendLine();
            sb.AppendLine("      - name: Rebuild content library");
            sb.AppendLine("        run: |");
            sb.AppendLine("          dotnet build playtypus-core/src/Playtypus.Content/Playtypus.Content.csproj -c Release");
            sb.AppendLine();
            sb.AppendLine("      - name: Decode keystore");
            sb.AppendLine("        run: |");
            sb.AppendLine("          echo \"${{ secrets.ANDROID_KEYSTORE_BASE64 }}\" | base64 -d > release.jks");
            sb.AppendLine();
            sb.AppendLine("      - name: Build Android .aab");
            sb.AppendLine("        run: |");
            sb.AppendLine("          dotnet publish playtypus-core/src/Playtypus.App/Playtypus.App.csproj \\");
            sb.AppendLine("            -c Release \\");
            sb.AppendLine($"            /p:ApplicationId={androidPackageName} \\");
            sb.AppendLine("            /p:AndroidKeyStore=true \\");
            sb.AppendLine("            /p:AndroidSigningKeyStore=release.jks \\");
            sb.AppendLine("            /p:AndroidSigningKeyAlias=${{ secrets.ANDROID_KEY_ALIAS }} \\");
            sb.AppendLine("            /p:AndroidSigningKeyPass=${{ secrets.ANDROID_KEY_PASSWORD }} \\");
            sb.AppendLine("            /p:AndroidSigningStorePass=${{ secrets.ANDROID_KEYSTORE_PASSWORD }}");
            sb.AppendLine();
            sb.AppendLine("      - name: Publish GitHub Release");
            sb.AppendLine("        uses: softprops/action-gh-release@v2");
            sb.AppendLine("        with:");
            sb.AppendLine("          files: '**/*.aab'");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Best-effort read-back of an existing deploy.yml into a (secrets-less)
    /// config, so DeployPage can pre-fill "regenerate/update workflow" with
    /// whatever was configured last time instead of a blank form. Secrets
    /// are never in the YAML to begin with (only ${{ secrets.X }}
    /// references) — those fields are always null here regardless of what
    /// was set previously, which is correct: this is a form pre-fill, not a
    /// secret-recovery mechanism, and GitHub's API doesn't expose secret
    /// values to read back even if this tried to.
    ///
    /// Regex against this method's OWN known output shape (BuildDeployWorkflowYaml
    /// above), not a general YAML parser — safe specifically because both
    /// sides are maintained together here. Returns null on anything
    /// unexpected (hand-edited file, older format) rather than guessing.
    /// </summary>
    public static DeployWorkflowConfig? TryParseDeployWorkflowConfig(string yaml)
    {
        var repoMatch = Regex.Match(yaml, @"repository:\s*(\S+)");
        var refMatch = Regex.Match(yaml, @"ref:\s*(\S+)");
        if (!repoMatch.Success || !refMatch.Success) return null;

        var cfMatch = Regex.Match(yaml, @"--project-name=(\S+)");
        var androidPkgMatch = Regex.Match(yaml, @"/p:ApplicationId=(\S+?)\s*\\?\s*$", RegexOptions.Multiline);

        return new DeployWorkflowConfig(
            DeployWeb: yaml.Contains("deploy-web:"),
            DeployAndroid: yaml.Contains("deploy-android:"),
            PlaytypusRepo: repoMatch.Groups[1].Value,
            PlaytypusRef: refMatch.Groups[1].Value,
            CloudflareProjectName: cfMatch.Success ? cfMatch.Groups[1].Value : "",
            AndroidPackageName: androidPkgMatch.Success ? androidPkgMatch.Groups[1].Value : null,
            PlaytypusToken: null, CloudflareApiToken: null, CloudflareAccountId: null,
            AndroidKeystoreBase64: null, AndroidKeyAlias: null, AndroidKeyPassword: null, AndroidKeystorePassword: null);
    }

    public sealed record DeployTagResult(bool Success, string TagName, string? Error);

    /// <summary>
    /// The method GAPS.md §5 named directly. Creates "{platform}/v{version}"
    /// pointing at main's current commit — deploy.yml's `on.push.tags`
    /// trigger (see BuildDeployWorkflowYaml above) picks it up from there,
    /// same tag-naming convention as DeployMode.cs. Fails fast with a clear
    /// message if deploy.yml doesn't exist yet rather than creating a tag
    /// that triggers nothing — that failure mode (tag created, nothing
    /// visibly happens, no error anywhere) is exactly the kind of silent gap
    /// this method should not hand the person.
    /// </summary>
    public static async Task<DeployTagResult> CreateDeployTagAsync(RemoteRepo repo, string platform, string version)
    {
        var cleanVersion = version.Trim().TrimStart('v');
        if (!Version.TryParse(cleanVersion, out _))
            return new DeployTagResult(false, "", $"\"{version}\" är inte ett giltigt versionsnummer (förväntat MAJOR.MINOR.PATCH, t.ex. 1.2.0).");

        var tagName = $"{platform}/v{cleanVersion}";

        if (!await repo.FileExistsAsync(repo.WorkflowFile))
            return new DeployTagResult(false, tagName,
                $"{repo.WorkflowFile} finns inte än — generera arbetsflödet först (se avsnittet ovan) innan du skapar en driftsättningstagg.");

        // CreateTagAsync resolves fromBranch → SHA internally (see
        // GitHubRepoService.cs) — no separate GetBranchShaAsync call needed here.
        var error = await GitHubRepoService.CreateTagAsync(repo.Token, repo.OwnerRepo, tagName, repo.Branch);
        return new DeployTagResult(error == null, tagName, error);
    }

    /// <summary>
    /// Ported from DeployMode.cs's SuggestNextVersion — same semver-patch-bump
    /// default (1.2.0 → 1.2.1). Returns "1.0.0" when no matching tag exists yet.
    /// </summary>
    public static async Task<string> SuggestNextDeployVersionAsync(RemoteRepo repo, string platform)
    {
        var prefix = $"{platform}/v";
        var tags = await GitHubRepoService.ListTagsAsync(repo.Token, repo.OwnerRepo, prefix);

        var versions = tags
            .Select(t => t.Length > prefix.Length ? t[prefix.Length..] : "")
            .Select(v => Version.TryParse(v, out var parsed) ? parsed : null)
            .Where(v => v != null)
            .Select(v => v!)
            .ToList();

        if (versions.Count == 0) return "1.0.0";
        var latest = versions.Max()!;
        return $"{latest.Major}.{latest.Minor}.{Math.Max(latest.Build, 0) + 1}";
    }
}

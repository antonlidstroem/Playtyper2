using Playtyper.Shared.Services;

namespace Playtyper.Shared;

public enum RepoType
{
    /// <summary>Det vanliga fallet: ett pack/bundle/CI per repo (bundle/app-bundle.json på roten).</summary>
    Customer,

    /// <summary>Äldre modell: flera bundles i samma repo (bundles/{id}/app-bundle.json).</summary>
    PackLibrary,

    /// <summary>Repot finns men innehåller varken bundle/ eller packs/ ännu (helt tomt anslutet repo).</summary>
    Unknown
}

/// <summary>
/// Porterad från PackWizard (CLI) till Playtyper (Blazor WASM + MAUI Hybrid).
///
/// Ett "repo" är inte en mapp på disk — det är ett GitHub-repo identifierat
/// av (token, "org/repo"). Playtyper bryr sig aldrig om lokal disk för
/// kundinnehåll: allt som LÄSER eller SKRIVER går via GitHubRepoService,
/// dvs. HTTPS mot GitHubs API.
///
/// SKILLNAD MOT PACKWIZARD-ORIGINALET: alla I/O-metoder är nu genuint
/// asynkrona (XxxAsync + async/await) istället för att linda GitHubRepoService
/// i AsyncHelper.Run(...) (en synkron blockerande väntan). Det mönstret var
/// säkert i en konsolapp med en trådpool bakom sig, men i Blazor WebAssembly
/// är UI-tråden encelig — ett blockerande .Wait()/.Result där skulle antingen
/// frysa gränssnittet eller (i värsta fall) deadlocka. MAUI Blazor Hybrid har
/// fler trådar tillgängliga, men vi vill ha EN kodväg som är korrekt på båda
/// värdarna, inte två.
///
/// Lättviktig in-memory-cache för GetPackIdsAsync()/GetBundleIdsAsync()
/// eftersom de anropas ofta (varje navigationsrendering) men sällan ändras
/// inom en session. Ogiltigförklaras automatiskt efter varje skrivning/radering.
/// </summary>
public sealed class RemoteRepo
{
    public string Token     { get; }
    public string OwnerRepo { get; }   // "org/repo" — känt direkt, kräver aldrig ett nätverksanrop
    public string Branch    { get; }   // i praktiken alltid "main"
    public RepoType Type    { get; private set; }

    public RemoteRepo(string token, string ownerRepo, RepoType type, string branch = "main")
    {
        Token     = token;
        OwnerRepo = ownerRepo;
        Type      = type;
        Branch    = branch;
    }

    public void SetType(RepoType type) => Type = type;

    // ── Repo-relativa sökvägar (rena strängar — inget nätverksanrop) ──────────

    public string CustomerBundleFile => "bundle/app-bundle.json";
    public string WorkflowFile       => ".github/workflows/deploy.yml";
    public string PacksDir           => "packs";
    public string BundlesDir         => "bundles";

    public string PackDir(string packId)        => $"packs/{packId}";
    public string PackConfigPath(string packId) => $"packs/{packId}/pack.config.json";

    public string BundlePath(string bundleId) =>
        Type == RepoType.Customer ? CustomerBundleFile : $"bundles/{bundleId}/app-bundle.json";

    // ── Länkar (för UI) ────────────────────────────────────────────────────

    public string HtmlUrl => $"https://github.com/{OwnerRepo}";
    public string FileUrl(string path) => $"https://github.com/{OwnerRepo}/blob/{Branch}/{path}";
    public string ActionsUrl => $"https://github.com/{OwnerRepo}/actions";
    public string SecretsUrl => $"https://github.com/{OwnerRepo}/settings/secrets/actions";

    // ── Repo-typ-avgörande (statisk — körs INNAN en instans finns) ───────────

    /// <summary>
    /// Avgör repo-typ genom att fråga GitHub om bundle/ resp. packs/ har
    /// något innehåll.
    /// </summary>
    public static async Task<RepoType> DetectTypeRemoteAsync(string token, string ownerRepo, string branch = "main")
    {
        if (await GitHubRepoService.DirectoryExistsAsync(token, ownerRepo, "bundle", branch))
            return RepoType.Customer;
        if (await GitHubRepoService.DirectoryExistsAsync(token, ownerRepo, "packs", branch))
            return RepoType.PackLibrary;
        return RepoType.Unknown;
    }

    // ── Fil-I/O ────────────────────────────────────────────────────────────

    // Litet existens-cache: navigering/statuskontroller frågar om samma
    // välkända markörfiler (bundle/app-bundle.json, .github/workflows/deploy.yml)
    // varje gång ett skal ritas om. Utan cache vore det 2+ HTTP-anrop per
    // rendering för data som sällan ändras. Ogiltigförklaras automatiskt
    // av InvalidateListings() (anropas av alla skriv/raderingsmetoder nedan).
    private readonly Dictionary<string, bool> _existsCache = new();

    public async Task<bool> FileExistsAsync(string path)
    {
        if (_existsCache.TryGetValue(path, out var cached)) return cached;
        var exists = await GitHubRepoService.FileExistsAsync(Token, OwnerRepo, path, Branch);
        _existsCache[path] = exists;
        return exists;
    }

    public async Task<string?> ReadFileAsync(string path) =>
        (await GitHubRepoService.GetFileAsync(Token, OwnerRepo, path, Branch))?.Content;

    /// <summary>
    /// Skapar eller uppdaterar en fil på GitHub. Kastar RemoteWriteException
    /// vid fel.
    /// </summary>
    public async Task WriteFileAsync(string path, string content, string commitMessage)
    {
        var error = await GitHubRepoService.PutFileAsync(Token, OwnerRepo, path, content, Branch, commitMessage);
        if (error != null) throw new RemoteWriteException(path, error);
        InvalidateListings();
        _existsCache[path] = true;
    }

    /// <summary>
    /// Skapar en NY fil — används bara när anroparen redan vet att filen inte
    /// finns än (t.ex. scaffolding direkt efter att repot skapats). Sparar en
    /// GET jämfört med WriteFileAsync, men är fortfarande säker: om filen mot
    /// förmodan redan finns faller GitHubRepoService.CreateFileAsync
    /// automatiskt tillbaka till den vanliga upsert-vägen.
    /// </summary>
    public async Task CreateFileAsync(string path, string content, string commitMessage)
    {
        var error = await GitHubRepoService.CreateFileAsync(Token, OwnerRepo, path, content, Branch, commitMessage);
        if (error != null) throw new RemoteWriteException(path, error);
        InvalidateListings();
        _existsCache[path] = true;
    }

    public async Task DeleteFileAsync(string path, string commitMessage)
    {
        var error = await GitHubRepoService.DeleteFileAsync(Token, OwnerRepo, path, Branch, commitMessage);
        if (error != null) throw new RemoteWriteException(path, error);
        InvalidateListings();
        _existsCache[path] = false;
    }

    /// <summary>
    /// Tar bort en hel "mapp" (alla filer under ett path-prefix). GitHub har
    /// inget begrepp för tomma mappar — en mapp är bara ett gemensamt
    /// path-prefix — så detta innebär att varje fil under prefixet raderas
    /// för sig. Returnerar antalet raderade filer.
    /// </summary>
    public async Task<int> DeleteDirectoryAsync(string pathPrefix, string commitMessage)
    {
        var files = await GitHubRepoService.ListFilesRecursiveAsync(Token, OwnerRepo, pathPrefix, Branch);

        foreach (var f in files)
        {
            var error = await GitHubRepoService.DeleteFileAsync(Token, OwnerRepo, f, Branch, commitMessage);
            if (error != null) throw new RemoteWriteException(f, error);
        }

        InvalidateListings();
        return files.Count;
    }

    // ── Listningar (cachead — se klassdoc) ────────────────────────────────

    private List<string>? _packIdsCache;
    private List<string>? _bundleIdsCache;

    public async Task<IReadOnlyList<string>> GetPackIdsAsync()
    {
        if (_packIdsCache == null)
        {
            var entries = await GitHubRepoService.ListDirectoryAsync(Token, OwnerRepo, PacksDir, Branch);
            _packIdsCache = entries.Where(e => e.IsDir).Select(e => e.Name).OrderBy(n => n).ToList();
        }
        return _packIdsCache;
    }

    public async Task<IReadOnlyList<string>> GetBundleIdsAsync()
    {
        if (_bundleIdsCache != null) return _bundleIdsCache;

        if (Type == RepoType.Customer)
        {
            _bundleIdsCache = await FileExistsAsync(CustomerBundleFile)
                ? new List<string> { "bundle" }
                : new List<string>();
            return _bundleIdsCache;
        }

        var entries = await GitHubRepoService.ListDirectoryAsync(Token, OwnerRepo, BundlesDir, Branch);
        _bundleIdsCache = entries.Where(e => e.IsDir).Select(e => e.Name).OrderBy(n => n).ToList();
        return _bundleIdsCache;
    }

    /// <summary>
    /// Måste anropas efter operationer som kan ha lagt till/tagit bort ett
    /// helt pack eller en bundle, så att nästa GetPackIdsAsync()/
    /// GetBundleIdsAsync() hämtar färsk data istället för en inaktuell cache.
    /// Anropas automatiskt av WriteFileAsync/DeleteFileAsync/DeleteDirectoryAsync ovan.
    /// </summary>
    public void InvalidateListings()
    {
        _packIdsCache   = null;
        _bundleIdsCache = null;
        _existsCache.Clear();
    }
}

/// <summary>Kastas av RemoteRepo.WriteFileAsync/DeleteFileAsync när GitHub-anropet misslyckas.</summary>
public sealed class RemoteWriteException(string path, string gitHubError)
    : Exception($"Kunde inte skriva '{path}' till GitHub: {gitHubError}")
{
    public string Path        { get; } = path;
    public string GitHubError { get; } = gitHubError;
}

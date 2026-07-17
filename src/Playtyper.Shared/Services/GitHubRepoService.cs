using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Playtyper.Shared.Services;

/// <summary>
/// v12 — GitHub REST API-klient. EFTER v12 är detta den ENDA platsen i hela
/// PackWizard som läser eller skriver innehåll i ett kund-repo. Det finns
/// inget lokalt repo, ingen lokal klon och inget beroende av "git" eller
/// "keytool" för repo-operationer (keytool används fortfarande, men bara
/// för att generera en Android-keystore — se KeystoreService).
///
/// PackWizard kan startas från VILKEN mapp som helst på maskinen. Den enda
/// platsen som spelar roll är vilket GitHub-repo som är aktivt (repo-namnet
/// skickas in per anrop av anroparen, som håller reda på det via AppState)
/// — allt annat skickas och hämtas direkt mot GitHubs API över HTTPS.
///
/// Slår ihop de tidigare separata GitHubApiService (token/repo/secrets) och
/// GitHubPackService (pack-uppladdning) till en enda väldokumenterad klient,
/// och lägger till de generiska fil/mapp/tagg-operationer som tidigare
/// gjordes lokalt med File/Directory/git:
///   - GetFileAsync / PutFileAsync / DeleteFileAsync   (Contents API)
///   - ListDirectoryAsync / ListFilesRecursiveAsync     (Contents + Trees API)
///   - CreateTagAsync / ListTagsAsync / DeleteTagAsync  (Git Data API — ersätter git CLI)
/// </summary>
public static class GitHubRepoService
{
    private const string ApiBase   = "https://api.github.com";
    private const string UserAgent = "PackWizard/6.0";

    // ── Tokenvalidering ───────────────────────────────────────────────────────

    public static async Task<(bool Valid, string? Login, string? Error)> ValidateTokenAsync(string token)
    {
        try
        {
            using var http = CreateClient(token);
            var resp = await http.GetAsync($"{ApiBase}/user");

            if (!resp.IsSuccessStatusCode)
                return (false, null, $"HTTP {(int)resp.StatusCode} — {resp.ReasonPhrase}");

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var login = doc.RootElement.GetProperty("login").GetString();
            return (true, login, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    // ── Repo-existenskontroll / skapande ──────────────────────────────────────

    public static async Task<bool> RepoExistsAsync(string token, string ownerRepo)
    {
        try
        {
            using var http = CreateClient(token);
            var resp = await http.GetAsync($"{ApiBase}/repos/{ownerRepo}");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public sealed record CreateRepoResult(bool Success, string? HtmlUrl, string? Error);

    /// <summary>
    /// Skapar ett nytt GitHub-repo.
    ///   org.Length > 0  → POST /orgs/{org}/repos
    ///   org.Length == 0 → POST /user/repos  (under inloggad användare)
    /// </summary>
    public static async Task<CreateRepoResult> CreateRepoAsync(
        string token, string org, string repoName,
        string description = "", bool isPrivate = true)
    {
        try
        {
            using var http = CreateClient(token);

            var endpoint = string.IsNullOrWhiteSpace(org)
                ? $"{ApiBase}/user/repos"
                : $"{ApiBase}/orgs/{org}/repos";

            var payload = JsonSerializer.Serialize(new
            {
                name         = repoName,
                description  = description,
                @private     = isPrivate,
                auto_init    = true,    // GitHub skapar initial commit + main-branch åt oss.
                                        // Eliminerar race condition där Git Data API inte är
                                        // redo direkt efter repo-skapandet (se EnsureMainBranchAsync).
                has_issues   = true,
                has_projects = false,
                has_wiki     = false,
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(endpoint, content);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return new CreateRepoResult(false, null, ParseGitHubError(body, resp.StatusCode));

            using var doc = JsonDocument.Parse(body);
            var htmlUrl   = doc.RootElement.GetProperty("html_url").GetString();
            return new CreateRepoResult(true, htmlUrl, null);
        }
        catch (Exception ex)
        {
            return new CreateRepoResult(false, null, ex.Message);
        }
    }

    /// <summary>
    /// Säkerställer att main-branchen existerar.
    ///
    /// Strategi (i ordning):
    ///   1. Polla 3 gånger med 1,2 s mellanrum — hanterar auto_init-fördröjning
    ///      (GitHub skapar branchen inom ~2 s, men API:et svarar inte alltid direkt)
    ///   2. Om main fortfarande saknas: skapa initial commit manuellt via Git Data API
    ///      (hanterar repon skapade med auto_init:false, t.ex. från äldre PackWizard)
    ///
    /// Skapar INTE repot om det saknas — det görs explicit via CreateRepoAsync.
    /// </summary>
    public static async Task<(bool Ok, string? Error)> EnsureMainBranchAsync(string token, string ownerRepo)
    {
        // Steg 1: Polla (hanterar auto_init-race condition)
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) await Task.Delay(1200);
            try
            {
                using var http = CreateClient(token);
                var resp = await http.GetAsync($"{ApiBase}/repos/{ownerRepo}/git/ref/heads/main");
                if (resp.IsSuccessStatusCode) return (true, null);
                if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    return (false, ParseGitHubError(body, resp.StatusCode));
                }
            }
            catch (Exception ex) when (attempt == 2)
            {
                return (false, ex.Message);
            }
        }

        // Steg 2: Skapa initial commit manuellt (repot är helt tomt — ingen auto_init)
        using var http2 = CreateClient(token);
        var error = await CreateInitialCommitAsync(http2, ownerRepo);
        return error == null ? (true, null) : (false, error);
    }

    /// <summary>
    /// Skapar en minimal initial commit (blob → tree → commit → ref) direkt
    /// via Git Data API, utan något lokalt "git"-kommando.
    /// Används som fallback i EnsureMainBranchAsync när repot är helt tomt
    /// (dvs. skapat med auto_init:false eller om auto_init misslyckades).
    /// </summary>
    private static async Task<string?> CreateInitialCommitAsync(HttpClient http, string ownerRepo)
    {
        // Blob
        var readmeB64  = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("# Playtypus content pack repo\n\nSkapat av PackWizard.\n"));
        var blobResp = await http.PostAsync(
            $"{ApiBase}/repos/{ownerRepo}/git/blobs",
            new StringContent(
                JsonSerializer.Serialize(new { content = readmeB64, encoding = "base64" }),
                Encoding.UTF8, "application/json"));
        if (!blobResp.IsSuccessStatusCode)
            return $"Kunde inte skapa initial blob (HTTP {(int)blobResp.StatusCode})";

        using var blobDoc = JsonDocument.Parse(await blobResp.Content.ReadAsStringAsync());
        var blobSha = blobDoc.RootElement.GetProperty("sha").GetString()!;

        // Tree
        var treeResp = await http.PostAsync(
            $"{ApiBase}/repos/{ownerRepo}/git/trees",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    tree = new[] { new { path = "README.md", mode = "100644", type = "blob", sha = blobSha } }
                }),
                Encoding.UTF8, "application/json"));
        if (!treeResp.IsSuccessStatusCode) return "Kunde inte skapa initial tree";

        using var treeDoc = JsonDocument.Parse(await treeResp.Content.ReadAsStringAsync());
        var treeSha = treeDoc.RootElement.GetProperty("sha").GetString()!;

        // Commit
        var commitResp = await http.PostAsync(
            $"{ApiBase}/repos/{ownerRepo}/git/commits",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    message = "Initial commit (PackWizard)",
                    tree    = treeSha,
                    parents = Array.Empty<string>(),
                }),
                Encoding.UTF8, "application/json"));
        if (!commitResp.IsSuccessStatusCode) return "Kunde inte skapa initial commit";

        using var commitDoc = JsonDocument.Parse(await commitResp.Content.ReadAsStringAsync());
        var commitSha = commitDoc.RootElement.GetProperty("sha").GetString()!;

        // Ref
        var refResp = await http.PostAsync(
            $"{ApiBase}/repos/{ownerRepo}/git/refs",
            new StringContent(
                JsonSerializer.Serialize(new { @ref = "refs/heads/main", sha = commitSha }),
                Encoding.UTF8, "application/json"));

        return refResp.IsSuccessStatusCode ? null : "Kunde inte skapa main-branch ref";
    }

    // ── Generiska filoperationer (Contents API) ───────────────────────────────

    public sealed record FileResult(string Content, string Sha);

    /// <summary>Hämtar innehållet i en fil. Returnerar null om filen inte finns.</summary>
    public static async Task<FileResult?> GetFileAsync(
        string token, string ownerRepo, string path, string branch = "main")
    {
        using var http = CreateClient(token);
        var encodedPath = EncodePath(path);
        var resp = await http.GetAsync(
            $"{ApiBase}/repos/{ownerRepo}/contents/{encodedPath}?ref={Uri.EscapeDataString(branch)}");

        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("content", out var contentProp)) return null;

        var b64   = contentProp.GetString()?.Replace("\n", "") ?? "";
        var bytes = Convert.FromBase64String(b64);
        var sha   = doc.RootElement.GetProperty("sha").GetString() ?? "";
        return new FileResult(Encoding.UTF8.GetString(bytes), sha);
    }

    /// <summary>Sant om filen finns på angiven branch.</summary>
    public static async Task<bool> FileExistsAsync(
        string token, string ownerRepo, string path, string branch = "main") =>
        await GetFileAsync(token, ownerRepo, path, branch) != null;

    /// <summary>
    /// Skapar eller uppdaterar en fil (upsert). Hämtar befintligt SHA automatiskt
    /// om filen redan finns. Returnerar felmeddelande, eller null vid success.
    /// </summary>
    public static async Task<string?> PutFileAsync(
        string token, string ownerRepo, string path, string fileContent,
        string branch, string commitMessage)
    {
        using var http = CreateClient(token);
        var encodedPath = EncodePath(path);
        var apiPath      = $"{ApiBase}/repos/{ownerRepo}/contents/{encodedPath}";

        string? existingSha = null;
        var getResp = await http.GetAsync($"{apiPath}?ref={Uri.EscapeDataString(branch)}");
        if (getResp.IsSuccessStatusCode)
        {
            using var getDoc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync());
            existingSha = getDoc.RootElement.TryGetProperty("sha", out var shaProp) ? shaProp.GetString() : null;
        }

        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileContent));
        var payload = existingSha != null
            ? JsonSerializer.Serialize(new { message = commitMessage, content = b64, sha = existingSha, branch })
            : JsonSerializer.Serialize(new { message = commitMessage, content = b64, branch });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var putResp = await http.PutAsync(apiPath, content);
        if (putResp.IsSuccessStatusCode) return null;

        var body = await putResp.Content.ReadAsStringAsync();
        return ParseGitHubError(body, putResp.StatusCode, path);
    }

    /// <summary>
    /// Skapar en NY fil utan att först göra en GET för att hämta sha.
    /// Används bara där anroparen redan VET att filen inte finns än (t.ex.
    /// scaffolding direkt efter att ett repo skapats) — sparar ett HTTP-anrop
    /// per fil jämfört med PutFileAsync, som alltid gör GET-innan-PUT för att
    /// stödja både create och update.
    ///
    /// Om antagandet visar sig fel (filen fanns redan — GitHub svarar 422
    /// "sha wasn't supplied") faller metoden automatiskt tillbaka till den
    /// vanliga GET-och-PUT-vägen (PutFileAsync), så resultatet blir korrekt
    /// även om den optimistiska vägen missar. Detta gör CreateFileAsync
    /// idempotent att köra om (t.ex. om en scaffold avbryts och körs igen).
    /// </summary>
    public static async Task<string?> CreateFileAsync(
        string token, string ownerRepo, string path, string fileContent,
        string branch, string commitMessage)
    {
        using var http = CreateClient(token);
        var encodedPath = EncodePath(path);
        var apiPath      = $"{ApiBase}/repos/{ownerRepo}/contents/{encodedPath}";

        var b64     = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileContent));
        var payload = JsonSerializer.Serialize(new { message = commitMessage, content = b64, branch });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var putResp = await http.PutAsync(apiPath, content);
        if (putResp.IsSuccessStatusCode) return null;

        // 422 = "sha wasn't supplied" — filen fanns redan mot förmodan.
        // Fall tillbaka till den vanliga upsert-vägen (GET + PUT) en gång.
        if (putResp.StatusCode == HttpStatusCode.UnprocessableEntity)
            return await PutFileAsync(token, ownerRepo, path, fileContent, branch, commitMessage);

        var body = await putResp.Content.ReadAsStringAsync();
        return ParseGitHubError(body, putResp.StatusCode, path);
    }

    /// <summary>
    /// Tar bort en fil. No-op (returnerar null) om filen redan inte finns.
    /// </summary>
    public static async Task<string?> DeleteFileAsync(
        string token, string ownerRepo, string path, string branch, string commitMessage)
    {
        using var http = CreateClient(token);
        var encodedPath = EncodePath(path);
        var apiPath      = $"{ApiBase}/repos/{ownerRepo}/contents/{encodedPath}";

        var getResp = await http.GetAsync($"{apiPath}?ref={Uri.EscapeDataString(branch)}");
        if (!getResp.IsSuccessStatusCode) return null; // finns redan inte — inget att göra

        using var getDoc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync());
        var sha = getDoc.RootElement.TryGetProperty("sha", out var shaProp) ? shaProp.GetString() : null;
        if (sha == null) return null;

        var payload = JsonSerializer.Serialize(new { message = commitMessage, sha, branch });

        var req = new HttpRequestMessage(HttpMethod.Delete, apiPath)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        var delResp = await http.SendAsync(req);
        if (delResp.IsSuccessStatusCode) return null;

        var body = await delResp.Content.ReadAsStringAsync();
        return ParseGitHubError(body, delResp.StatusCode, path);
    }

    // ── Mapplistning ──────────────────────────────────────────────────────────

    public sealed record DirEntry(string Name, bool IsDir);

    /// <summary>
    /// Listar innehållet i en mapp (en nivå). Returnerar en tom lista — INTE
    /// ett fel — om mappen inte finns, eftersom GitHub inte har ett begrepp
    /// för tomma mappar (en "mapp" är bara ett gemensamt path-prefix).
    /// </summary>
    public static async Task<List<DirEntry>> ListDirectoryAsync(
        string token, string ownerRepo, string path, string branch = "main")
    {
        using var http = CreateClient(token);
        var encodedPath = EncodePath(path);
        var resp = await http.GetAsync(
            $"{ApiBase}/repos/{ownerRepo}/contents/{encodedPath}?ref={Uri.EscapeDataString(branch)}");

        if (!resp.IsSuccessStatusCode) return new List<DirEntry>();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var entries = new List<DirEntry>();

        // Om sökvägen är en fil (inte en mapp) returnerar GitHub ett objekt,
        // inte en array — då finns inga "barn" att lista.
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return entries;

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString() ?? "";
            var type = entry.GetProperty("type").GetString() ?? "";
            entries.Add(new DirEntry(name, type == "dir"));
        }

        return entries;
    }

    /// <summary>Sant om mappen har minst en fil/undermapp.</summary>
    public static async Task<bool> DirectoryExistsAsync(
        string token, string ownerRepo, string path, string branch = "main") =>
        (await ListDirectoryAsync(token, ownerRepo, path, branch)).Count > 0;

    /// <summary>
    /// Listar alla filer (rekursivt) under ett path-prefix, via Git Trees API.
    /// Returnerar fullständiga repo-relativa sökvägar, t.ex. "packs/foo/theme.css".
    /// </summary>
    public static async Task<List<string>> ListFilesRecursiveAsync(
        string token, string ownerRepo, string pathPrefix, string branch = "main")
    {
        using var http = CreateClient(token);
        var resp = await http.GetAsync(
            $"{ApiBase}/repos/{ownerRepo}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1");

        if (!resp.IsSuccessStatusCode) return new List<string>();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("tree", out var tree)) return new List<string>();

        var prefix = pathPrefix.TrimEnd('/') + "/";
        var result = new List<string>();
        foreach (var entry in tree.EnumerateArray())
        {
            if (!entry.TryGetProperty("type", out var t) || t.GetString() != "blob") continue;
            var p = entry.GetProperty("path").GetString();
            if (p != null && p.StartsWith(prefix, StringComparison.Ordinal)) result.Add(p);
        }
        return result;
    }

    // ── Branchar ──────────────────────────────────────────────────────────────

    public static async Task<string?> GetBranchShaAsync(string token, string ownerRepo, string branch)
    {
        using var http = CreateClient(token);
        var resp = await http.GetAsync(
            $"{ApiBase}/repos/{ownerRepo}/git/ref/heads/{Uri.EscapeDataString(branch)}");
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("object").GetProperty("sha").GetString();
    }

    /// <summary>
    /// Skapar branchen från fromBranch:s HEAD om den inte redan finns.
    /// Om fromBranch (vanligtvis "main") saknar commits initieras den
    /// automatiskt via EnsureMainBranchAsync innan preview-branchen skapas.
    /// </summary>
    public static async Task<string?> EnsureBranchAsync(
        string token, string ownerRepo, string branch, string fromBranch = "main")
    {
        using var http = CreateClient(token);
        var encoded   = Uri.EscapeDataString(branch);
        var checkResp = await http.GetAsync($"{ApiBase}/repos/{ownerRepo}/git/ref/heads/{encoded}");
        if (checkResp.IsSuccessStatusCode) return null; // finns redan

        var sha = await GetBranchShaAsync(token, ownerRepo, fromBranch);
        if (sha == null)
        {
            // fromBranch saknas (tomt repo) — initiera den innan vi skapar preview-branchen
            var (ok, err) = await EnsureMainBranchAsync(token, ownerRepo);
            if (!ok) return $"Repot har ingen '{fromBranch}'-branch och kunde inte initieras: {err}";

            sha = await GetBranchShaAsync(token, ownerRepo, fromBranch);
            if (sha == null) return $"Kunde inte hämta HEAD-SHA för '{fromBranch}' trots initiering";
        }

        var payload = JsonSerializer.Serialize(new { @ref = $"refs/heads/{branch}", sha });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var createResp = await http.PostAsync($"{ApiBase}/repos/{ownerRepo}/git/refs", content);

        return createResp.IsSuccessStatusCode ? null : $"Kunde inte skapa branch '{branch}'";
    }

    public sealed record MergeResult(bool Success, string? Error);

    /// <summary>Mergar head-branchen till base-branchen via GitHubs Merge API.</summary>
    public static async Task<MergeResult> MergeBranchAsync(
        string token, string ownerRepo, string head, string @base, string commitMessage)
    {
        using var http = CreateClient(token);
        var payload = JsonSerializer.Serialize(new { @base, head, commit_message = commitMessage });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await http.PostAsync($"{ApiBase}/repos/{ownerRepo}/merges", content);

        if (resp.IsSuccessStatusCode) return new MergeResult(true, null);
        // 204/409 ("Already merged" / "No common ancestor" -- inget kvar att göra) räknas som ok.
        if ((int)resp.StatusCode is 204 or 409) return new MergeResult(true, null);

        var body = await resp.Content.ReadAsStringAsync();
        return new MergeResult(false, ParseGitHubError(body, resp.StatusCode));
    }

    // ── Taggar (ersätter lokal "git tag" + "git push") ────────────────────────

    /// <summary>
    /// Skapar en (lightweight) tagg som pekar på HEAD av fromBranch.
    /// Detta är det som triggar GitHub Actions-workflowen — motsvarar
    /// "git tag X && git push origin X" men helt utan lokal git.
    /// </summary>
    public static async Task<string?> CreateTagAsync(
        string token, string ownerRepo, string tagName, string fromBranch = "main")
    {
        var sha = await GetBranchShaAsync(token, ownerRepo, fromBranch);
        if (sha == null) return $"Kunde inte hämta HEAD-SHA för '{fromBranch}'";

        using var http = CreateClient(token);
        var payload = JsonSerializer.Serialize(new { @ref = $"refs/tags/{tagName}", sha });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await http.PostAsync($"{ApiBase}/repos/{ownerRepo}/git/refs", content);

        if (resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadAsStringAsync();
        return ParseGitHubError(body, resp.StatusCode);
    }

    /// <summary>Listar taggnamn som matchar ett prefix, t.ex. "web/v" → ["web/v1.0.0", ...].</summary>
    public static async Task<List<string>> ListTagsAsync(string token, string ownerRepo, string prefix)
    {
        using var http = CreateClient(token);
        var resp = await http.GetAsync(
            $"{ApiBase}/repos/{ownerRepo}/git/matching-refs/tags/{Uri.EscapeDataString(prefix)}");

        if (!resp.IsSuccessStatusCode) return new List<string>();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var tags = new List<string>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var r = entry.GetProperty("ref").GetString(); // "refs/tags/web/v1.0.0"
            if (r != null && r.StartsWith("refs/tags/")) tags.Add(r["refs/tags/".Length..]);
        }
        return tags;
    }

    public static async Task<string?> DeleteTagAsync(string token, string ownerRepo, string tagName)
    {
        using var http = CreateClient(token);
        var resp = await http.DeleteAsync(
            $"{ApiBase}/repos/{ownerRepo}/git/refs/tags/{Uri.EscapeDataString(tagName)}");
        if (resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadAsStringAsync();
        return ParseGitHubError(body, resp.StatusCode);
    }

    // ── Secrets ───────────────────────────────────────────────────────────────

    public sealed record SetSecretResult(string SecretName, bool Success, string? Error);

    /// <summary>
    /// Sätter en lista GitHub Actions Secrets på ett repo via API.
    /// Krypterar varje secret med repots public key (libsodium sealed box),
    /// enligt: https://docs.github.com/en/rest/actions/secrets
    /// </summary>
    public static async Task<List<SetSecretResult>> SetSecretsAsync(
        string token, string ownerRepo, IEnumerable<(string Name, string Value)> secrets)
    {
        var results = new List<SetSecretResult>();

        var (publicKey, keyId) = await GetRepoPublicKeyAsync(token, ownerRepo);
        if (publicKey == null || keyId == null)
        {
            foreach (var (name, _) in secrets)
                results.Add(new SetSecretResult(name, false, "Kunde inte hämta repots public key"));
            return results;
        }

        using var http = CreateClient(token);

        foreach (var (name, value) in secrets)
        {
            try
            {
                var encrypted = EncryptSecret(publicKey, value);
                var payload = JsonSerializer.Serialize(new { encrypted_value = encrypted, key_id = keyId });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var resp = await http.PutAsync(
                    $"{ApiBase}/repos/{ownerRepo}/actions/secrets/{name}", content);

                if (resp.IsSuccessStatusCode)
                {
                    results.Add(new SetSecretResult(name, true, null));
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    results.Add(new SetSecretResult(name, false, ParseGitHubError(body, resp.StatusCode)));
                }
            }
            catch (Exception ex)
            {
                results.Add(new SetSecretResult(name, false, ex.Message));
            }
        }

        return results;
    }

    private static async Task<(string? PublicKey, string? KeyId)> GetRepoPublicKeyAsync(
        string token, string ownerRepo)
    {
        try
        {
            using var http = CreateClient(token);
            var resp = await http.GetAsync($"{ApiBase}/repos/{ownerRepo}/actions/secrets/public-key");
            if (!resp.IsSuccessStatusCode) return (null, null);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return (doc.RootElement.GetProperty("key").GetString(),
                    doc.RootElement.GetProperty("key_id").GetString());
        }
        catch
        {
            return (null, null);
        }
    }

    private static string EncryptSecret(string base64PublicKey, string secretValue)
    {
        var publicKey   = Convert.FromBase64String(base64PublicKey);
        var secretBytes = Encoding.UTF8.GetBytes(secretValue);
        // GitHub kräver crypto_box_seal (sealed/anonymous box) — inte crypto_box.
        var encrypted = SecretsCrypto.CryptoBoxSeal(secretBytes, publicKey);
        return Convert.ToBase64String(encrypted);
    }

    // ── Pack-uppladdning (preview-branch + promote) ───────────────────────────
    //
    // Behålls som ett separat, namngivet flöde (istället för att bara lutas
    // på de generiska fil-metoderna ovan) eftersom det har en egen, medveten
    // branch-strategi:
    //   preview/{packId}  → Cloudflare Pages branch-deploy (förhandsgranskning)
    //   main               → live
    // Används av NewPackMode för nya/regenererade packs. Alla andra ändringar
    // (redigering av enstaka fält, radering, lösenord, bundle) går direkt mot
    // main via RemoteFs, eftersom de inte har samma behov av förhandsgranskning.

    public sealed record UploadResult(bool Success, string? PreviewBranch, string? Error);

    public static async Task<UploadResult> UploadPackAsync(
        string token, string ownerRepo, string packId, Dictionary<string, string> files)
    {
        var branch = $"preview/{packId}";

        var branchError = await EnsureBranchAsync(token, ownerRepo, branch);
        if (branchError != null) return new UploadResult(false, null, branchError);

        foreach (var (filename, content) in files)
        {
            var path  = $"packs/{packId}/{filename}";
            var error = await PutFileAsync(token, ownerRepo, path, content, branch,
                $"Add {filename} for pack {packId}");
            if (error != null) return new UploadResult(false, null, $"{filename}: {error}");
        }

        return new UploadResult(true, branch, null);
    }

    public static async Task<MergeResult> PromotePackToMainAsync(
        string token, string ownerRepo, string packId) =>
        await MergeBranchAsync(token, ownerRepo, $"preview/{packId}", "main",
            $"Promote pack {packId} to main");

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Path-segment måste URL-kodas individuellt, INTE som helhet — annars
    /// blir "/" kodat till %2F och GitHub tolkar hela sökvägen som ett enda
    /// filnamn istället för en nästlad sökväg.
    /// </summary>
    private static string EncodePath(string path) =>
        string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

    private static string ParseGitHubError(string body, HttpStatusCode status, string? path = null)
    {
        string message;
        try
        {
            using var doc = JsonDocument.Parse(body);
            message = doc.RootElement.TryGetProperty("message", out var m)
                ? m.GetString() ?? $"HTTP {(int)status}"
                : $"HTTP {(int)status}";
        }
        catch
        {
            message = $"HTTP {(int)status}";
        }

        // GitHub kräver ett SEPARAT scope/permission för att skriva filer under
        // .github/workflows/ — även om token redan har full Contents-åtkomst.
        // Utan det scopet svarar GitHub med exakt "Resource not accessible by
        // personal access token" (403), vilket annars är ett väldigt kryptiskt
        // fel för en användare som redan tycker sig ha rätt behörigheter.
        var isWorkflowPath = path != null &&
            path.Replace('\\', '/').TrimStart('/').StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase);

        if (isWorkflowPath && status == HttpStatusCode.Forbidden &&
            message.Contains("Resource not accessible", StringComparison.OrdinalIgnoreCase))
        {
            message +=
                "\n     → Din GitHub-token saknar behörighet att skriva .github/workflows/-filer. " +
                "Detta är ett EXTRA scope utöver vanlig Contents-åtkomst.\n" +
                "       Classic token: lägg till scopet \"workflow\" (utöver \"repo\").\n" +
                "       Fine-grained token: sätt \"Workflows: Read and write\" under Repository permissions.\n" +
                "       Uppdatera sedan token i [9] Inställningar och kör [4] igen.";
        }

        return message;
    }

    private static HttpClient CreateClient(string token)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        http.Timeout = TimeSpan.FromSeconds(30);
        return http;
    }
}

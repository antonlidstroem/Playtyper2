using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playtyper.Shared.Services;

/// <summary>
/// Thin wrapper over the Cloudflare Pages API — ported from
/// tools/PackWizard/src/Services/CloudflareApiService.cs in the Playtypus
/// repo (its own WorkflowMode CLI wizard), needed here for the same reason:
/// generating a working deploy.yml (GitHubOperator.EnsureDeployWorkflowAsync)
/// means a Cloudflare Pages project has to actually exist for
/// `wrangler pages deploy --project-name=X` to succeed against.
///
/// Difference from the CLI original: no GlobalSettings.ResolveCredentials
/// equivalent — Playtyper has no local settings file the way a CLI tool
/// does, so the API token/account ID are whatever the Driftsättning page's
/// own form fields hold for this session (see DeployPage.razor). Everything
/// else is a close, mostly mechanical port: same endpoints, same response
/// shapes.
/// </summary>
public static class CloudflareApiService
{
    private static readonly HttpClient _http = new() { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };

    public record TokenCheckResult(bool Valid, string? Error);

    /// <summary>Verifies an API token is well-formed and actually accepted by Cloudflare before it's saved as a GitHub secret — catches a pasted-wrong-token typo immediately instead of at the next deploy, several steps removed from where the mistake was made.</summary>
    public static async Task<TokenCheckResult> VerifyTokenAsync(string apiToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "user/tokens/verify");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            using var res = await _http.SendAsync(req);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new TokenCheckResult(false, "Token avvisades av Cloudflare (401) — kontrollera att den kopierades helt.");
            if (!res.IsSuccessStatusCode)
                return new TokenCheckResult(false, $"Cloudflare svarade {(int)res.StatusCode}.");

            var body = await res.Content.ReadFromJsonAsync<CfEnvelope<object>>();
            return body?.Success == true
                ? new TokenCheckResult(true, null)
                : new TokenCheckResult(false, FirstError(body));
        }
        catch (Exception ex) { return new TokenCheckResult(false, ex.Message); }
    }

    public record ProjectResult(bool Success, bool AlreadyExisted, string? Error, string? LiveUrl);

    /// <summary>
    /// Ensures a Cloudflare Pages project with this name exists — checks
    /// first (GET), creates it (POST) only if missing, matching
    /// WorkflowMode's own "check, only create if truly absent" order so
    /// re-running the Driftsättning setup for an already-configured app is
    /// harmless rather than erroring on a duplicate-name conflict.
    /// production_branch is fixed to "main": deploy.yml's tag-triggered
    /// workflow always runs from whatever ref the tag points at, and Pages'
    /// own "production" concept is only used for the project's default
    /// *.pages.dev URL, not for gating which pushes deploy.
    /// </summary>
    public static async Task<ProjectResult> EnsureProjectAsync(string apiToken, string accountId, string projectName)
    {
        try
        {
            using var checkReq = new HttpRequestMessage(HttpMethod.Get, $"accounts/{accountId}/pages/projects/{Uri.EscapeDataString(projectName)}");
            checkReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            using var checkRes = await _http.SendAsync(checkReq);
            if (checkRes.IsSuccessStatusCode)
            {
                var existing = await checkRes.Content.ReadFromJsonAsync<CfEnvelope<CfProject>>();
                return new ProjectResult(true, true, null, BuildLiveUrl(existing?.Result?.Subdomain));
            }
            if (checkRes.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var errBody = await checkRes.Content.ReadFromJsonAsync<CfEnvelope<object>>();
                return new ProjectResult(false, false, $"Kunde inte slå upp projektet ({(int)checkRes.StatusCode}): {FirstError(errBody)}", null);
            }

            using var createReq = new HttpRequestMessage(HttpMethod.Post, $"accounts/{accountId}/pages/projects");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            createReq.Content = JsonContent.Create(new { name = projectName, production_branch = "main" });
            using var createRes = await _http.SendAsync(createReq);
            var createBody = await createRes.Content.ReadFromJsonAsync<CfEnvelope<CfProject>>();
            return createRes.IsSuccessStatusCode && createBody?.Success == true
                ? new ProjectResult(true, false, null, BuildLiveUrl(createBody.Result?.Subdomain))
                : new ProjectResult(false, false, FirstError(createBody), null);
        }
        catch (Exception ex) { return new ProjectResult(false, false, ex.Message, null); }
    }

    /// <summary>
    /// Best-effort live-URL lookup for the Driftsättning tag section — never
    /// blocks tag creation on failure, just omits the "Live på:" line if it
    /// can't confirm one. The URL is deterministic from the project name and
    /// available as soon as the project exists, whether or not a deployment
    /// has ever actually run.
    /// </summary>
    public static async Task<string?> TryGetProjectUrlAsync(string apiToken, string accountId, string projectName)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"accounts/{accountId}/pages/projects/{Uri.EscapeDataString(projectName)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            using var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<CfEnvelope<CfProject>>();
            return BuildLiveUrl(body?.Result?.Subdomain);
        }
        catch { return null; }
    }

    /// <summary>
    /// Cloudflare's own API is inconsistent about whether "subdomain" is a
    /// bare subdomain ("my-app") or already a full hostname
    /// ("my-app.customdomain.com") — same defensive check as the original
    /// PackWizard CLI tool this was ported from.
    /// </summary>
    private static string? BuildLiveUrl(string? subdomain) =>
        string.IsNullOrWhiteSpace(subdomain) ? null
        : subdomain.Contains('.') ? $"https://{subdomain}"
        : $"https://{subdomain}.pages.dev";

    private static string? FirstError<T>(CfEnvelope<T>? body) =>
        body?.Errors is { Count: > 0 } errs ? errs[0].Message : null;

    private sealed class CfEnvelope<T>
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("result")] public T? Result { get; set; }
        [JsonPropertyName("errors")] public List<CfError>? Errors { get; set; }
    }

    private sealed class CfError
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }

    private sealed class CfProject
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        // Cloudflare returns the *.pages.dev hostname (no scheme) here, e.g. "playtypus-kommun.pages.dev".
        [JsonPropertyName("subdomain")] public string? Subdomain { get; set; }
    }
}

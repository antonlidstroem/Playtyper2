using Microsoft.JSInterop;

namespace Playtyper.Shared.Services;

/// <summary>
/// ICredentialStore för Playtyper.Web (Blazor WASM) — sessionStorage.
/// Registreras i Playtyper.Web/Program.cs. Se ICredentialStore.cs för varför
/// MAUI-huvudet (Playtyper.App) registrerar en annan implementation
/// (SecureCredentialStore, i Playtyper.App/Services/ — den kan inte ligga
/// här i Shared eftersom den behöver Microsoft.Maui.Storage, som inte
/// existerar i en WASM-kontext).
/// </summary>
public sealed class SessionCredentialStore(IJSRuntime js) : ICredentialStore
{
    private const string Key = "playtyper.github.token";

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            return await js.InvokeAsync<string?>("playtyperInterop.sessionGet", Key);
        }
        catch
        {
            // JS-interop kan kasta om det anropas innan sidan är fullt
            // interaktiv (prerendering) - hellre "ingen token sparad" än en
            // krasch vid appstart.
            return null;
        }
    }

    public async Task SetTokenAsync(string token) =>
        await js.InvokeVoidAsync("playtyperInterop.sessionSet", Key, token);

    public async Task ClearTokenAsync() =>
        await js.InvokeVoidAsync("playtyperInterop.sessionRemove", Key);
}

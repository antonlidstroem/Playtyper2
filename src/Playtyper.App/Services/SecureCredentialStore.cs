using Playtyper.Shared.Services;

namespace Playtyper.App.Services;

/// <summary>
/// ICredentialStore för Playtyper.App (MAUI Hybrid). Använder
/// Microsoft.Maui.Storage.SecureStorage — Keychain på iOS/macCatalyst,
/// Android Keystore-backad EncryptedSharedPreferences på Android, DPAPI på
/// Windows. Kan inte ligga i Playtyper.Shared: Microsoft.Maui.Storage finns
/// inte i en WASM-kontext, så en delad fil skulle inte kompilera för Web.
///
/// Till skillnad från SessionCredentialStore (Web) rensas INTE token här
/// mellan sessioner — en installerad app förväntas rimligen komma ihåg att
/// du är inloggad mellan uppstarter, och SecureStorage är dessutom striktare
/// säkrare än sessionStorage någonsin kan vara i en webbläsare.
/// </summary>
public sealed class SecureCredentialStore : ICredentialStore
{
    private const string Key = "playtyper.github.token";

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(Key);
        }
        catch
        {
            // SecureStorage kan kasta om OS-nyckelringen inte är tillgänglig
            // (t.ex. enhet utan skärmlås konfigurerat på vissa Android-
            // versioner) - hellre "ingen token sparad" än en krasch vid start.
            return null;
        }
    }

    public Task SetTokenAsync(string token)
    {
        SecureStorage.Default.Set(Key, token);
        return Task.CompletedTask;
    }

    public Task ClearTokenAsync()
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}

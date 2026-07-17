namespace Playtyper.Shared.Services;

/// <summary>
/// Lagring för GitHub-token — den enda hemligheten Playtyper självt hanterar
/// (allt annat, som PLAYTYPUS_TOKEN, går rakt in i GitHubs egna krypterade
/// secrets-lager och rör aldrig den här klienten igen efter att den satts).
///
/// TVÅ IMPLEMENTATIONER, VALDA PER VÄRD — det här är den enda platsen i hela
/// Playtyper.Shared där Web och App faktiskt BEHÖVER olika beteende:
///
///   - Playtyper.Web (Blazor WASM):  SessionCredentialStore — sessionStorage.
///     Rensas när fliken stängs. Matchar hur webbverktyget redan beskrevs i
///     strategifasen: "man loggar in för en session, inte permanent".
///
///   - Playtyper.App (MAUI Hybrid):  SecureCredentialStore — plattformens
///     OS-krypterade nyckelring (Keychain på iOS/macCatalyst, EncryptedShared-
///     Preferences via Android Keystore på Android, DPAPI på Windows) via
///     Microsoft.Maui.Storage.SecureStorage. En installerad app förväntas
///     rimligen komma ihåg att du är inloggad mellan uppstarter — och
///     SecureStorage är dessutom STRIKT säkrare än något webbläsar-API
///     skulle kunna vara, så det är inte bara bekvämare utan också en
///     genuin förbättring, inte en avvägning.
///
/// Registreras i respektive värds Program.cs/MauiProgram.cs — se
/// kommentarerna där. Resten av Playtyper.Shared injicerar bara
/// ICredentialStore och bryr sig aldrig om vilken det är.
/// </summary>
public interface ICredentialStore
{
    Task<string?> GetTokenAsync();
    Task SetTokenAsync(string token);
    Task ClearTokenAsync();
}

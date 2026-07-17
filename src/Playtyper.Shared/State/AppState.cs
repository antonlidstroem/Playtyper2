namespace Playtyper.Shared.State;

using Playtyper.Shared.Services;

/// <summary>
/// Registreras som Scoped (Web: en gång per sidladdning/flik. MAUI: en gång
/// per appstart, samma sak i praktiken eftersom en Blazor Hybrid-app
/// normalt bara har en enda WebView-instans igång).
///
/// Håller det Blazors URL-routing inte kan bära åt oss: den aktiva
/// RemoteRepo-anslutningen och (om ett pack är öppet) dess PackDraft.
/// Sidor läser/skriver hit istället för att skicka runt de objekten som
/// parametrar genom flera nivåer av komponenter.
/// </summary>
public sealed class AppState
{
    public string? Login { get; private set; }
    public string? Token { get; private set; }
    public RemoteRepo? Repo { get; private set; }
    public PackDraft? ActiveDraft { get; private set; }

    public event Action? Changed;

    public void SetSession(string login, string token)
    {
        Login = login;
        Token = token;
        Changed?.Invoke();
    }

    public void SetRepo(RemoteRepo repo)
    {
        Repo = repo;
        Changed?.Invoke();
    }

    public void SetActiveDraft(PackDraft? draft)
    {
        ActiveDraft = draft;
        Changed?.Invoke();
    }

    public void SignOut()
    {
        Login = null;
        Token = null;
        Repo = null;
        ActiveDraft = null;
        Changed?.Invoke();
    }

    public bool IsConnected => Repo != null;
}

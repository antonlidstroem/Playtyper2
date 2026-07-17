using Microsoft.Extensions.Logging;
using Playtyper.Shared.Services;
using Playtyper.Shared.State;
using Playtyper.App.Services;
using Playtypus.Core.Services;

namespace Playtyper.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { });

        builder.Services.AddMauiBlazorWebView();

        // MAUI-specifika implementationer — se ICredentialStore.cs och
        // AppHistoryStore.cs i Playtyper.Shared för resonemanget kring varför
        // det installerade appen medvetet får en annan (säkrare, mer
        // beständig) lagringsstrategi än webbversionen.
        builder.Services.AddSingleton<ICredentialStore, SecureCredentialStore>();
        builder.Services.AddSingleton<IAppHistoryStore, PreferencesAppHistoryStore>();
        builder.Services.AddSingleton<LocalDraftCache>();
        builder.Services.AddSingleton<AppState>();

        // Live preview (GAPS.md §2) — see the matching comment in
        // Playtyper.Web/Program.cs; AddPlaytypusCore() itself always uses
        // AddScoped (see PlaytypusCoreServiceCollectionExtensions.cs), which
        // is intentional even here: MAUI's single BlazorWebView gets its own
        // DI scope, so Scoped-within-that-scope already behaves like
        // Singleton-per-app in practice, the same way the registrations
        // above are Singleton explicitly. No need for a second, MAUI-only
        // copy of the extension just to swap AddScoped for AddSingleton.
        //
        // KNOWN GAP: the preview's iframe+postMessage approach (see
        // PreviewPanel.razor) is verified against Playtyper.Web. It SHOULD
        // work identically here — same standard HTML/JS mechanism, and
        // BlazorWebView is a standards-compliant engine (WebView2/WKWebView/
        // Android WebView) under the hood — but this repo has no MAUI build
        // environment available to actually confirm it renders in the
        // Android/Windows app. Worth an explicit check before relying on it
        // there; see GAPS.md's updated preview section.
        builder.Services.AddScoped(sp => new HttpClient());
        builder.Services.AddPlaytypusCore();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Playtyper.Shared.Components;
using Playtyper.Shared.Services;
using Playtyper.Shared.State;
using Playtypus.Core.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<PlaytyperApp>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Web-specifika implementationer — se ICredentialStore.cs / AppHistoryStore.cs
// för varför MAUI-huvudet (Playtyper.App/MauiProgram.cs) registrerar andra.
builder.Services.AddScoped<ICredentialStore, SessionCredentialStore>();
builder.Services.AddScoped<IAppHistoryStore, LocalStorageAppHistoryStore>();
builder.Services.AddScoped<LocalDraftCache>();
builder.Services.AddScoped<AppState>();

// Live preview (GAPS.md §2) renders Playtypus.Core's real AppShell inside
// /preview-frame — see Components/Screens/PreviewFramePage.razor. That route
// runs in the SAME WASM app/DI container as everything else (it is reached
// via an <iframe>, not a separate host), so AppShell's dependencies need to
// be registered here exactly like Playtypus.Web's own Program.cs does — see
// PlaytypusCoreServiceCollectionExtensions.cs for why this is one call
// instead of the ~30 AddScoped lines Playtypus.Web itself used to have
// inline. HttpClient is registered even though the preview's own data path
// never uses it (PackContext/LanguageService still take one via constructor
// injection regardless of which Init*/Load* method ends up called), and
// costs nothing unused.
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddPlaytypusCore();

await builder.Build().RunAsync();

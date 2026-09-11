using DYS.Molargo.Services;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Documents;
using DYS.Molargo.Shared.Extensions;
using DYS.Molargo.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DYS.Molargo;

/// <summary>
/// The device head's composition root. Thin by design: it registers the two things only a
/// device can answer — the form factor and the database path — and hands everything else
/// to <c>DYS.Molargo.Shared</c>.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // Archivo is loaded by the Blazor stylesheet as a webfont, not here. This
                // registration is only for the native MAUI chrome around the WebView.
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // The platform-specific half — the only things this head knows that Shared cannot.
        builder.Services.AddSingleton<IFormFactor, FormFactor>();
        builder.Services.AddSingleton<IDatabasePathProvider, MauiDatabasePathProvider>();
        builder.Services.AddSingleton<IDocumentPathProvider, MauiDocumentPathProvider>();
        builder.Services.AddSingleton<IDocumentViewer, MauiDocumentViewer>();

        // Everything else: view models, feature services, navigation, session, the clock.
        builder.Services.AddMolargoCore();
        builder.Services.AddMolargoLocalStore();

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

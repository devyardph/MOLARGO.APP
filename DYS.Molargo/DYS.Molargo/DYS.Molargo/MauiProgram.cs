using DYS.Molargo.Platform;
using DYS.Molargo.Shared.Api;
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
    /// <summary>
    /// The practice server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A constant for now, and it must not stay one: a shipped build points every practice
    /// at whatever was compiled in. It belongs in the device's own settings, entered once
    /// alongside the clinic code at first run, and this is the single place that changes
    /// when it moves.
    /// </para>
    /// <para>
    /// http on localhost for development only. Anything else must be https — a bearer token
    /// and a clinic's records travel on this connection, and in the clear they are readable
    /// by anything between the tablet and the server.
    /// </para>
    /// </remarks>
    private const string ApiAddress = "http://localhost:5165";

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
        // No database path among them any more: the records are on the server.
        builder.Services.AddSingleton<IFormFactor, FormFactor>();
        builder.Services.AddSingleton<IDocumentPathProvider, MauiDocumentPathProvider>();
        builder.Services.AddSingleton<IDocumentViewer, MauiDocumentViewer>();

        // Everything a screen needs, wherever the records live.
        builder.Services.AddMolargoShell();

        // And where they live. Exactly one of these, never both.
        //
        // The server is the store: every transaction goes through the API, so a booking
        // made here is a booking the other surgery can already see. That is the whole
        // reason for it — a diary that is only right on the device that wrote it is not a
        // diary a practice can share.
        //
        // The cost, stated here because it is the thing to know: the app does not work
        // without a connection. There is no local copy, and a dropped connection stops the
        // front desk rather than slowing it down.
        builder.Services.AddMolargoApiStore(new MolargoApiOptions
        {
            BaseAddress = new Uri(ApiAddress),
        });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();

        // And to a file, because AddDebug writes to the attached debugger's output window
        // and an app started any other way writes its exceptions nowhere. Blazor catches an
        // unhandled exception, shows its "Something went wrong" bar, and logs the detail —
        // so without this the one thing needed to diagnose that bar is the one thing thrown
        // away. The path is printed on startup so it does not have to be hunted for.
        var logPath = Path.Combine(FileSystem.CacheDirectory, "molargo-log.txt");

        builder.Logging.AddProvider(new FileLoggerProvider(logPath));

        Console.WriteLine($"Molargo log: {logPath}");
#endif

        return builder.Build();
    }
}

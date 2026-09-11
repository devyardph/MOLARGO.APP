using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Repositories;
using DYS.Molargo.Shared.Services;

namespace DYS.Molargo.Shared.Features.Admin.Services;

/// <summary>
/// A rendered test page, and what would have happened to it.
/// </summary>
/// <param name="Page">The receipt as it would be printed, at the configured width.</param>
/// <param name="Reached">
/// Whether it went to a printer. Always false today, and named rather than implied so no
/// caller can mistake a rendered page for a printed one.
/// </param>
/// <param name="Detail">Why, in a sentence a receptionist can act on.</param>
public sealed record TestPrint(string Page, bool Reached, string Detail);

/// <summary>
/// Where receipts and printed documents go, and what one would look like.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="INotificationSettingsService"/> even though both back the one
/// Settings screen. That one holds a credential and opens a socket; this one holds a device
/// address and opens nothing. Merging them would put the printer's fields behind the mail
/// account's validation rules for no reason.
/// </remarks>
public interface IPrinterSettingsService
{
    /// <summary>
    /// The clinic's printer settings, creating an unsaved default row on first read.
    /// </summary>
    Task<PrinterSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Saves the settings. Returns a refusal, or null.</summary>
    Task<string?> SaveAsync(PrinterSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Renders the receipt that would print, and says whether anything received it.
    /// </summary>
    /// <param name="settings">
    /// The settings to lay the page out with — the ones on screen, not the ones in the
    /// database. A test that ignores what was just typed tests the wrong thing, and the
    /// paper width is exactly what somebody would change and then test.
    /// </param>
    Task<TestPrint> TestPrintAsync(PrinterSettings settings, CancellationToken ct = default);
}

/// <inheritdoc cref="IPrinterSettingsService"/>
public sealed class PrinterSettingsService : IPrinterSettingsService
{
    /// <summary>
    /// The most copies of one script worth printing.
    /// </summary>
    /// <remarks>
    /// Two covers the usual pair — one to the patient, one for the file. The cap exists
    /// because a typo in this box is a hundred pages off a shared printer, and a script is
    /// a document a pharmacy dispenses against: extra copies of one are a real risk, not
    /// just wasted paper.
    /// </remarks>
    private const int MaxPrescriptionCopies = 3;

    private readonly IRepository<PrinterSettings> _settings;
    private readonly IRepository<PracticeLocation> _locations;
    private readonly IRepository<AuditEntry> _audit;
    private readonly IRepository<Provider> _providers;
    private readonly ISessionService _session;
    private readonly IPracticeGuard _guard;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public PrinterSettingsService(
        IRepository<PrinterSettings> settings,
        IRepository<PracticeLocation> locations,
        IRepository<AuditEntry> audit,
        IRepository<Provider> providers,
        ISessionService session,
        ITenantContext tenant,
        IPracticeGuard guard,
        IClock clock)
    {
        _settings = settings;
        _locations = locations;
        _audit = audit;
        _providers = providers;
        _session = session;
        _tenant = tenant;
        _guard = guard;
        _clock = clock;
    }

    public async Task<PrinterSettings> GetAsync(CancellationToken ct = default)
    {
        var rows = await _settings.ListAsync(ct: ct).ConfigureAwait(false);

        // One row per clinic, and the tenant filter already confines this to one.
        return rows.FirstOrDefault() ?? new PrinterSettings();
    }

    public async Task<string?> SaveAsync(
        PrinterSettings settings, CancellationToken ct = default)
    {
        var denied = await _guard
            .RefuseAsync(PracticePermissions.ManageSettings, ct)
            .ConfigureAwait(false);

        if (denied is not null) return denied;

        if (settings.PrintReceiptAutomatically
            && string.IsNullOrWhiteSpace(settings.DeviceAddress))
        {
            // Otherwise every payment silently fails to print, and the setting says it is
            // printing automatically. Better to refuse than to promise.
            return AddressPrompt(settings.Connection);
        }

        if (settings.Connection == PrinterConnection.Network
            && settings.NetworkPort is < 1 or > 65535)
        {
            return "The printer port has to be between 1 and 65535. 9100 is the usual one.";
        }

        if (settings.PrescriptionCopies is < 1 or > MaxPrescriptionCopies)
        {
            return $"Print between 1 and {MaxPrescriptionCopies} copies of a prescription.";
        }

        if (settings.OpenCashDrawer && settings.Connection == PrinterConnection.Bluetooth)
        {
            // A drawer is kicked by the printer over its own port. Bluetooth counter
            // printers are the mobile ones, which have no drawer port at all — the switch
            // would be on and nothing would ever open.
            return "A Bluetooth printer has no cash drawer port. Turn the drawer off, or "
                + "connect the printer by network or USB.";
        }

        var existing = await GetAsync(ct).ConfigureAwait(false);
        var changed = Describe(existing, settings);

        existing.Connection = settings.Connection;
        existing.DeviceAddress = Trim(settings.DeviceAddress);
        existing.NetworkPort = settings.NetworkPort;
        existing.ReceiptPrinterName = Trim(settings.ReceiptPrinterName);
        existing.Paper = settings.Paper;
        existing.PrintReceiptAutomatically = settings.PrintReceiptAutomatically;
        existing.OpenCashDrawer = settings.OpenCashDrawer;
        existing.PrintLogo = settings.PrintLogo;
        existing.ReceiptFooter = Trim(settings.ReceiptFooter);
        existing.DocumentPrinterName = Trim(settings.DocumentPrinterName);
        existing.DocumentPaper = settings.DocumentPaper;
        existing.PrescriptionCopies = settings.PrescriptionCopies;
        existing.PrintPrescriberDetails = settings.PrintPrescriberDetails;

        await _settings.SaveAsync(existing, ct).ConfigureAwait(false);
        await RecordAsync(existing.Id, changed, ct).ConfigureAwait(false);

        return null;
    }

    public async Task<TestPrint> TestPrintAsync(
        PrinterSettings settings, CancellationToken ct = default)
    {
        var locations = await _locations.ListAsync(ct: ct).ConfigureAwait(false);
        var site = locations.OrderBy(location => location.DisplayOrder).FirstOrDefault();

        var receipt = ReceiptLayout.Sample(
            site?.Name ?? _tenant.TenantName ?? "Molargo",
            AddressOf(site),
            site?.Phone,
            site?.Abn,
            _clock.UtcNow.ToLocalTime(),
            _session.UserDisplayName,
            settings.ReceiptFooter);

        var page = ReceiptLayout.Render(receipt, settings.ReceiptColumns);

        // Not sent, and said outright. There is no print path in this app at all: no
        // socket is opened to a network printer, and nothing reaches USB or Bluetooth
        // either. Rendering it is the honest half of the job — the layout, the paper width
        // and the footer are all really being exercised here, so a wrong roll width or a
        // footer that overflows shows up now rather than at the counter.
        var detail = string.IsNullOrWhiteSpace(settings.DeviceAddress)
            ? "Rendered below. No printer is recorded yet, and nothing was sent — this app "
                + "has no print path."
            : $"Rendered below at {settings.ReceiptColumns} characters. Nothing was sent to "
                + $"{settings.DeviceAddress} — this app has no print path yet.";

        // Not audited. Nothing changed and nothing left the machine, and an audit entry
        // against a settings row that may never have been saved is a dangling reference in
        // the one table that has to be trustworthy.
        return new TestPrint(page, Reached: false, detail);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>What the address field is called for a given connection.</summary>
    /// <remarks>
    /// Public because the screen labels the same field from here. Two copies of these three
    /// words drift, and then the refusal names a field the operator cannot find.
    /// </remarks>
    public static string AddressLabel(PrinterConnection connection) => connection switch
    {
        PrinterConnection.Network => "Host name or IP",
        PrinterConnection.Usb => "USB device name",
        _ => "Pairing name",
    };

    public static string ConnectionLabel(PrinterConnection connection) => connection switch
    {
        PrinterConnection.Network => "Network",
        PrinterConnection.Usb => "USB",
        _ => "Bluetooth",
    };

    public static string PaperLabel(ReceiptPaper paper) =>
        paper == ReceiptPaper.Roll58mm ? "58 mm" : "80 mm";

    /// <summary>The same three words mid-sentence.</summary>
    /// <remarks>
    /// Written out rather than lowercased from <see cref="AddressLabel"/>, which turned
    /// "Host name or IP" into "host name or ip".
    /// </remarks>
    private static string AddressPrompt(PrinterConnection connection)
    {
        var field = connection switch
        {
            PrinterConnection.Network => "host name or IP",
            PrinterConnection.Usb => "USB device name",
            _ => "pairing name",
        };

        return $"Add the printer's {field} before printing receipts automatically.";
    }

    private static string? AddressOf(PracticeLocation? site)
    {
        if (site is null) return null;

        var parts = new[] { site.AddressLine, site.Suburb, site.State, site.Postcode }
            .Where(part => !string.IsNullOrWhiteSpace(part));

        var joined = string.Join(" ", parts);

        return joined.Length == 0 ? null : joined;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>What changed, in words, for the audit entry.</summary>
    /// <remarks>
    /// The device address is safe to record — unlike the mail account's password, it is an
    /// address on the practice's own network, and knowing which printer a receipt went to
    /// is the point of auditing this at all.
    /// </remarks>
    private static string Describe(PrinterSettings before, PrinterSettings after)
    {
        var parts = new List<string>();

        if (before.Connection != after.Connection
            || !string.Equals(before.DeviceAddress, Trim(after.DeviceAddress), StringComparison.Ordinal))
        {
            parts.Add(
                $"receipt printer set to {ConnectionLabel(after.Connection)} "
                + $"{Trim(after.DeviceAddress) ?? "(none)"}");
        }

        if (before.Paper != after.Paper)
        {
            parts.Add($"paper set to {PaperLabel(after.Paper)}");
        }

        if (before.PrintReceiptAutomatically != after.PrintReceiptAutomatically)
        {
            parts.Add(after.PrintReceiptAutomatically
                ? "receipts set to print automatically"
                : "automatic receipt printing turned off");
        }

        if (before.OpenCashDrawer != after.OpenCashDrawer)
        {
            parts.Add(after.OpenCashDrawer ? "cash drawer on" : "cash drawer off");
        }

        if (before.PrintLogo != after.PrintLogo)
        {
            parts.Add(after.PrintLogo ? "logo on" : "logo off");
        }

        if (!string.Equals(before.DocumentPrinterName, Trim(after.DocumentPrinterName), StringComparison.Ordinal)
            || before.DocumentPaper != after.DocumentPaper)
        {
            parts.Add(
                $"documents set to {Trim(after.DocumentPrinterName) ?? "(none)"} "
                + $"on {after.DocumentPaper}");
        }

        if (before.PrescriptionCopies != after.PrescriptionCopies)
        {
            parts.Add($"prescription copies set to {after.PrescriptionCopies}");
        }

        if (before.PrintPrescriberDetails != after.PrintPrescriberDetails)
        {
            parts.Add(after.PrintPrescriberDetails
                ? "prescriber details printed on scripts"
                : "prescriber details left off scripts");
        }

        return parts.Count == 0
            ? "Saved printer settings with no changes"
            : "Printer settings — " + string.Join("; ", parts);
    }

    private async Task RecordAsync(Guid settingsId, string detail, CancellationToken ct)
    {
        var providerId = _session.ProviderId;

        var actor = providerId is { } id
            ? await _providers.GetByIdAsync(id, ct).ConfigureAwait(false)
            : null;

        await _audit
            .SaveAsync(
                new AuditEntry
                {
                    Action = AuditAction.Updated,
                    EntityName = nameof(PrinterSettings),
                    EntityId = settingsId,
                    ProviderId = providerId,
                    ProviderName = actor?.FullName ?? _session.UserDisplayName,
                    OccurredUtc = _clock.UtcNow,
                    Detail = detail,
                },
                ct)
            .ConfigureAwait(false);
    }
}

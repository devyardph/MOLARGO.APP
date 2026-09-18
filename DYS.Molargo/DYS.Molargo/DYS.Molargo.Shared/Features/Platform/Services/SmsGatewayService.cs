using System.Text.Json;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Platform.Services;

/// <summary>One country's SMS gateway, with the key masked for the screen.</summary>
/// <param name="HasKey">
/// Whether a key is stored. The value never leaves the service — see
/// <see cref="ISmsGatewayService"/>.
/// </param>
public sealed record SmsGatewayRow(
    Guid GatewayId,
    string CountryCode,
    string ProviderName,
    string ApiUrl,
    string? SenderId,
    decimal PricePerMessage,
    string CurrencyCode,

    // The template and the headers come back, unlike the key. They are the provider's
    // documented request shape, not a secret — and they have to be editable, which means
    // they have to be readable.
    string PayloadTemplate,
    string Headers,
    string ContentType,
    bool HasKey,
    bool IsActive,
    DateTime? LastTestUtc,
    string? LastTestResult,
    bool LastTestSucceeded)
{
    /// <summary>True where this country could actually send.</summary>
    /// <remarks>
    /// The template counts as much as the key. A gateway with credentials and no body posts
    /// an empty request, which the provider refuses — and the country looks configured on
    /// the list right up until a reminder fails to arrive.
    /// </remarks>
    public bool IsUsable =>
        IsActive && HasKey && ApiUrl.Length > 0 && PayloadTemplate.Length > 0;
}

/// <summary>
/// The vendor's SMS providers, one per country.
/// </summary>
/// <remarks>
/// <para>
/// Vendor-only, behind the same <see cref="PlatformGuard"/> check as plans and clinics, and
/// returning nothing to anybody else. A clinic cannot read these: the key sends for every
/// practice in the country, so one practice holding it could send as all of them.
/// </para>
/// <para>
/// The key is write-only through this contract. It goes in on a save and never comes back
/// — <see cref="SmsGatewayRow"/> reports only whether one is stored. A key rendered into a
/// page is a key in the browser's memory, in a view-source, and in anything that screenshots
/// the screen, and there is no reason for it ever to be on screen once it is set.
/// </para>
/// </remarks>
public interface ISmsGatewayService
{
    /// <summary>Every configured country, active and not.</summary>
    Task<IReadOnlyList<SmsGatewayRow>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// The countries the vendor sells in, as the picker's universe.
    /// </summary>
    /// <remarks>
    /// Taken from the plans, the same way the plan screen takes it. A country with no plan
    /// has no clinics to text, and offering the full ISO list would invite a gateway that
    /// matches nothing — a row that looks configured and silently serves nobody.
    /// </remarks>
    Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds or updates one country's gateway. Null on success, or the refusal.
    /// </summary>
    /// <param name="apiKey">
    /// Left null to keep the stored key. Blank is not "clear it" — a save that wiped the
    /// credential because a masked box was not retyped is a country that silently stops
    /// sending.
    /// </param>
    Task<string?> SaveAsync(
        Guid gatewayId,
        string countryCode,
        string providerName,
        string apiUrl,
        string? senderId,
        decimal pricePerMessage,
        string currencyCode,
        string payloadTemplate,
        string? headers,
        string contentType,
        string? apiKey,
        CancellationToken ct = default);

    /// <summary>Switches a country's gateway on or off.</summary>
    Task<string?> SetActiveAsync(Guid gatewayId, bool isActive, CancellationToken ct = default);

    /// <summary>Removes a country's gateway and its key.</summary>
    Task<string?> DeleteAsync(Guid gatewayId, CancellationToken ct = default);
}

/// <inheritdoc cref="ISmsGatewayService"/>
public sealed class SmsGatewayService : ISmsGatewayService
{
    private readonly MolargoDatabase _database;
    private readonly ISessionService _session;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public SmsGatewayService(
        MolargoDatabase database,
        ISessionService session,
        ITenantContext tenant,
        IClock clock)
    {
        _database = database;
        _session = session;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<IReadOnlyList<SmsGatewayRow>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return [];

        // The filter is bypassed on purpose. These rows are the vendor's and carry the
        // vendor's tenant id, and this screen is only ever reached by the vendor — but the
        // ambient filter follows whichever clinic the operator is currently looking at.
        var rows = await db.SmsGateways
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(gateway => !gateway.IsDeleted)
            .OrderBy(gateway => gateway.CountryCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Select(gateway => new SmsGatewayRow(
                gateway.Id,
                gateway.CountryCode,
                gateway.ProviderName,
                gateway.ApiUrl,
                gateway.SenderId,
                gateway.PricePerMessage,
                gateway.CurrencyCode,
                gateway.PayloadTemplate,
                gateway.Headers,
                gateway.ContentType,

                // Whether, never what.
                !string.IsNullOrWhiteSpace(gateway.ApiKey),
                gateway.IsActive,
                gateway.LastTestUtc,
                gateway.LastTestResult,
                gateway.LastTestSucceeded))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false)) return [];

        return await db.Plans
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(plan => !plan.IsDeleted)
            .Select(plan => plan.CountryCode)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<string?> SaveAsync(
        Guid gatewayId,
        string countryCode,
        string providerName,
        string apiUrl,
        string? senderId,
        decimal pricePerMessage,
        string currencyCode,
        string payloadTemplate,
        string? headers,
        string contentType,
        string? apiKey,
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        var country = (countryCode ?? string.Empty).Trim().ToUpperInvariant();

        if (country.Length != 2 || !country.All(char.IsAsciiLetterUpper))
        {
            return "The country has to be a two-letter code — AU, NZ, PH.";
        }

        var provider = (providerName ?? string.Empty).Trim();

        if (provider.Length == 0) return "Name the provider, so a bill can be traced to it.";

        var url = (apiUrl ?? string.Empty).Trim();

        // Parsed rather than pattern-matched, and https only. A key posted over http is a
        // key read by anything between here and the provider.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            return "The API URL has to be an absolute https address — "
                + "https://api.example.com/v1/messages.";
        }

        if (pricePerMessage < 0m)
        {
            return "A price cannot be negative. Zero is allowed — it means messages are "
                + "included in the plan for that country.";
        }

        var currency = (currencyCode ?? string.Empty).Trim().ToUpperInvariant();

        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
        {
            return "The currency has to be a three-letter code — AUD, PHP, GBP.";
        }

        var type = (contentType ?? string.Empty).Trim().ToLowerInvariant();

        if (type != SmsPayload.Json && type != SmsPayload.Form)
        {
            return "The content type has to be either " + SmsPayload.Json
                + " or " + SmsPayload.Form + ".";
        }

        var template = (payloadTemplate ?? string.Empty).Trim();

        if (template.Length == 0)
        {
            return "The payload needs a body template — whatever the provider's "
                + "documentation says to post. Use {{To}} and {{Message}} for the parts "
                + "that change per message.";
        }

        // Both tokens, checked here rather than discovered at send time. A template missing
        // {{Message}} posts successfully and delivers nothing — the provider returns a 200
        // for a request that asked it to send an empty text, so the failure never surfaces
        // as a failure anywhere.
        foreach (var required in new[] { "{{To}}", "{{Message}}" })
        {
            if (!template.Contains(required, StringComparison.Ordinal))
            {
                return $"The payload has no {required} in it, so the message would go out "
                    + "without that. Add it where the provider's documentation puts it.";
            }
        }

        var headerLines = (headers ?? string.Empty).Trim();

        foreach (var line in SmsPayload.ReadLines(headerLines))
        {
            if (line.IndexOf(':', StringComparison.Ordinal) <= 0)
            {
                return $"\"{line}\" is not a header. One per line, written as "
                    + "Name: Value — Authorization: Bearer {{ApiKey}}.";
            }
        }

        // The template rendered against a sample, then parsed. Checked with real-looking
        // values rather than as raw text, because the text before substitution is not valid
        // JSON at all and the text after it is what actually gets posted. A template that
        // is one comma short fails here, at a screen with somebody looking at it, rather
        // than on a reminder at seven in the morning.
        if (type == SmsPayload.Json)
        {
            var sample = SmsPayload.Render(
                url, type, headerLines, template,
                "+61400000000", "Test message", senderId, "sample-key");

            try
            {
                using var _ = JsonDocument.Parse(sample.Body);
            }
            catch (JsonException error)
            {
                return "The payload is not valid JSON once the tokens are filled in: "
                    + error.Message
                    + " Check the preview underneath — that is what would be posted.";
            }
        }

        var existing = await db.SmsGateways
            .IgnoreQueryFilters()
            .Where(gateway => !gateway.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var row = gatewayId == Guid.Empty
            ? null
            : existing.FirstOrDefault(gateway => gateway.Id == gatewayId);

        if (existing.Any(gateway => gateway.Id != (row?.Id ?? Guid.Empty)
            && string.Equals(gateway.CountryCode, country, StringComparison.Ordinal)))
        {
            return $"{country} already has a gateway. Edit that one, or switch it off first.";
        }

        var key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

        if (row is null && key is null)
        {
            return "A new gateway needs its API key.";
        }

        var now = _clock.UtcNow;
        var isNew = row is null;

        row ??= new SmsGateway
        {
            Id = Guid.NewGuid(),

            // The vendor's own tenant, like a plan. These are not any clinic's rows.
            TenantId = _tenant.TenantId,
            CreatedUtc = now,
        };

        row.CountryCode = country;
        row.ProviderName = provider;
        row.ApiUrl = url;
        row.SenderId = string.IsNullOrWhiteSpace(senderId) ? null : senderId.Trim();

        // Rounded to four places, not two. A per-message rate is fractions of a cent in
        // most markets, and rounding to currency here would price every message at zero.
        row.PricePerMessage = Math.Round(pricePerMessage, 4, MidpointRounding.AwayFromZero);
        row.CurrencyCode = currency;
        row.PayloadTemplate = template;
        row.Headers = headerLines;
        row.ContentType = type;
        row.UpdatedUtc = now;

        // Only when one was typed. A blank box means "leave it", not "clear it" — see the
        // contract. Clearing is done by deleting the gateway.
        if (key is not null)
        {
            row.ApiKey = key;

            // The stored result described the old key. Kept, and it would read as a pass
            // for a credential that has just been replaced.
            row.LastTestUtc = null;
            row.LastTestResult = null;
            row.LastTestSucceeded = false;
        }

        if (isNew) db.SmsGateways.Add(row);

        await AuditAsync(db, row.Id,
            isNew ? AuditAction.Created : AuditAction.Updated,
            (isNew ? "Added" : "Changed")
                + $" the {country} SMS gateway ({provider})"
                + $" at {row.PricePerMessage:0.####} {currency} per message"
                + (key is null ? string.Empty : ", with a new API key"),
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> SetActiveAsync(
        Guid gatewayId, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        var row = await db.SmsGateways
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(gateway => gateway.Id == gatewayId && !gateway.IsDeleted, ct)
            .ConfigureAwait(false);

        if (row is null) return "That gateway no longer exists.";

        if (row.IsActive == isActive) return null;

        row.IsActive = isActive;
        row.UpdatedUtc = _clock.UtcNow;

        await AuditAsync(db, gatewayId, AuditAction.Updated,
            (isActive ? "Switched on" : "Switched off")
                + $" the {row.CountryCode} SMS gateway ({row.ProviderName})",
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<string?> DeleteAsync(Guid gatewayId, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return PlatformGuard.NotPermitted;
        }

        var row = await db.SmsGateways
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(gateway => gateway.Id == gatewayId && !gateway.IsDeleted, ct)
            .ConfigureAwait(false);

        if (row is null) return null;

        var country = row.CountryCode;
        var provider = row.ProviderName;

        row.IsDeleted = true;
        row.DeletedUtc = _clock.UtcNow;
        row.UpdatedUtc = _clock.UtcNow;

        // The key goes with it, not just the row. A soft-deleted row still holding a live
        // credential is the credential still sitting in the database file after somebody
        // believed they had removed it.
        row.ApiKey = string.Empty;

        await AuditAsync(db, gatewayId, AuditAction.Deleted,
            $"Removed the {country} SMS gateway ({provider}) and its key", ct)
            .ConfigureAwait(false);

        return null;
    }

    // ---- helpers ---------------------------------------------------------

    private Task<bool> IsSuperAdminAsync(MolargoDbContext db, CancellationToken ct) =>
        PlatformGuard.IsSuperAdminAsync(db, _session.ProviderId, ct);

    /// <summary>Writes the entry into the vendor's own tenant.</summary>
    /// <remarks>
    /// The vendor's commercial arrangement, like a plan — not a clinic's record, even
    /// though it decides whether that clinic's texts go out.
    /// </remarks>
    private async Task AuditAsync(
        MolargoDbContext db,
        Guid gatewayId,
        AuditAction action,
        string detail,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Action = action,
            EntityName = nameof(SmsGateway),
            EntityId = gatewayId,
            ProviderId = _session.ProviderId,
            ProviderName = _session.UserDisplayName,
            OccurredUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now,
            DeviceId = await _database.GetDeviceIdAsync(ct).ConfigureAwait(false),
            Detail = detail,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

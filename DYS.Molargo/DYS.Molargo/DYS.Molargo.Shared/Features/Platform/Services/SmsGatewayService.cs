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

    // The scheme and its visible fields come back; the secret does not. A username is not
    // a credential — it is an account id or an email, and hiding it would mean nobody could
    // check which account a country sends under without retyping the password.
    SmsAuthType AuthType,
    string AuthHeaderName,
    string AuthUsername,
    bool Base64EncodeApiKey,
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
        IsActive && ApiUrl.Length > 0 && PayloadTemplate.Length > 0 && IsAuthComplete;

    /// <summary>True where the chosen scheme has everything it needs.</summary>
    /// <remarks>
    /// Which fields those are differs, and so does whether the secret is among them. Basic
    /// needs the username and may have no password at all — providers that authenticate on
    /// a key alone document it as the username with nothing after the colon. An API key
    /// needs both its header and its value.
    /// </remarks>
    public bool IsAuthComplete => AuthType switch
    {
        SmsAuthType.BasicAuth => AuthUsername.Length > 0,
        _ => AuthHeaderName.Length > 0 && HasKey,
    };

    /// <summary>What the list says is missing, for a row that cannot send.</summary>
    public string MissingLabel => AuthType switch
    {
        SmsAuthType.BasicAuth => "No username",
        _ => "No key",
    };
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
        SmsAuthType authType,
        string? authHeaderName,
        string? authUsername,
        bool base64EncodeApiKey,
        string? apiKey,
        CancellationToken ct = default);

    /// <summary>
    /// Sends one real text through a stored gateway, and records what came back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A real send, not a dry run. The failures worth catching here are the ones only the
    /// provider can report — a key that was revoked, a sender id never registered, a spent
    /// balance, a template the documentation describes differently from how it behaves —
    /// and none of those are visible from anything short of asking it to carry a message.
    /// It costs whatever the country's messages cost, on the vendor's account.
    /// </para>
    /// <para>
    /// Reads the stored row rather than anything on the screen, because the key only exists
    /// there — and because a test that passed against unsaved edits would be a test of
    /// something no clinic will ever send through.
    /// </para>
    /// </remarks>
    /// <param name="toNumber">
    /// Where to send it. Interpreted against the gateway's own country, so a local number
    /// works and an international one is taken as given — see <see cref="PhoneNumber"/>.
    /// </param>
    Task<SmsResult> TestAsync(
        Guid gatewayId, string? toNumber, CancellationToken ct = default);

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
    private readonly ISmsSender _sms;

    public SmsGatewayService(
        MolargoDatabase database,
        ISessionService session,
        ITenantContext tenant,
        IClock clock,
        ISmsSender sms)
    {
        _database = database;
        _session = session;
        _tenant = tenant;
        _clock = clock;
        _sms = sms;
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
                gateway.AuthType,
                gateway.AuthHeaderName,
                gateway.AuthUsername,
                gateway.Base64EncodeApiKey,

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
        SmsAuthType authType,
        string? authHeaderName,
        string? authUsername,
        bool base64EncodeApiKey,
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

        // The auth fields the chosen scheme actually reads, trimmed and checked per type.
        // The other one is cleared rather than kept: a username left behind from a scheme
        // somebody switched away from is a value the screen would show under a heading that
        // does not use it.
        var headerName = authType == SmsAuthType.BasicAuth
            ? SmsAuth.AuthorizationHeader
            : (authHeaderName ?? string.Empty).Trim();

        var username = authType == SmsAuthType.BasicAuth
            ? (authUsername ?? string.Empty).Trim()
            : string.Empty;

        if (authType == SmsAuthType.BasicAuth)
        {
            if (username.Length == 0)
            {
                return "Basic auth needs the username as well as the password. Without one "
                    + "the credential encodes to an empty account, which a provider answers "
                    + "with a 401 that reads as a wrong password.";
            }

            if (username.Contains(':', StringComparison.Ordinal))
            {
                // The separator itself. A colon in the username splits the pair somewhere
                // the provider does not expect, and the account it reads is a prefix of the
                // real one — refused with no indication of why.
                return "A Basic auth username cannot contain a colon — that is the "
                    + "character separating it from the password.";
            }
        }
        else
        {
            if (headerName.Length == 0)
            {
                return "An API key needs the header name to send it in — X-API-Key, or "
                    + "Authorization.";
            }

            if (!IsHeaderName(headerName))
            {
                // Checked here because the failure downstream is silent: a name with a
                // space in it is refused by HttpClient at send time and the request goes
                // out without the credential.
                return $"\"{headerName}\" is not a usable header name. A name cannot "
                    + "contain spaces, colons or punctuation beyond - and _.";
            }
        }

        var headerLines = (headers ?? string.Empty).Trim();

        foreach (var line in SmsPayload.ReadLines(headerLines))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0)
            {
                return $"\"{line}\" is not a header. One per line, written as "
                    + "Name: Value — Accept: application/json.";
            }

            var typedName = line[..colon].Trim();

            if (!IsHeaderName(typedName))
            {
                return $"\"{typedName}\" is not a usable header name. A name cannot contain "
                    + "spaces or punctuation beyond - and _, and one that is not usable is "
                    + "dropped at send time without anything being reported.";
            }

            // The auth header is built from the fields above, so a line repeating it would
            // be a second value for the same name — which goes out comma-joined and is
            // refused by the provider as a malformed credential.
            if (string.Equals(typedName, headerName, StringComparison.OrdinalIgnoreCase))
            {
                return $"\"{typedName}\" is already sent by the auth type above. Remove the "
                    + "line, or change the header the key is sent in.";
            }

            if (string.Equals(typedName, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                return "Content-Type is set by the content type box above, not a header "
                    + "line. Two of them go out joined by a comma and are refused.";
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
                "+61400000000", "Test message", senderId,
                new SmsAuth(authType, headerName, username, "sample-key", base64EncodeApiKey));

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

        // Required for an API key, optional for Basic. A key with no value is a header with
        // nothing in it; a Basic credential with no password is what providers that
        // authenticate on the key alone actually document — the key as the username and
        // nothing after the colon.
        if (row is null && key is null && authType == SmsAuthType.ApiKey)
        {
            return "A new gateway needs its API key.";
        }

        // Changing the scheme is the one edit where a blank box cannot mean "keep it": an
        // API key is not a Basic password, and carrying one over would send the wrong
        // credential under a scheme that looks correctly filled in. Refused where the new
        // scheme needs a secret, and cleared where it does not — never reused.
        var clearSecret = row is not null && row.AuthType != authType && key is null;

        if (clearSecret && authType == SmsAuthType.ApiKey)
        {
            return "Switching to an API key needs the key. The stored Basic auth password "
                + "is not the same credential.";
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
        row.AuthType = authType;
        row.AuthHeaderName = headerName;
        row.AuthUsername = username;
        row.Base64EncodeApiKey = authType == SmsAuthType.ApiKey && base64EncodeApiKey;
        row.UpdatedUtc = now;

        // When one was typed, or when the scheme changed under a stored one. A blank box
        // otherwise means "leave it", not "clear it" — see the contract.
        if (key is not null || clearSecret)
        {
            row.ApiKey = key ?? string.Empty;

            // The stored result described the old credential. Kept, and it would read as a
            // pass for one that has just been replaced.
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
                + (authType == SmsAuthType.BasicAuth
                    ? $", Basic auth as {username}"

                        // Recorded, because a credential with no password is a thing
                        // somebody will later want to know was deliberate.
                        + (row.ApiKey.Length == 0 ? " with no password" : string.Empty)
                    : $", API key in {headerName}"
                        + (row.Base64EncodeApiKey ? " (Base64-encoded)" : string.Empty))
                + (key is null ? string.Empty : ", with a new credential"),
            ct)
            .ConfigureAwait(false);

        return null;
    }

    public async Task<SmsResult> TestAsync(
        Guid gatewayId, string? toNumber, CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        if (!await IsSuperAdminAsync(db, ct).ConfigureAwait(false))
        {
            return SmsResult.Failed(PlatformGuard.NotPermitted);
        }

        var row = await db.SmsGateways
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(gateway => gateway.Id == gatewayId && !gateway.IsDeleted, ct)
            .ConfigureAwait(false);

        if (row is null) return SmsResult.Failed("That gateway no longer exists.");

        // Not IsActive. A switched-off gateway is exactly the one somebody wants to test
        // before switching it back on, and refusing that would mean the only way to check a
        // repair is to put it back into service for every practice in the country first.
        if (!row.IsConfigured)
        {
            return SmsResult.Failed(
                "This gateway is not finished — it needs a URL, a credential and a body "
                    + "template before it can send anything.");
        }

        // Checked here as well as in the sender, only for the wording. The shared message
        // says the record has no mobile on it, which is true of a patient and meaningless
        // of a box somebody has just typed into. Returned before the row is stamped: this
        // never reached the provider, so it is not a test result.
        if (!PhoneNumber.ToInternational(toNumber, row.CountryCode).Ok)
        {
            return SmsResult.Failed(
                $"\"{toNumber}\" is not a number this gateway can dial. Type a "
                    + $"{row.CountryCode} mobile, or an international number starting "
                    + "with +.");
        }

        var credentials = new SmsCredentials(
            row.CountryCode,
            row.ProviderName,
            row.ApiUrl,
            row.SenderId,
            row.PricePerMessage,
            row.CurrencyCode,
            row.PayloadTemplate,
            row.Headers,
            row.ContentType,
            row.Auth);

        var result = await _sms
            .SendViaAsync(credentials, toNumber, TestMessage(row.CountryCode), ct,
                purpose: $"{row.CountryCode} gateway test")
            .ConfigureAwait(false);

        var now = _clock.UtcNow;

        row.LastTestUtc = now;
        row.LastTestSucceeded = result.Succeeded;

        // The provider's own words, and the number they were about. Kept verbatim for the
        // same reason the comms log keeps them: a carrier names the cause exactly, and a
        // paraphrase loses the only part somebody can act on.
        row.LastTestResult = result.Number is { Length: > 0 }
            ? $"{result.Number} — {result.Detail}"
            : result.Detail;

        row.UpdatedUtc = now;

        await AuditAsync(db, gatewayId, AuditAction.Updated,
            $"Tested the {row.CountryCode} SMS gateway ({row.ProviderName}) — "
                + (result.Succeeded ? "sent" : "failed")
                + $" to {result.Number ?? "an unusable number"}: {result.Detail}",
            ct)
            .ConfigureAwait(false);

        return result;
    }

    /// <summary>What a test send actually says.</summary>
    /// <remarks>
    /// Names the app and the country, because it arrives on somebody's phone with no
    /// context — and a test message that reads like a real one is the sort of thing that
    /// gets forwarded to a practice manager as a fault.
    /// </remarks>
    private static string TestMessage(string country) =>
        $"Molargo test message. The {country} SMS gateway is working. "
            + "No action is needed.";

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

    /// <summary>True where a string can be sent as an HTTP header name.</summary>
    /// <remarks>
    /// The RFC's token characters. Narrower than the wire allows in one respect — it refuses
    /// an empty name — and exactly as wide in the rest, because HttpClient applies the same
    /// rule at send time and refuses anything outside it. Checked at the screen because its
    /// refusal downstream is silent: the header is dropped and the request goes out without
    /// it.
    /// </remarks>
    private static bool IsHeaderName(string name) =>
        name.Length > 0
        && name.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || "!#$%&'*+-.^_`|~".Contains(character, StringComparison.Ordinal));

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

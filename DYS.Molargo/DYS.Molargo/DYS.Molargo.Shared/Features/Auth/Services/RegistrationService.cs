using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Shared.Features.Auth.Services;

/// <summary>
/// How big the practice says it is, which is what sizes the plan.
/// </summary>
/// <remarks>
/// Asked as a range rather than a number because the answer is a self-description, not a
/// count — a practice signing up does not yet have its locations in the app. It picks the
/// plan; the actual site count is what later prices it.
/// </remarks>
public enum PracticeSize
{
    Single = 0,
    SmallGroup = 1,
    LargeGroup = 2,
}

/// <summary>Everything the three-step signup collects.</summary>
public sealed record PracticeSignup(
    string PracticeName,
    PracticeSize Size,
    string CountryCode,
    string FullName,
    string Email,
    ProviderRole Role,
    string Password,
    bool AcceptedTerms);

/// <summary>
/// What a signup produced — or why it was refused.
/// </summary>
/// <param name="ClinicCode">
/// The code their staff type at sign-in. Handed back because it is derived from the
/// practice name rather than chosen, so the person who just signed up has no other way to
/// learn it.
/// </param>
/// <param name="Username">Likewise derived, and likewise unguessable if not shown.</param>
public sealed record PracticeCreated(
    bool Succeeded,
    string? Refusal,
    string? ClinicCode,
    string? Username,
    string? PlanName,
    DateOnly? TrialEndsOn)
{
    public static PracticeCreated Refused(string refusal) =>
        new(false, refusal, null, null, null, null);
}

/// <summary>
/// Self-service practice signup: creates a tenant, its first site and chair, and the
/// person who signed up.
/// </summary>
/// <remarks>
/// <para>
/// Runs for somebody who is not signed in and has no tenant, which makes it the second
/// place in the app that writes outside the tenant filter — the first being
/// <c>AuthService</c>, which reads a clinic by its code to work out what the filter should
/// be. Inserts are not filtered, so every row is stamped with the new tenant by hand.
/// </para>
/// <para>
/// It creates a working practice, not just a row: a site named after the practice and one
/// chair in it. Without those the diary has no columns and the new clinic cannot take a
/// booking — which is the state a brand new tenant would otherwise open in, and it reads
/// as the app being broken rather than as setup not being finished.
/// </para>
/// </remarks>
public interface IRegistrationService
{
    /// <summary>The countries a practice can sign up in — those with plans on sale.</summary>
    Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default);

    Task<PracticeCreated> CreatePracticeAsync(
        PracticeSignup signup, CancellationToken ct = default);
}

/// <inheritdoc cref="IRegistrationService"/>
public sealed class RegistrationService : IRegistrationService
{
    /// <summary>
    /// Days of free trial. Thirty, as the signup screen promises.
    /// </summary>
    /// <remarks>
    /// A constant rather than a per-plan field: it is a single commercial promise made on
    /// one screen, and putting it on the plan would invite four countries to drift apart on
    /// the one number the marketing quotes.
    /// </remarks>
    public const int TrialDays = 30;

    /// <summary>The shortest password a practice owner may set.</summary>
    /// <remarks>
    /// Twelve, matching the vendor's own operators and the design's "12+ characters". This
    /// account can read every patient the practice ever sees.
    /// </remarks>
    private const int MinimumPasswordLength = 12;

    private readonly MolargoDatabase _database;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;

    public RegistrationService(
        MolargoDatabase database, IPasswordHasher hasher, IClock clock)
    {
        _database = database;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<IReadOnlyList<string>> GetCountriesAsync(
        CancellationToken ct = default)
    {
        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        // Only where a price exists. Offering a country with no plan would take a practice
        // through three steps and then have nothing to put them on.
        return await db.Plans
            .AsNoTracking()
            .Where(plan => plan.IsActive && !plan.IsDeleted)
            .Select(plan => plan.CountryCode)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<PracticeCreated> CreatePracticeAsync(
        PracticeSignup signup, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(signup.PracticeName))
        {
            return PracticeCreated.Refused("Enter the practice name.");
        }

        if (string.IsNullOrWhiteSpace(signup.FullName))
        {
            return PracticeCreated.Refused("Enter your name.");
        }

        if (!LooksLikeEmail(signup.Email))
        {
            return PracticeCreated.Refused("That email address does not look right.");
        }

        if (string.IsNullOrWhiteSpace(signup.Password)
            || signup.Password.Length < MinimumPasswordLength)
        {
            return PracticeCreated.Refused(
                $"Use at least {MinimumPasswordLength} characters. This account can read "
                    + "every patient record the practice holds.");
        }

        if (!signup.AcceptedTerms)
        {
            return PracticeCreated.Refused(
                "Accept the terms to create the practice.");
        }

        var country = (signup.CountryCode ?? string.Empty).Trim().ToUpperInvariant();

        if (country.Length != 2)
        {
            return PracticeCreated.Refused("Choose a country.");
        }

        await using var db = await _database.CreateContextAsync(ct).ConfigureAwait(false);

        var email = signup.Email.Trim();

        // One practice per email address.
        //
        // This is the guard against the commonest accident on this form: a slow first
        // submit, a second press, and a practice that now has two clinic codes, two owner
        // accounts and two trials — with no way for the person to tell which one their
        // staff should be signing into.
        //
        // Checked against tenants' contact addresses rather than against every provider on
        // the platform. A dentist who works at one practice and opens another is doing
        // something legitimate, and refusing them would be the wrong rule; two practices
        // billed to one address, created minutes apart, is the mistake worth catching.
        var already = await db.Tenants
            .AsNoTracking()
            .AnyAsync(
                tenant => tenant.ContactEmail != null
                    && !tenant.IsDeleted
                    && EF.Functions.Like(tenant.ContactEmail, email),
                ct)
            .ConfigureAwait(false);

        if (already)
        {
            return PracticeCreated.Refused(
                $"A practice is already registered to {email}. Sign in with your clinic "
                    + "code instead, or use a different address if this is a second "
                    + "practice. Nothing has been created.");
        }

        var plan = await ChoosePlanAsync(db, country, signup.Size, ct).ConfigureAwait(false);

        if (plan is null)
        {
            return PracticeCreated.Refused(
                $"There is no plan on sale for {country} yet. Nothing has been created.");
        }

        var name = signup.PracticeName.Trim();
        var code = await UniqueCodeAsync(db, name, ct).ConfigureAwait(false);

        var now = _clock.UtcNow;
        var today = _clock.Today;
        var tenantId = Guid.NewGuid();

        var tenant = new Tenant
        {
            Id = tenantId,
            TenantId = tenantId,
            Name = name,
            Slug = code,
            ContactEmail = email,
            CountryCode = country,
            PlanId = plan.Id,
            SubscribedOn = today,

            // Inclusive of today, so "30 days" is thirty days of use rather than
            // twenty-nine and a bit.
            TrialEndsOn = today.AddDays(TrialDays - 1),
            TermsAcceptedUtc = now,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        var location = new PracticeLocation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            ShortName = ShortName(name),
            IsActive = true,
            DisplayOrder = 0,

            // The practice's own zone is not knowable from a country code alone — a
            // country can hold several. Seeded with the commonest for the country and
            // editable in Admin, rather than guessed silently from the machine's clock.
            TimeZoneId = DefaultTimeZone(country),
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        var chair = new Operatory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PracticeLocationId = location.Id,
            Name = "Chair 1",
            DisplayOrder = 0,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        var (first, last) = SplitName(signup.FullName.Trim());
        var username = await UniqueUsernameAsync(db, first, last, ct).ConfigureAwait(false);

        var owner = new Provider
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FirstName = first,
            LastName = last,
            DisplayName = signup.FullName.Trim(),

            // Never SuperAdmin, whatever arrives. That role belongs to the vendor's own
            // tenant, and a signup form is the last place it should be reachable from.
            Role = ProviderRoles.IsPlatform(signup.Role) ? ProviderRole.Dentist : signup.Role,

            // The owner of the practice, whichever clinical role they picked. Two separate
            // facts about one person: an owner who is also a dentist keeps the dentist's
            // diary column, clinical authorship and provider number, and gains the run of
            // the practice's settings on top.
            //
            // Nobody else exists yet to be the owner, so this is not a choice the form
            // needs to offer. Permissions stay None, which is correct and unused — an
            // owner holds everything implicitly, so storing a full set here would be a
            // second answer to the same question.
            IsOwner = true,
            Email = email,
            PrimaryLocationId = location.Id,
            Username = username,
            PasswordHash = _hasher.Hash(signup.Password),
            PasswordUpdatedUtc = now,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        db.Tenants.Add(tenant);
        db.PracticeLocations.Add(location);
        db.Operatories.Add(chair);
        db.Providers.Add(owner);

        // Into the new clinic's own log, which is where they will look for it, and the
        // first entry their audit trail ever has.
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Action = AuditAction.Created,
            EntityName = nameof(Tenant),
            EntityId = tenantId,
            ProviderId = owner.Id,
            ProviderName = owner.FullName,
            OccurredUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now,
            DeviceId = await _database.GetDeviceIdAsync(ct).ConfigureAwait(false),
            Detail = $"Practice created on the {plan.Name} plan with a {TrialDays}-day "
                + $"trial to {tenant.TrialEndsOn:d MMM yyyy}. Terms accepted by "
                + owner.FullName,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new PracticeCreated(
            true, null, code, username, plan.Name, tenant.TrialEndsOn);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// The plan that matches what the practice said about its size.
    /// </summary>
    /// <remarks>
    /// By code, with a fallback to the cheapest plan on sale. The codes are the vendor's
    /// and could be renamed, and a signup that dead-ended because somebody renamed
    /// "practice" would be the worst possible way to find that out.
    /// </remarks>
    private static async Task<Plan?> ChoosePlanAsync(
        MolargoDbContext db, string country, PracticeSize size, CancellationToken ct)
    {
        var onSale = await db.Plans
            .AsNoTracking()
            .Where(plan => plan.CountryCode == country && plan.IsActive && !plan.IsDeleted)
            .OrderBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.MonthlyBase)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (onSale.Count == 0) return null;

        var wanted = size switch
        {
            PracticeSize.Single => "solo",
            PracticeSize.SmallGroup => "practice",
            _ => "group",
        };

        return onSale.FirstOrDefault(plan =>
            string.Equals(plan.Code, wanted, StringComparison.OrdinalIgnoreCase))
            ?? onSale[0];
    }

    /// <summary>
    /// A clinic code from the practice name, made unique.
    /// </summary>
    /// <remarks>
    /// Derived rather than asked for, because a practice signing up has no idea it needs
    /// one — and it is the thing their whole staff will type every morning. A collision
    /// gets a number rather than a refusal: two practices legitimately share a name, and
    /// neither should be turned away at signup over it.
    /// </remarks>
    private static async Task<string> UniqueCodeAsync(
        MolargoDbContext db, string name, CancellationToken ct)
    {
        var taken = await db.Tenants
            .AsNoTracking()
            .Select(tenant => tenant.Slug)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = Slugify(name);

        if (stem.Length < 3) stem = $"clinic-{stem}".TrimEnd('-');

        var candidate = stem;

        for (var suffix = 2; used.Contains(candidate); suffix++)
        {
            candidate = $"{stem}-{suffix}";
        }

        return candidate;
    }

    /// <summary>First initial and surname, made unique within the new clinic.</summary>
    /// <remarks>
    /// The same convention the seeded practice uses, so the shape of a username is
    /// consistent whether a clinic was seeded or signed up. Unique only within the tenant —
    /// two practices can each have a "jcitizen", because sign-in looks it up inside one
    /// clinic code.
    /// </remarks>
    private static async Task<string> UniqueUsernameAsync(
        MolargoDbContext db, string first, string last, CancellationToken ct)
    {
        // A brand new tenant has nobody in it, so nothing can collide — but this runs
        // before the tenant exists at all, and reading the whole provider table with the
        // filter bypassed is the only way to be sure. Cheap, and once per signup.
        var stem = Slugify($"{(first.Length > 0 ? first[..1] : "u")}{last}")
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (stem.Length < 3) stem = "owner";

        var taken = await db.Providers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(provider => provider.Username != null)
            .Select(provider => provider.Username!)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = stem;

        for (var suffix = 2; used.Contains(candidate); suffix++)
        {
            candidate = $"{stem}{suffix}";
        }

        return candidate;
    }

    /// <summary>Lowercase, hyphenated, ASCII letters and digits only.</summary>
    private static string Slugify(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var lastWasHyphen = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && builder.Length > 0)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>
    /// "Apex Dental Newtown" to "Newtown" — the last word, where there is more than one.
    /// </summary>
    /// <remarks>
    /// The app bar and every site picker show the short name, and a practice's full trading
    /// name is too long for a chip. The last word is usually the suburb, which is exactly
    /// what distinguishes one site from another.
    /// </remarks>
    private static string? ShortName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length > 1 ? words[^1] : null;
    }

    private static (string First, string Last) SplitName(string fullName)
    {
        var words = fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)

            // Titles are not names. "Dr Jane Citizen" should become Jane Citizen, or the
            // clinic's staff list reads "Dr Dr Citizen" everywhere it prefixes one.
            .Where(word => word is not ("Dr" or "Dr." or "Mr" or "Mrs" or "Ms" or "Prof"))
            .ToArray();

        return words.Length switch
        {
            0 => ("Practice", "Owner"),
            1 => (words[0], "—"),
            _ => (string.Join(' ', words[..^1]), words[^1]),
        };
    }

    /// <summary>
    /// The commonest zone for a country, as a starting point.
    /// </summary>
    /// <remarks>
    /// A guess, and flagged as one on the completion screen. Several of these countries
    /// span more than one zone, and getting it wrong shifts every appointment time — so it
    /// is a default to correct in Admin, not an answer.
    /// </remarks>
    private static string DefaultTimeZone(string country) => country switch
    {
        "AU" => "Australia/Sydney",
        "NZ" => "Pacific/Auckland",
        "GB" => "Europe/London",
        "PH" => "Asia/Manila",
        _ => "UTC",
    };

    /// <summary>
    /// Something before an @, something after, and a dot.
    /// </summary>
    /// <remarks>
    /// Deliberately shallow, like the notification settings' check. A stricter rule rejects
    /// valid addresses while still not proving the mailbox exists.
    /// </remarks>
    private static bool LooksLikeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var at = value.IndexOf('@', StringComparison.Ordinal);

        return at > 0
            && at < value.Length - 1
            && value.IndexOf('.', at) > at + 1
            && !value.Contains(' ', StringComparison.Ordinal);
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

// "Claim" is a health-fund claim in this domain and a token claim in the framework. The
// security one is aliased, following the same rule the app uses for Patient.
using SecurityClaim = System.Security.Claims.Claim;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>What the API is configured to sign and accept.</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "molargo-api";

    public string Audience { get; set; } = "molargo";

    /// <summary>
    /// The signing key. Configuration only — never a literal in this file.
    /// </summary>
    /// <remarks>
    /// Whoever holds it can mint a token for any clinic in the database, so it belongs
    /// wherever the deployment keeps its secrets and nowhere else. The API refuses to start
    /// without one rather than falling back to a default: a development default that
    /// reached production would be a signing key published on a repository.
    /// </remarks>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// How long a token lasts.
    /// </summary>
    /// <remarks>
    /// Eight hours — a working day, so a sign-in lasts a shift. The claims are a snapshot,
    /// so this is also how long a permission taken away on the Users screen can still be
    /// exercised by a token already issued. Shorten it if that is too long; it is the whole
    /// trade being made.
    /// </remarks>
    public int LifetimeHours { get; set; } = 8;
}

/// <summary>What a caller sends to sign in.</summary>
/// <remarks>
/// The same three things the app's own sign-in screen asks for. The clinic code is what
/// makes a username unique — two practices can each have a "jsmith" — so a token request
/// without it could not identify a person even with the right password.
/// </remarks>
public sealed record SignInRequest(string ClinicCode, string Username, string Password);

public sealed record SignInResponse(
    string Token, DateTime ExpiresUtc, string DisplayName, Guid TenantId, string Practice);

/// <summary>
/// Turns a clinic code, username and password into a signed token.
/// </summary>
/// <remarks>
/// A deliberately separate implementation from the app's <c>AuthService</c>, which does
/// things an API must not: it starts a process-wide session, it can hold a sign-in waiting
/// on an emailed code, and it remembers the signed-in person. What is shared is the part
/// that must never differ — the password verifier, which lives in
/// <see cref="IPasswordHasher"/> in the data project for exactly this reason.
/// </remarks>
public sealed class TokenService
{
    /// <summary>
    /// Attempts before an account is locked, and for how long.
    /// </summary>
    /// <remarks>
    /// The same five-and-fifteen the app enforces. A second, looser rule on the API would
    /// make the lockout worth nothing: an attacker would simply use whichever door counts
    /// more slowly.
    /// </remarks>
    private const int MaximumAttempts = 5;

    private const int LockoutMinutes = 15;

    private readonly IDbContextFactory<MolargoDbContext> _factory;
    private readonly IPasswordHasher _hasher;
    private readonly JwtOptions _options;
    private readonly TimeProvider _time;

    public TokenService(
        IDbContextFactory<MolargoDbContext> factory,
        IPasswordHasher hasher,
        JwtOptions options,
        TimeProvider time)
    {
        _factory = factory;
        _hasher = hasher;
        _options = options;
        _time = time;
    }

    /// <summary>
    /// Signs a caller in, or says no.
    /// </summary>
    /// <remarks>
    /// One refusal for every failure — wrong clinic, unknown user, wrong password, inactive
    /// account. Distinguishing them tells an attacker which half of a guess was right, and
    /// the practice gains nothing from the distinction that the audit log does not already
    /// record.
    /// </remarks>
    public async Task<(SignInResponse? Response, string? Refusal)> SignInAsync(
        SignInRequest request, CancellationToken ct = default)
    {
        const string refusal = "Those details do not match.";

        if (string.IsNullOrWhiteSpace(request.ClinicCode)
            || string.IsNullOrWhiteSpace(request.Username)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return (null, refusal);
        }

        // No tenant filter yet — there is no tenant until this succeeds, which is the one
        // query in the API that legitimately reads across clinics. IgnoreQueryFilters is
        // explicit about that rather than leaving it to a context that happens to be
        // unstamped.
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var code = request.ClinicCode.Trim();

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Slug == code && row.IsActive && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        if (tenant is null) return (null, refusal);

        var username = request.Username.Trim();

        var provider = await db.Providers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.TenantId == tenant.Id
                && row.Username == username
                && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        if (provider is null || !provider.CanSignIn) return (null, refusal);

        var now = _time.GetUtcNow().UtcDateTime;

        if (provider.LockedUntilUtc is { } until && until > now)
        {
            return (null,
                $"That account is locked for another {(int)(until - now).TotalMinutes + 1} minutes.");
        }

        if (!_hasher.Verify(request.Password, provider.PasswordHash!))
        {
            provider.FailedSignInCount++;

            if (provider.FailedSignInCount >= MaximumAttempts)
            {
                provider.LockedUntilUtc = now.AddMinutes(LockoutMinutes);
                provider.FailedSignInCount = 0;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return (null, refusal);
        }

        provider.FailedSignInCount = 0;
        provider.LockedUntilUtc = null;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var expires = now.AddHours(Math.Max(1, _options.LifetimeHours));

        return (new SignInResponse(
            Issue(tenant, provider, expires),
            expires,
            provider.DisplayName is { Length: > 0 } display ? display : provider.FullName,
            tenant.Id,
            tenant.Name), null);
    }

    /// <summary>
    /// A token for somebody already authenticated by something else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the app's sign-in route, which runs the shared <c>IAuthService</c> — the same
    /// class the device used to run locally, with the real lockout counter and the two-step
    /// code. By the time this is called the credential has been checked; re-checking it here
    /// would be a second password verification against the same row, which would double
    /// every failed-attempt count and lock accounts at half the configured threshold.
    /// </para>
    /// <para>
    /// Not public to anything but the endpoints in this assembly. It mints a credential
    /// without seeing one, which is correct exactly once — immediately after a check that
    /// has already happened.
    /// </para>
    /// </remarks>
    internal async Task<(string Token, DateTime ExpiresUtc, ProviderRole Role, bool IsOwner,
        PracticePermissions Permissions, Guid LocationId, string? CurrencyCode)?>
        IssueForAsync(Guid tenantId, Guid providerId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Filters bypassed: this context has no tenant, because establishing one is what
        // the token being minted is for.
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == tenantId, ct)
            .ConfigureAwait(false);

        var provider = await db.Providers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == providerId && row.TenantId == tenantId, ct)
            .ConfigureAwait(false);

        if (tenant is null || provider is null) return null;

        var expires = _time.GetUtcNow().UtcDateTime
            .AddHours(Math.Max(1, _options.LifetimeHours));

        // Read from the staff record rather than taken from the caller. The sign-in result
        // the app's AuthService returns does not carry the role or the permissions, and a
        // route that accepted them as arguments would let the shape of a request decide what
        // a token was allowed to do.
        return (
            Issue(tenant, provider, expires),
            expires,
            provider.Role,
            provider.IsOwner,
            provider.Permissions,
            provider.PrimaryLocationId ?? Guid.Empty,
            tenant.CurrencyCode);
    }

    private string Issue(Tenant tenant, Provider provider, DateTime expires)
    {
        var claims = new List<SecurityClaim>
        {
            new SecurityClaim(JwtRegisteredClaimNames.Sub, provider.Id.ToString()),
            new SecurityClaim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new SecurityClaim(ClaimTypes.Name, provider.FullName),
            new SecurityClaim(MolargoClaims.TenantId, tenant.Id.ToString()),
            new SecurityClaim(MolargoClaims.ProviderId, provider.Id.ToString()),
            new SecurityClaim(MolargoClaims.Role, provider.Role.ToString()),
            new SecurityClaim(MolargoClaims.Permissions, provider.Permissions.ToString()),
            new SecurityClaim(MolargoClaims.IsOwner, provider.IsOwner.ToString()),
        };

        if (provider.PrimaryLocationId is { } locationId)
        {
            claims.Add(new SecurityClaim(MolargoClaims.LocationId, locationId.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

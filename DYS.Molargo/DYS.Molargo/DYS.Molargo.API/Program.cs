using System.Text;
using DYS.Molargo.Api.Endpoints;
using DYS.Molargo.Api.Infrastructure;
using DYS.Molargo.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---- the seed verb ------------------------------------------------------

// Before anything else is wired. It needs the configuration and a connection and nothing
// more — no authentication, no endpoints, no listening socket — and an empty server
// database is the one state in which none of the rest of this file can do anything useful.
if (args.Contains(SeedCommand.Verb, StringComparer.OrdinalIgnoreCase))
{
    return await SeedCommand.RunAsync(args, builder.Configuration, builder.Environment);
}

// ---- configuration ------------------------------------------------------

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();

// Refused at startup rather than defaulted. A signing key with a fallback value is a key
// that ships, and whoever has it can mint a token for any clinic in the database.
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is missing or shorter than 32 characters. Development gets one "
        + "from appsettings.Development.json; anywhere else, supply it from the "
        + "deployment's secret store or Jwt__SigningKey.");
}

// The development key is in appsettings.Development.json, which is committed — so it is
// public, and a token signed with it could be minted by anybody who can read the
// repository. Harmless against a localhost database, fatal against a real one.
//
// Checked by value rather than trusted to a file name: configuration composes, and the
// most likely way this key reaches production is a Development file copied to a server, or
// an environment variable set from a README by somebody in a hurry.
if (!builder.Environment.IsDevelopment()
    && string.Equals(jwt.SigningKey, DevelopmentSigningKey, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        $"Jwt:SigningKey is the published development key and the environment is "
        + $"{builder.Environment.EnvironmentName}. Generate one per environment — anyone "
        + "holding this value can mint a token for any clinic in the database.");
}

var connection = builder.Configuration.GetConnectionString("Molargo");

if (string.IsNullOrWhiteSpace(connection))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Molargo is missing. Development gets one from "
        + "appsettings.Development.json; anywhere else, supply it from the deployment's "
        + "secret store or ConnectionStrings__Molargo. The API has no local database to "
        + "fall back on — unlike the app, which owns a file.");
}

builder.Services.AddSingleton(jwt);

// ---- the data layer -----------------------------------------------------

// The same context the device uses, on a different provider. The money columns differ and
// nothing else does — see MolargoDbContext, which asks the provider rather than being told.
builder.Services.AddDbContextFactory<MolargoDbContext>(options =>
    options.UseNpgsql(
        connection,
                // The migrations live here, not in DYS.Molargo.Data. Data is deliberately
                // provider-free — it is shared with the device, which is on SQLite — and
                // a migration is a script for one particular database engine.
        npgsql => npgsql.MigrationsAssembly(typeof(Program).Assembly.FullName)));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton(TimeProvider.System);

// Scoped, all three. The clinic, the caller and the context that carries the clinic are
// per request here — the one assumption the app's singletons could safely make and this
// cannot. See RequestTenantContext.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, RequestTenantContext>();
builder.Services.AddScoped<IApiCaller, ApiCaller>();
builder.Services.AddScoped<IMolargoContextSource, ApiContextSource>();

// The same generic repository the device uses, over Postgres. Scoped rather than the
// device's singleton, because it holds the request's tenant.
builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

builder.Services.AddScoped<TokenService>();

// ---- authentication -----------------------------------------------------

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

            // No grace on expiry. The default five minutes is for clock skew between
            // machines; both ends of this are the same server until proven otherwise, and
            // a token's lifetime is already the window a revoked permission stays usable.
            ClockSkew = TimeSpan.Zero,
        };
    });

// Authenticated by default, opt out per route.
//
// The endpoints used to sit in groups that carried RequireAuthorization once. One route per
// file means each would otherwise have to remember it, and a forgotten RequireAuthorization
// is not a broken endpoint — it is an open one, serving a clinic's records to anybody who
// finds the URL. A fallback policy inverts that: forgetting means locked, and the single
// route that must be anonymous says so out loud.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSchemeTransformer>());

var app = builder.Build();

// ---- pipeline -----------------------------------------------------------

// AllowAnonymous, because the fallback policy above covers this too — and an OpenAPI
// document behind a token is one a client cannot fetch before it knows how to sign in.
// Development only, so the shape of the API is not public in production.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    // An interactive client at /scalar, so the API can be exercised without writing a
    // request by hand. Development only, for the same reason the document is: the
    // shape of the API is not something to publish.
    app.MapScalarApiReference(options => options
        .WithTitle("Molargo API")
        .AddPreferredSecuritySchemes("Bearer"))
        .AllowAnonymous();
}

app.UseAuthentication();
app.UseAuthorization();

// Reads the tenant off the caller's token and into the scoped context, before anything
// touches a repository. Every query filters on it, so a request that skipped this matches
// no rows rather than all of them.
app.UseMiddleware<TenantResolutionMiddleware>();

// Every IEndpoint in this assembly, one route per class. Adding an endpoint is adding a
// file — there is no list here to forget to update. See IEndpoint.
app.MapMolargoEndpoints();

app.Run();

return 0;

/// <summary>
/// The signing key published in appsettings.Development.json.
/// </summary>
/// <remarks>
/// Named here so the guard above can refuse it outside Development. Kept in step with that
/// file by hand — which is why the guard's message says what to do rather than only that
/// something is wrong: if the two ever drift, the symptom is a development environment that
/// refuses to start, which is the harmless direction.
/// </remarks>
public partial class Program
{
    internal const string DevelopmentSigningKey =
        "molargo-development-only-signing-key-not-for-any-real-database";
}

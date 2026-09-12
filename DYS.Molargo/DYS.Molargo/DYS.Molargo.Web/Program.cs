using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using DYS.Molargo.Shared.Data;
using DYS.Molargo.Shared.Documents;
using DYS.Molargo.Shared.Features.Patient.Services;
using DYS.Molargo.Shared.Extensions;
using DYS.Molargo.Shared.Services;
using DYS.Molargo.Web.Components;
using DYS.Molargo.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The platform-specific half — the only things this head knows that Shared cannot.
builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddSingleton<IDatabasePathProvider, WebDatabasePathProvider>();
builder.Services.AddSingleton<IDocumentPathProvider, WebDocumentPathProvider>();

// Scoped, not singleton: it navigates, so it depends on NavigationManager.
builder.Services.AddScoped<IDocumentViewer, WebDocumentViewer>();

// ---- sign-in that survives a reload ---------------------------------------
//
// A Blazor circuit cannot set a cookie: the response that would carry it was written when
// the page loaded. So a verified sign-in is parked server-side, the circuit redirects
// through /auth/resume, and that request sets the cookie. Without this the session lives
// only in the circuit and every page refresh signs the user out.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<SessionHandoffStore>();
builder.Services.AddScoped<ISessionHandoff, CookieSessionHandoff>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "molargo.session";

        // HttpOnly so page script cannot read it, and SameSite=Strict because nothing
        // legitimately links into this app from another site.
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        // Eight hours, sliding — a shift, renewed while someone is working. Long enough
        // that a refresh or a walk to another room does not sign anyone out, short enough
        // that a machine left overnight is not still signed in the next morning.
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        options.LoginPath = "/sign-in";
        options.LogoutPath = "/auth/sign-out";
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Everything else: view models, feature services, navigation, session, the clock, and the
// local SQLite store. The web head reads the same local database as the device heads while
// the app is offline-only; an API-backed repository replaces this when sync is built.
builder.Services.AddMolargoCore();
builder.Services.AddMolargoLocalStore();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();

// ---- the cookie handoff ---------------------------------------------------

// Exchanges a single-use token for the session cookie.
//
// GET, because the circuit gets here by navigating. That is safe only because the token is
// unguessable, server-issued, single-use and expires in thirty seconds — it is not a
// credential anyone can construct, and redeeming it twice fails.
app.MapGet("/auth/resume", async (
    string? token,
    HttpContext http,
    SessionHandoffStore store) =>
{
    var user = store.Redeem(token);

    // A missing, expired or replayed token lands back on the sign-in screen rather than
    // reporting anything. There is nothing useful to tell whoever followed a stale link.
    if (user is null) return Results.Redirect("/sign-in");

    var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.NameIdentifier, user.ProviderId.ToString()),
            new Claim(AuthClaims.ProviderId, user.ProviderId.ToString()),
            new Claim(AuthClaims.TenantId, user.TenantId.ToString()),
            new Claim(AuthClaims.TenantName, user.TenantName),
        ],
        CookieAuthenticationDefaults.AuthenticationScheme);

    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    return Results.Redirect("/");
});

app.MapGet("/auth/sign-out", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.Redirect("/sign-in");
});

// Streams a stored patient document.
//
// A plain endpoint rather than something the Blazor circuit serves: a browser downloads
// a file by fetching a URL, and pushing 25MB of radiograph through the SignalR
// connection as base64 would hold all of it in memory at both ends.
//
// Behind the session cookie, now that there is one.
//
// This route used to be open, and it is the only one that hands out a file — an
// unauthenticated caller who guessed a document id got a radiograph. RequireAuthorization
// closes that. The document is still fetched through the tenant filter, so a signed-in
// user at one clinic cannot pull another clinic's file even with its id.
app.MapGet("/documents/{id:guid}", async (
    Guid id,
    IPatientService patients,
    IDocumentStore store,
    CancellationToken ct) =>
{
    var document = await patients.GetDocumentAsync(id, ct);
    if (document is null) return Results.NotFound();

    var content = await store.OpenAsync(document.RelativePath, ct);
    if (content is null) return Results.NotFound();

    // Inline, with a download name. The browser shows a PDF or an image in a tab and
    // saves anything it cannot render, which is what someone clicking a document wants.
    return Results.File(
        content,
        document.ContentType ?? "application/octet-stream",
        Path.GetFileName(document.RelativePath),
        enableRangeProcessing: true);
}).RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    // Routes.razor lives in DYS.Molargo.Shared, and so does every screen it routes to.
    // Without this the router finds no @page in this assembly and every URL 404s.
    .AddAdditionalAssemblies(typeof(DYS.Molargo.Shared._Imports).Assembly);

app.Run();

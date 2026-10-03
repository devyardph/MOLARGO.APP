using System.Reflection;
using DYS.Molargo.Shared.Documents;
using Microsoft.Extensions.DependencyInjection;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Api;
using DYS.Molargo.Shared.Extensions;
using DYS.Molargo.Services.Features.Auth;
using DYS.Molargo.Services.Features.Diary;
using DYS.Molargo.Services.Features.Inventory;
using DYS.Molargo.Services.Features.Patient;
using DYS.Molargo.Shared.Services;

namespace DYS.Molargo.SelfTest;

/// <summary>
/// Drives the app's own client against a running server.
/// </summary>
/// <remarks>
/// <para>
/// The device's half of API mode is a generated proxy and a hand-written sign-in, and
/// neither is exercised by anything that builds. A compiling client proves nothing: the
/// failures live in serialisation, in argument order, in whether the session actually ends
/// up filled — all of them runtime, all of them silent until a screen is blank.
/// </para>
/// <para>
/// So this registers the real client the way <c>MauiProgram</c> does — the same
/// <c>AddMolargoApiStore</c>, the same proxies, the same <c>ApiAuthService</c> — and calls
/// through it. It lives in the API project only because that is a console process that can
/// reference Shared; nothing here is server code, and it talks to the server over HTTP like
/// any other client.
/// </para>
/// <para>
/// Development only, for the same reason the seed is: it signs in, and the connection
/// string it would be pointed at could be a real one.
/// </para>
/// </remarks>
internal static class SelfTestCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        // Development only, for the same reason the seed is: it signs in, and the server it
        // is pointed at could be a real one. Read from the environment variable rather than
        // from an IHostEnvironment — there is no web host here to supply one.
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"Refused: selftest runs in Development only (environment is '{environment}').");

            return 1;
        }

        var address = Argument(args, "--server") ?? "http://localhost:5165";
        var clinic = Argument(args, "--clinic") ?? "molargo-dental";
        var user = Argument(args, "--user") ?? "rvance";
        var password = Argument(args, "--password") ?? "molargo-demo";

        var services = new ServiceCollection();

        // Deliberately not AddMolargoShell: the view models want a navigator, which wants
        // Blazor's NavigationManager, which does not exist in a console. The session and
        // the handoff are registered by hand because the client genuinely needs them —
        // ApiAuthService fills one and clears the other.
        // The shell's own registrations, with stand-ins for the three things only a head can
        // supply. This is what lets every view model be resolved below: a missing
        // registration shows up here as a named failure, where in the app it is an unhandled
        // exception during render and a bare "Something went wrong" bar.
        services.AddMolargoShell();
        services.AddMolargoApiStore(new MolargoApiOptions { BaseAddress = new Uri(address) });

        // After the shell and the store, not before: both register with AddScoped rather
        // than TryAdd, so a stand-in put in first is simply replaced by the real one — which
        // then wants a NavigationManager and fails every view model for the wrong reason.
        services.AddSingleton(NullProxy.For<IAppNavigator>());
        services.AddSingleton<IFormFactor, DesktopFormFactor>();
        services.AddSingleton<IDocumentViewer, NoDocumentViewer>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider;

        Console.WriteLine();
        Console.WriteLine($"Client self-test against {address}");
        Console.WriteLine();

        var failures = 0;

        // ---- nothing is answering locally ----------------------------------

        // The question this answers is "does a feature service still run on the device".
        // Name-matching the catalogue against the view models cannot answer it: a local
        // registration left behind would satisfy the same interface and simply win, and
        // every screen would keep working against a database that is not the practice's.
        //
        // So each one is resolved and asked what it actually is.
        var local = MolargoRpc.Services
            .Where(contract => resolved.GetRequiredService(contract) is not RpcServiceProxy)
            .Select(contract => contract.Name)
            .ToList();

        failures += Check(
            $"all {MolargoRpc.Services.Count} feature services go to the API"
                + (local.Count > 0 ? $" — LOCAL: {string.Join(", ", local)}" : string.Empty),
            local.Count == 0);

        // ---- every catalogued member can actually be called ------------------

        // Resolving a service proves it exists; it does not prove its members work. An event
        // or a synchronous property on a proxied interface throws the moment it is touched,
        // which is how a component subscribing to ISetupService.Changed took down the whole
        // app bar — found only by a person clicking, after the DI check had passed.
        //
        // Events are fine now: the proxy keeps the handler locally. Anything else that has to
        // answer without awaiting is still a real problem, so it is listed here rather than
        // waiting to be discovered on whichever screen touches it.
        var uncrossable = new List<string>();

        foreach (var contract in MolargoRpc.Services)
        {
            foreach (var member in contract.GetMembers())
            {
                var bad = member switch
                {
                    PropertyInfo property => $"{property.Name} (property)",
                    MethodInfo m when m.IsSpecialName => null,
                    MethodInfo m when m.ReturnType == typeof(Task) => null,
                    MethodInfo m when m.ReturnType.IsGenericType
                        && m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>) => null,
                    MethodInfo m => $"{m.Name} (returns {m.ReturnType.Name})",
                    _ => null,
                };

                if (bad is not null) uncrossable.Add($"{MolargoRpc.NameOf(contract)}.{bad}");
            }
        }

        failures += Check(
            "every catalogued member can cross the wire"
                + (uncrossable.Count > 0 ? $" — CANNOT: {string.Join(", ", uncrossable)}" : string.Empty),
            uncrossable.Count == 0);

        // ---- every screen can be built --------------------------------------

        // Resolving a view model runs its constructor, which is where a missing dependency
        // actually bites. Done before signing in, because that is when the app does it too:
        // the shell builds the screen and only then loads anything into it.
        var viewModelNames = services
            .Where(registration => registration.ServiceType.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .Select(registration => registration.ServiceType)
            .Distinct()
            .ToList();

        var viewModelCount = viewModelNames.Count;
        var broken = new List<string>();

        foreach (var viewModel in viewModelNames)
        {
            try
            {
                resolved.GetRequiredService(viewModel);
            }
            catch (Exception ex)
            {
                // The innermost message names the type that could not be resolved; the outer
                // one says only that something failed to activate.
                var cause = ex;
                while (cause.InnerException is not null) cause = cause.InnerException;

                broken.Add($"{viewModel.Name}: {cause.Message}");
            }
        }

        failures += Check(
            $"all {viewModelCount} view models resolve",
            broken.Count == 0);

        foreach (var one in broken) Console.Error.WriteLine($"        {one}");

        // ---- sign in -------------------------------------------------------

        var auth = resolved.GetRequiredService<IAuthService>();
        var session = resolved.GetRequiredService<ISessionService>();

        var signIn = await auth.SignInAsync(clinic, user, password);

        if (!signIn.Succeeded)
        {
            Console.Error.WriteLine($"  FAIL  sign in: {signIn.Failure ?? "refused"}");

            return 1;
        }

        Console.WriteLine($"  ok    signed in as {signIn.DisplayName} at {signIn.TenantName}");

        // The point of ApiAuthService existing at all. A proxied sign-in would have checked
        // the password correctly and left this empty.
        failures += Check("session filled locally", session.IsSignedIn && session.ProviderId is not null);
        failures += Check("session found its sites", session.Locations.Count > 0);
        failures += Check("session has a location", session.LocationId != Guid.Empty);

        // ---- the proxies ---------------------------------------------------

        var patients = resolved.GetRequiredService<IPatientService>();
        var page = await patients.SearchAsync(new PatientQuery { Page = 0, PageSize = 5 });

        failures += Check($"patients paged ({page.TotalCount} total)", page.Items.Count > 0);

        // A second call with an argument the first did not have, to prove arguments are
        // carried in order rather than by luck.
        var searched = await patients.SearchAsync(
            new PatientQuery { SearchTerm = "a", Page = 0, PageSize = 5 });

        failures += Check("patients searched", searched.TotalCount <= page.TotalCount);

        var stock = await resolved.GetRequiredService<IInventoryService>().GetAllSuppliersAsync();
        failures += Check($"suppliers returned ({stock.Count})", stock.Count > 0);

        var reminders = await resolved.GetRequiredService<IDiaryService>().AreRemindersOnAsync();
        failures += Check("a bool came back", reminders || !reminders);

        // Money is the one thing the PostgreSQL move changed and the one thing a wire
        // format can quietly round — JSON has no decimal, so a careless client reads these
        // as double and 0.1 + 0.2 stops being 0.3. Checked on the way back through the
        // proxy, not just in the server's own response.
        var owing = page.Items.Sum(row => row.Balance);

        failures += Check(
            $"balances arrived as decimals (sum {owing})",
            page.Items.All(row => row.Balance == decimal.Round(row.Balance, 2)));

        // ---- a method that answers null --------------------------------------

        // Most reads can legitimately find nothing, and that path shares no code with the
        // one that finds something. It was broken for every one of them at once: the server
        // wrote a zero-length body for a null result, and zero bytes is not a JSON document,
        // so the client threw a parse error for a perfectly ordinary answer.
        var missing = await patients.FindEmailHolderAsync("nobody-at-all@example.invalid");

        failures += Check("a null result comes back as null, not a parse error", missing is null);

        var noSuchPatient = await patients.GetAsync(Guid.Empty);

        failures += Check("a missing record comes back as null", noSuchPatient is null);

        // ---- a file on the wire --------------------------------------------

        // The one argument type JSON cannot carry. Proven by actually sending one, because
        // the multipart path shares no code with the ordinary one past the first branch —
        // and a document upload that fails only on a real device is the worst place to find
        // that out.
        if (page.Items.Count > 0)
        {
            var bytes = "Molargo self-test attachment."u8.ToArray();

            var document = await patients.AddDocumentAsync(
                page.Items[0].Id,
                DocumentKind.Other,
                "selftest.txt",
                "text/plain",
                new MemoryStream(bytes));

            failures += Check(
                $"document uploaded ({document.SizeBytes} bytes, kind {document.Kind})",
                document.SizeBytes == bytes.Length);

            // Cleaned up after itself. A smoke test that leaves a row behind every run is a
            // patient record filling with rubbish.
            var removed = await patients.DeleteDocumentAsync(document.Id);
            failures += Check("document removed again", removed);
        }

        // ---- the guard -----------------------------------------------------

        // Re-reads the staff record on the server rather than answering from the session,
        // which is why it is proxied rather than kept local.
        var owner = await resolved.GetRequiredService<IPracticeGuard>().IsOwnerAsync();
        failures += Check($"guard answered (owner: {owner})", true);

        // ---- sign out ------------------------------------------------------

        await auth.SignOutAsync();

        failures += Check("session cleared", !session.IsSignedIn);
        failures += Check("token cleared", !resolved.GetRequiredService<IApiTokenStore>().HasToken);

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) FAILED.");
        Console.WriteLine();

        return failures == 0 ? 0 : 1;
    }

    private static int Check(string what, bool passed)
    {
        Console.WriteLine($"  {(passed ? "ok  " : "FAIL")}  {what}");

        return passed ? 0 : 1;
    }

    private static string? Argument(string[] args, string name)
    {
        var at = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}

/// <summary>
/// A do-nothing implementation of any interface.
/// </summary>
/// <remarks>
/// Generated rather than written out. IAppNavigator has two dozen members and gains one
/// whenever a screen is added, and a hand-written stub here would break the build of this
/// test every time somebody added a route — which is how a test stops being run.
/// </remarks>
// Not sealed: DispatchProxy.Create builds a subclass of this at runtime and refuses a
// sealed base, with a message naming a compiler-generated type that appears nowhere.
file class NullProxy : DispatchProxy
{
    public static T For<T>() => DispatchProxy.Create<T, NullProxy>();

    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        method?.ReturnType is { } returns && returns != typeof(void) && returns.IsValueType
            ? Activator.CreateInstance(returns)
            : null;
}

file sealed class DesktopFormFactor : IFormFactor
{
    public string GetFormFactor() => "Desktop";

    public string GetPlatform() => "SelfTest";
}

file sealed class NoDocumentViewer : IDocumentViewer
{
    public bool CanOpen => false;

    public Task OpenAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
}

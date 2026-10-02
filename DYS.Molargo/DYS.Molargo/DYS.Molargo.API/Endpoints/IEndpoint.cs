using System.Reflection;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>
/// One route, in one file.
/// </summary>
/// <remarks>
/// <para>
/// The endpoints used to be grouped several to a static class. That reads well at three
/// routes and badly at thirty: the file that holds "patients" becomes the file everybody
/// edits, its DTOs drift to the top away from the handlers that use them, and a change to
/// one route shows up in a diff against all of them.
/// </para>
/// <para>
/// One class per route instead. The file name is the route — <c>CreatePatientEndpoint</c>
/// is <c>POST /patients</c> and nothing else — so finding the code for a call is a matter
/// of knowing what the call does, and two people adding endpoints do not collide.
/// </para>
/// </remarks>
public interface IEndpoint
{
    /// <summary>Registers this route.</summary>
    void Map(IEndpointRouteBuilder app);
}

public static class EndpointRegistration
{
    /// <summary>
    /// Finds every endpoint in this assembly and maps it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scanned rather than listed. A list is one more place to edit for every new file, and
    /// the failure it invites — an endpoint written, tested by its author against a running
    /// app, and left out of the list — is one nobody sees until a caller gets a 404.
    /// </para>
    /// <para>
    /// The trade is the opposite failure: a class that forgets <see cref="IEndpoint"/> is
    /// not registered and nothing says so. That one surfaces immediately, because the route
    /// it was written for does not answer at all.
    /// </para>
    /// </remarks>
    public static void MapMolargoEndpoints(this IEndpointRouteBuilder app)
    {
        var endpoints = typeof(IEndpoint).Assembly
            .GetTypes()
            .Where(type => typeof(IEndpoint).IsAssignableFrom(type)
                && type is { IsAbstract: false, IsInterface: false })

            // Ordered by name so the route list is stable between runs. Reflection does not
            // promise an order, and an OpenAPI document that reshuffles itself every build
            // is a document nobody can diff.
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(Activator.CreateInstance)
            .Cast<IEndpoint>();

        foreach (var endpoint in endpoints) endpoint.Map(app);
    }
}

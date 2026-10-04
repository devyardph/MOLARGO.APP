using DYS.Molargo.Api.Infrastructure;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Services.Features.Platform;
using DYS.Molargo.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Api.Endpoints;

/// <summary>
/// Where a payment provider tells us a subscription charge was paid.
/// </summary>
/// <remarks>
/// <para>
/// The other half of the payment path. The app sends a practice to a hosted checkout and
/// then has no idea what happened — the payer finishes on the provider's own page, which
/// may be on their phone, long after the request that created the link has ended. This is
/// how the answer gets back.
/// </para>
/// <para>
/// Anonymous, and it has to be: the provider holds no token and never will. What stands in
/// for authentication is the signature on the body, checked against the signing secret on
/// the country's gateway row. That makes the signing secret the only thing between this
/// route and anybody marking any charge paid, which is why a gateway without one refuses
/// every webhook rather than trusting it.
/// </para>
/// <para>
/// The country is in the path rather than guessed from the body. One provider may serve
/// several countries with different keys, and a webhook that had to be tried against every
/// gateway in turn would be a signature oracle — each attempt telling an attacker whether
/// that country's secret was the one.
/// </para>
/// </remarks>
public sealed class PaymentWebhookEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/payments/{country}", HandleAsync)
            .AllowAnonymous()
            .WithTags("Payments")
            .WithSummary("Receives a signed payment event for one country's gateway.");
    }

    private static async Task<IResult> HandleAsync(
        string country,
        HttpRequest request,
        IPaymentGatewayResolver gateways,
        PaymentSettlement settlement,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var log = logs.CreateLogger("Molargo.PaymentWebhook");

        var credentials = await gateways.ForCountryAsync(country, ct).ConfigureAwait(false);

        if (credentials is null)
        {
            // 404 rather than 400. A country with no gateway is a route that does not
            // exist, and saying anything more precise tells a stranger which countries are
            // configured.
            log.LogWarning("Webhook for {Country}, which has no gateway.", country);

            return Results.NotFound();
        }

        var provider = gateways.ProviderFor(credentials);

        if (provider is null)
        {
            log.LogError(
                "{Country} names provider {Provider}, which this build cannot serve.",
                credentials.CountryCode, credentials.ProviderName);

            return Results.NotFound();
        }

        // Read as text, exactly as it arrived. A signature covers the bytes, so anything
        // that deserialises and re-serialises — reordering a key, changing whitespace —
        // makes every webhook fail to verify, which reads as nobody paying.
        request.EnableBuffering();

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        var signature = request.Headers["Paymongo-Signature"].FirstOrDefault()
            ?? request.Headers["X-Signature"].FirstOrDefault();

        var verdict = provider.Interpret(credentials, body, signature);

        if (!verdict.Verified)
        {
            // Logged in full and answered with nothing. The provider retries a non-2xx,
            // which is right: a signing secret that has just been corrected should let the
            // retries through rather than needing the payment chased by hand.
            log.LogWarning(
                "Refused a {Country} webhook: {Detail}", credentials.CountryCode, verdict.Detail);

            return Results.Unauthorized();
        }

        if (verdict.Outcome == PaymentOutcome.Ignored || verdict.Reference is null)
        {
            // Acknowledged. An event this app does not act on still has to be answered, or
            // the provider redelivers it for hours.
            return Results.Ok();
        }

        // By reference, never by a charge id this handler chose. PaymentSettlement does the
        // lookup, so the worst a mistake here can do is fail to find a row.
        var found = await settlement
            .ApplyAsync(verdict.Reference, verdict.Outcome, verdict.Detail, ct)
            .ConfigureAwait(false);

        if (!found)
        {
            // Acknowledged, not retried. A reference this app does not know will never
            // start being known, so answering an error would buy hours of redelivery and
            // no payment.
            log.LogWarning(
                "A {Country} webhook named reference {Reference}, which matches no charge.",
                credentials.CountryCode, verdict.Reference);

            return Results.Ok();
        }

        log.LogInformation(
            "Charge with reference {Reference} marked {Outcome} from a {Country} webhook.",
            verdict.Reference, verdict.Outcome, credentials.CountryCode);

        return Results.Ok();
    }
}

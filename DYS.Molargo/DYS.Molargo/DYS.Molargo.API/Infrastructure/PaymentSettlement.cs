using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Settles a subscription charge on the word of a payment provider.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <c>ISubscriptionBillingService.MarkPaidAsync</c>, and the reason is a
/// security one rather than a tidiness one. That interface is in the RPC catalogue, so
/// every signed-in user of every clinic can call it — it is safe only because it checks
/// for the vendor's platform role on every call. A webhook has no session at all, so it
/// cannot pass that check, and the obvious fix of relaxing it would hand any practice the
/// ability to mark its own bill paid.
/// </para>
/// <para>
/// So this lives in the API, is registered nowhere the wire can reach, and is used by one
/// endpoint. Its authority is the signature the webhook arrived with, checked before this
/// is called.
/// </para>
/// <para>
/// It settles <em>by reference</em> and takes no charge id. The caller cannot name a row;
/// it can only hand over the identifier the provider issued, which this looks up. A
/// mistake in the endpoint therefore cannot settle an arbitrary charge — the worst it can
/// do is fail to find one.
/// </para>
/// <para>
/// The two states a human has and this does not: waiving a charge, and reversing a payment.
/// Both are judgements, and both stay on the operator's screen behind the platform guard.
/// </para>
/// </remarks>
internal sealed class PaymentSettlement
{
    private readonly IDbContextFactory<MolargoDbContext> _contexts;
    private readonly IClock _clock;
    private readonly ILogger<PaymentSettlement> _log;

    public PaymentSettlement(
        IDbContextFactory<MolargoDbContext> contexts,
        IClock clock,
        ILogger<PaymentSettlement> log)
    {
        _contexts = contexts;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Applies a provider's verdict to the charge carrying its reference.
    /// </summary>
    /// <returns>
    /// True when a row was found and dealt with. False means nothing matched, which the
    /// caller acknowledges rather than retries — a reference this app does not know will
    /// never start being known.
    /// </returns>
    public async Task<bool> ApplyAsync(
        string reference, PaymentOutcome outcome, string? detail, CancellationToken ct)
    {
        await using var db = await _contexts.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Filters bypassed: a charge is stamped with the clinic it bills, and this request
        // has no clinic — the provider is not signed in to anything.
        var charge = await db.SubscriptionCharges
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                row => row.Reference == reference && !row.IsDeleted, ct)
            .ConfigureAwait(false);

        if (charge is null) return false;

        var now = _clock.UtcNow;

        if (outcome == PaymentOutcome.Paid)
        {
            // Redelivery is normal and must not be a second payment. Treated as done
            // rather than as an error, because from the provider's side it is.
            if (charge.Status == ChargeStatus.Paid) return true;

            charge.Status = ChargeStatus.Paid;
            charge.AttemptedUtc ??= now;
            charge.SettledUtc = now;

            // Cleared, because it is no longer true. A paid charge still showing why it
            // once failed is how a clinic gets chased for money it has already sent.
            charge.FailureReason = null;
        }
        else
        {
            // A late failure for something already paid is not applied. Out-of-order
            // delivery happens, and letting a stale "failed" overwrite a settled charge
            // would put a practice back in arrears over a payment that cleared.
            if (charge.Status == ChargeStatus.Paid)
            {
                _log.LogWarning(
                    "A failure arrived for charge {Charge}, which is already paid. Ignored.",
                    charge.Id);

                return true;
            }

            charge.Status = ChargeStatus.Failed;
            charge.AttemptedUtc = now;
            charge.SettledUtc = null;
            charge.FailureReason = Trim(detail) ?? "The payment failed at the provider.";
        }

        charge.UpdatedUtc = now;

        // An audit entry, written here rather than through IAuditLog: that service stamps
        // the entry with the signed-in person and the device, and there is neither. The
        // provider is the actor, and saying so is the point of the row.
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = charge.TenantId,
            Action = AuditAction.Updated,
            EntityName = nameof(SubscriptionCharge),
            EntityId = charge.Id,
            ProviderName = "Payment provider",
            OccurredUtc = now,
            Detail = outcome == PaymentOutcome.Paid
                ? $"Payment confirmed by the provider (ref {reference})."
                : $"The provider reported a failed payment (ref {reference}): "
                    + charge.FailureReason,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    private static string? Trim(string? value) =>
        value is null ? null : value.Length <= 400 ? value : value[..400];
}

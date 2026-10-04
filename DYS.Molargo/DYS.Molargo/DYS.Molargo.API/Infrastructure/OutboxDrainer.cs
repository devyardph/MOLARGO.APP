using DYS.Molargo.Domain.Data;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Services.Features.Admin;
using DYS.Molargo.Services.Messaging;
using Microsoft.EntityFrameworkCore;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Sends the messages that were queued rather than sent.
/// </summary>
/// <remarks>
/// <para>
/// Reads <c>CommunicationLog</c> rows left <c>Pending</c> by whatever caused them, sends
/// each, and records the outcome on the row. Nothing else holds the queue: the row is the
/// message, the audit entry and the retry state at once, which is why it is in the database
/// rather than in Redis — a reminder accepted into a cache and lost on eviction is a patient
/// who does not turn up and no record that anything was meant to reach them.
/// </para>
/// <para>
/// The awkward part, and the reason this is not three lines: a request has a tenant and this
/// does not. Every sender below it reads the signed-in clinic's settings — the mail account,
/// the SMS gateway, the sending address — so the drain works one clinic at a time, in a
/// scope with that clinic's tenant set.
/// </para>
/// </remarks>
internal sealed class OutboxDrainer : BackgroundService
{
    /// <summary>How often to look for work.</summary>
    /// <remarks>
    /// Ten seconds. An appointment reminder is not urgent to the second, and a tighter loop
    /// is a query against every clinic's queue several times a minute for the long stretches
    /// when there is nothing to do.
    /// </remarks>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// When a claim is treated as abandoned.
    /// </summary>
    /// <remarks>
    /// A process that claimed rows and then died leaves them held. Without a timeout they
    /// stay held forever, behind a server that no longer exists — so a claim older than this
    /// is fair game. Generous enough that a slow gateway is not mistaken for a dead process.
    /// </remarks>
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Attempts before a message is given up on.
    /// </summary>
    /// <remarks>
    /// Five. Most failures here are permanent — a mistyped number, a closed mailbox — and
    /// retrying those forever means the queue never drains past them. A row that runs out
    /// of attempts is marked Failed with its reason, where the comms screen shows it.
    /// </remarks>
    private const int MaximumAttempts = 5;

    /// <summary>Rows taken per clinic per pass.</summary>
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopes;
    private readonly IDbContextFactory<MolargoDbContext> _contexts;
    private readonly IClock _clock;
    private readonly ILogger<OutboxDrainer> _log;
    private readonly string _who = $"{Environment.MachineName}:{Environment.ProcessId}";

    public OutboxDrainer(
        IServiceScopeFactory scopes,
        IDbContextFactory<MolargoDbContext> contexts,
        IClock clock,
        ILogger<OutboxDrainer> log)
    {
        _scopes = scopes;
        _contexts = contexts;
        _clock = clock;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        _log.LogInformation("Outbox drainer started as {Who}.", _who);

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stopping).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let one bad pass end the loop. A drainer that stops on an error is a
                // queue that silently stops draining, and the symptom of that is a quiet
                // drop in attendance nobody traces back to a background service.
                _log.LogError(ex, "Outbox pass failed; the next one runs as usual.");
            }

            try
            {
                await Task.Delay(Interval, stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var abandoned = now - ClaimTimeout;

        await using var db = await _contexts.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Filters bypassed throughout: this context has no tenant, because the queue spans
        // every clinic on the server and finding which ones have work is the first step.
        var tenants = await db.CommunicationLogs
            .IgnoreQueryFilters()
            .Where(row => row.Status == CommunicationStatus.Pending
                && !row.IsDeleted
                && (row.NextAttemptUtc == null || row.NextAttemptUtc <= now)
                && (row.ClaimedUtc == null || row.ClaimedUtc < abandoned))
            .Select(row => row.TenantId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var tenantId in tenants)
        {
            if (ct.IsCancellationRequested) return;

            await DrainTenantAsync(tenantId, now, abandoned, ct).ConfigureAwait(false);
        }
    }

    private async Task DrainTenantAsync(
        Guid tenantId, DateTime now, DateTime abandoned, CancellationToken ct)
    {
        await using var db = await _contexts.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Claimed before anything is sent, and claimed atomically. Two servers run this
        // loop against one table; without the claim the obvious implementation sends every
        // reminder twice, once per server, and the patient gets two texts.
        var claimed = await db.CommunicationLogs
            .IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId
                && row.Status == CommunicationStatus.Pending
                && !row.IsDeleted
                && (row.NextAttemptUtc == null || row.NextAttemptUtc <= now)
                && (row.ClaimedUtc == null || row.ClaimedUtc < abandoned))
            .OrderBy(row => row.CreatedUtc)
            .Take(BatchSize)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.ClaimedUtc, now)
                    .SetProperty(row => row.ClaimedBy, _who),
                ct)
            .ConfigureAwait(false);

        if (claimed == 0) return;

        var mine = await db.CommunicationLogs
            .IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId
                && row.ClaimedBy == _who
                && row.ClaimedUtc == now
                && row.Status == CommunicationStatus.Pending)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // One scope per clinic, with that clinic's tenant set — everything the senders read
        // below (the mail account, the SMS gateway, the sending address) is per clinic.
        using var scope = _scopes.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(tenantId);

        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsSender>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationSettingsService>();

        NotificationSettings? settings = null;

        foreach (var row in mine)
        {
            if (ct.IsCancellationRequested) break;

            row.Attempts++;

            try
            {
                string? failure;

                if (row.Channel == CommunicationChannel.Sms)
                {
                    var result = await sms
                        .SendAsync(row.Recipient, row.Body, ct,
                            purpose: row.Purpose.ToString(), patientId: row.PatientId)
                        .ConfigureAwait(false);

                    failure = result.Succeeded ? null : result.Detail;
                }
                else
                {
                    // Read once per clinic per pass, not once per message: it is the same
                    // answer for every row in this batch.
                    settings ??= await notifications.GetAsync(ct).ConfigureAwait(false);

                    var result = await email
                        .SendAsync(settings, row.Recipient ?? string.Empty,
                            row.Subject ?? string.Empty, row.Body, ct,
                            purpose: row.Purpose.ToString(), patientId: row.PatientId)
                        .ConfigureAwait(false);

                    failure = result.Succeeded ? null : result.Detail;
                }

                Settle(row, failure, now);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A thrown sender is a failed attempt, not a dead drainer. The row keeps its
                // place and the reason, and the next pass tries the rest of the batch.
                Settle(row, ex.Message, now);

                _log.LogWarning(
                    ex, "Sending {Channel} message {Id} threw.", row.Channel, row.Id);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Records the outcome, and decides whether there will be another attempt.</summary>
    private static void Settle(CommunicationLog row, string? failure, DateTime now)
    {
        row.ClaimedUtc = null;
        row.ClaimedBy = null;
        row.UpdatedUtc = now;

        if (failure is null)
        {
            row.Status = CommunicationStatus.Sent;
            row.SentUtc = now;
            row.NextAttemptUtc = null;

            return;
        }

        row.FailureReason = failure;

        if (row.Attempts >= MaximumAttempts)
        {
            // Given up on, and said so. A message that quietly stays Pending forever is
            // worse than one marked Failed, because nobody goes looking for it.
            row.Status = CommunicationStatus.Failed;
            row.NextAttemptUtc = null;

            return;
        }

        // Exponential, so a gateway that is briefly down is not hammered by every pending
        // message at once — which is how a short outage turns into a rate-limit ban.
        row.NextAttemptUtc = now.AddSeconds(30 * Math.Pow(2, row.Attempts - 1));
    }
}

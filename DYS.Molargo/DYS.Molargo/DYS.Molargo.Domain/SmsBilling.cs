using System.Linq.Expressions;
using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain;

/// <summary>
/// Which messages count as having been sent, for every place that counts them.
/// </summary>
/// <remarks>
/// <para>
/// One definition, because there are now three screens that answer "how many texts" — the
/// practice's plan, the vendor's per-country usage, and the credit check that decides
/// whether the next one may go — and three of them disagreeing is three different numbers
/// for one question. The disagreement would show up as a practice being charged for a
/// message the vendor's own total did not include.
/// </para>
/// <para>
/// An expression rather than a method, so EF can translate it into the query instead of
/// every caller loading the table and filtering in memory.
/// </para>
/// </remarks>
public static class SmsBilling
{
    /// <summary>
    /// A text that actually left.
    /// </summary>
    /// <remarks>
    /// Sent, delivered or replied to. Pending never went, failed never arrived, and
    /// suppressed was deliberately held back — counting any of those would charge a
    /// practice for a message nobody received, and would spend credit the carrier never
    /// billed for.
    /// </remarks>
    public static Expression<Func<CommunicationLog, bool>> Billable => message =>
        !message.IsDeleted
        && message.Channel == CommunicationChannel.Sms
        && (message.Status == CommunicationStatus.Sent
            || message.Status == CommunicationStatus.Delivered
            || message.Status == CommunicationStatus.Responded);

    /// <summary>The same rule, for a row already in hand.</summary>
    public static bool IsBillable(CommunicationLog message) =>
        !message.IsDeleted
        && message.Channel == CommunicationChannel.Sms
        && message.Status is CommunicationStatus.Sent
            or CommunicationStatus.Delivered
            or CommunicationStatus.Responded;

    /// <summary>
    /// The first instant of the month a date falls in, in UTC.
    /// </summary>
    /// <remarks>
    /// The calendar month, because that is the period the vendor bills — see the charge
    /// run, which raises one charge per month starting on the first. A counter on any other
    /// boundary would not reconcile with the invoice it is meant to explain.
    /// </remarks>
    public static DateTime MonthStart(DateOnly today) =>
        new(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
}

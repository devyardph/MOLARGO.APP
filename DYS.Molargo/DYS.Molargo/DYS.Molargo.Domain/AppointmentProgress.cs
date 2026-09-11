using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Domain;

/// <summary>
/// Where a status sits in the day's real progression.
/// </summary>
/// <remarks>
/// <see cref="AppointmentStatus"/> cannot answer this itself: <c>Seated</c> is numbered 7
/// although it belongs between arrived and in-chair, because the values are persisted and
/// renumbering would reinterpret every stored row. Anything ordering or comparing statuses
/// goes through here instead of sorting on the enum value.
/// </remarks>
public static class AppointmentProgress
{
    /// <summary>
    /// How far through the visit, from 0 (booked) to 5 (finished). Cancellations and
    /// no-shows return -1: they are not points on the progression, and giving them a rank
    /// would sort them into the middle of the day's flow.
    /// </summary>
    public static int Rank(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => 0,
        AppointmentStatus.Confirmed => 1,
        AppointmentStatus.CheckedIn => 2,
        AppointmentStatus.Seated => 3,
        AppointmentStatus.InProgress => 4,
        AppointmentStatus.Completed => 5,
        _ => -1,
    };

    /// <summary>Whether the appointment is still expected to happen, or is happening.</summary>
    public static bool IsLive(AppointmentStatus status) =>
        Rank(status) is >= 0 and < 5;

    /// <summary>
    /// Whether the slot was lost — cancelled or missed.
    /// </summary>
    /// <remarks>
    /// The distinction the diary needs: a lost slot keeps its place on the record but must
    /// not occupy the chair, because the chair is free and re-sellable.
    /// </remarks>
    public static bool IsLostSlot(AppointmentStatus status) => status is
        AppointmentStatus.Cancelled or AppointmentStatus.FailedToAttend;

    /// <summary>Whether the patient is in the building.</summary>
    public static bool IsPresent(AppointmentStatus status) =>
        Rank(status) is >= 2 and <= 4;

    /// <summary>
    /// Whether the appointment is still waiting for the patient to walk in.
    /// </summary>
    /// <remarks>
    /// Not the same as "is not present", which is what the diary first tested and which is
    /// also true of a finished visit — so "Mark arrived" was offered on a completed
    /// appointment and walked it back to arrived, destroying the completion and its
    /// timestamp. Only a booking that has not yet reached the door can arrive.
    /// </remarks>
    public static bool IsAwaitingArrival(AppointmentStatus status) => Rank(status) is 0 or 1;
}

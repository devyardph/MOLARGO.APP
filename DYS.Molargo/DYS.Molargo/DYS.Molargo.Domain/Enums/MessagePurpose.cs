namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// What a message is for. Drives which consent applies: a patient may opt out of
/// marketing while still needing appointment reminders, and conflating the two is how a
/// practice ends up in breach of the Spam Act.
/// </summary>
public enum MessagePurpose
{
    /// <summary>Appointment reminder or confirmation. Operational, not marketing.</summary>
    AppointmentReminder = 0,

    /// <summary>Recall notice.</summary>
    Recall = 1,

    /// <summary>Post-treatment care instructions.</summary>
    Aftercare = 2,

    /// <summary>Account or payment notice.</summary>
    AccountNotice = 3,

    /// <summary>Treatment plan or estimate follow-up.</summary>
    TreatmentFollowUp = 4,

    /// <summary>Promotional. Requires marketing consent.</summary>
    Marketing = 5,

    /// <summary>
    /// A security notice to a member of staff — their password was reset, say.
    /// </summary>
    /// <remarks>
    /// Its own purpose rather than <see cref="AccountNotice"/>, which is a patient's
    /// account. This goes to staff, so no patient consent applies to it and none should be
    /// consulted: a clinician who opted out of marketing still has to be told that somebody
    /// changed their password. Filing it under a patient purpose is how a consent check
    /// would one day silently suppress it.
    /// </remarks>
    SecurityNotice = 7,

    /// <summary>Anything else, logged by hand.</summary>
    General = 6,
}

namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// What causes a template to be sent.
/// </summary>
/// <remarks>
/// On the template rather than on a separate rule entity: a practice does not maintain
/// "the 48-hour reminder rule" and "the 48-hour reminder wording" as two things it can get
/// out of step. One row is the message and the occasion for it.
/// </remarks>
public enum MessageTrigger
{
    /// <summary>Sent by hand, from the patient's record or a campaign.</summary>
    Manual = 0,

    /// <summary>A set number of hours before an appointment.</summary>
    BeforeAppointment = 1,

    /// <summary>A set number of hours after an appointment — a post-op check.</summary>
    AfterAppointment = 2,

    /// <summary>When a recall falls due.</summary>
    RecallDue = 3,

    /// <summary>On an overdue account.</summary>
    AccountOverdue = 4,

    /// <summary>On the patient's birthday.</summary>
    Birthday = 5,

    /// <summary>After a completed visit, asking for a review.</summary>
    ReviewRequest = 6,

    /// <summary>
    /// When an administrator resets a member of staff's password.
    /// </summary>
    /// <remarks>
    /// The only trigger here that fires for staff rather than for a patient, and the only
    /// one the app actually acts on today — every other value describes an occasion no
    /// scheduler is watching for yet. Admin → Users sends this one at the moment it
    /// happens, which is why it needs no scheduler.
    /// </remarks>
    PasswordReset = 7,

    /// <summary>
    /// When somebody asks for a reset link from the sign-in screen.
    /// </summary>
    /// <remarks>
    /// A separate occasion from <see cref="PasswordReset"/>, and separate wording: that one
    /// tells somebody their password has already been changed by an administrator, this one
    /// hands them a link to change it themselves. One template covering both would have to
    /// be vague about which happened, and "your password was reset" is alarming when it
    /// was not.
    /// </remarks>
    PasswordResetRequested = 8,

    /// <summary>
    /// When somebody with two-step sign-in enters a correct password.
    /// </summary>
    /// <remarks>
    /// Its own occasion rather than wording borrowed from the reset codes, because the two
    /// ask the reader for opposite things. A reset code says "if this was not you, ignore
    /// it" — the code lapses and nothing has changed. This one says the opposite: if it was
    /// not you, somebody has your password right now and the account is one email away from
    /// being theirs. A template vague enough to cover both would have to drop the sentence
    /// that matters.
    ///
    /// Like <see cref="PasswordReset"/>, this fires at the moment it happens and needs no
    /// scheduler watching for it.
    /// </remarks>
    SignInCode = 9,
}

namespace DYS.Molargo.Shared.Features.Patient.Services;

/// <summary>One answer being submitted.</summary>
/// <param name="YesNo">Null means unanswered, which is not the same as "no".</param>
/// <param name="Detail">What the patient listed, where the question asked.</param>
public sealed record MedicalHistoryAnswerInput(string Code, bool? YesNo, string? Detail);

/// <summary>
/// A completed questionnaire on its way to being recorded.
/// </summary>
/// <param name="SignedByName">
/// Who signed. Not always the patient — a parent for a child, a guardian under a power of
/// attorney — so it is captured rather than assumed.
/// </param>
/// <param name="SignedByRelationship">Their relationship to the patient.</param>
/// <param name="AdditionalNotes">Anything the patient added beyond the fixed questions.</param>
public sealed record MedicalHistorySubmission(
    Guid PatientId,
    IReadOnlyList<MedicalHistoryAnswerInput> Answers,
    string SignedByName,
    string? SignedByRelationship,
    string? AdditionalNotes,
    Guid? AppointmentId = null);

/// <summary>
/// A patient's answer that contradicts what is already on their record.
/// </summary>
/// <remarks>
/// These exist because a "no" must never delete an alert. A patient saying they are not
/// on warfarin when the record says they are is exactly the situation a clinician has to
/// look at — they may have stopped, or they may have forgotten, and the consequences of
/// the two are not symmetrical. So the alert stays and the discrepancy is reported.
/// </remarks>
/// <param name="Question">The question they answered "no" to.</param>
/// <param name="ExistingAlert">What the record still says.</param>
public sealed record MedicalHistoryDiscrepancy(string Question, string ExistingAlert);

/// <summary>What recording a questionnaire did.</summary>
/// <param name="FormId">The stored form, for opening it later.</param>
/// <param name="AlertsAdded">New alerts created from "yes" answers.</param>
/// <param name="NeedsReview">
/// Answers that contradict an existing alert. Nothing was removed; a clinician has to
/// decide.
/// </param>
/// <param name="NextDueOn">When the next re-confirmation falls due.</param>
public sealed record MedicalHistoryResult(
    Guid FormId,
    int AlertsAdded,
    IReadOnlyList<MedicalHistoryDiscrepancy> NeedsReview,
    DateOnly NextDueOn);

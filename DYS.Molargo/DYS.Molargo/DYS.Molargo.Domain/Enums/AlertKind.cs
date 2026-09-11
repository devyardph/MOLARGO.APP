namespace DYS.Molargo.Domain.Enums;

/// <summary>
/// What kind of warning a patient alert carries. The kind decides where it is surfaced:
/// an allergy has to interrupt prescribing, a payment note only needs to reach the front
/// desk.
/// </summary>
public enum AlertKind
{
    /// <summary>Drug, material or latex allergy. Blocks prescribing without acknowledgement.</summary>
    Allergy = 0,

    /// <summary>A diagnosed condition bearing on treatment — diabetes, endocarditis risk.</summary>
    MedicalCondition = 1,

    /// <summary>Current medication, including anticoagulants and bisphosphonates.</summary>
    Medication = 2,

    /// <summary>Pregnancy, which changes radiography and prescribing.</summary>
    Pregnancy = 3,

    /// <summary>Behavioural or access note — needs a ground-floor chair, severe gag reflex.</summary>
    CareNote = 4,

    /// <summary>Account or payment warning for the front desk. Never shown to the patient.</summary>
    Financial = 5,
}

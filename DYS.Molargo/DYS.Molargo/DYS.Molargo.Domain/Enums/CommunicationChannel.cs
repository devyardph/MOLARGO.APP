namespace DYS.Molargo.Domain.Enums;

/// <summary>How a message reached, or left, the patient.</summary>
public enum CommunicationChannel
{
    Sms = 0,
    Email = 1,

    /// <summary>A phone call, logged by hand by whoever made or took it.</summary>
    Phone = 2,

    /// <summary>Posted letter.</summary>
    Letter = 3,

    /// <summary>A message through the patient app or portal.</summary>
    PatientPortal = 4,

    /// <summary>Spoken to at the front desk.</summary>
    InPerson = 5,
}

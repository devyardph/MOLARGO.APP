namespace DYS.Molargo.Domain.Enums;

/// <summary>Where an invoice sits financially.</summary>
public enum InvoiceStatus
{
    /// <summary>Being assembled. Not yet the patient's liability.</summary>
    Draft = 0,

    Issued = 1,

    /// <summary>Some payment received, a balance outstanding.</summary>
    PartiallyPaid = 2,

    Paid = 3,

    /// <summary>Issued past its terms with a balance outstanding.</summary>
    Overdue = 4,

    /// <summary>Cancelled after issue. Reversed rather than deleted, for the audit trail.</summary>
    Voided = 5,

    /// <summary>Written off as unrecoverable. Still on the ledger.</summary>
    WrittenOff = 6,
}

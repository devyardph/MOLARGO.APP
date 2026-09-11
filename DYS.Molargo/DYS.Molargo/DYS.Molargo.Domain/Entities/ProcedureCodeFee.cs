namespace DYS.Molargo.Domain.Entities;

/// <summary>
/// One site's price for one catalogue item, where that site charges something other than
/// the practice's fee.
/// </summary>
/// <remarks>
/// <para>
/// Sparse on purpose: a row exists only where a site actually differs. The alternative — a
/// fee per item per site, filled in for every combination — turns a 600-item ADA schedule
/// at three sites into 1,800 numbers to keep in step, and a schedule-wide price rise into
/// 1,800 edits. Here it stays 600 numbers plus a short list of deliberate exceptions, and
/// the exception list is readable on its own: "Newtown charges $30 more for 011" is the
/// whole row.
/// </para>
/// <para>
/// The cost is that the price of an item is in two places, so no screen may show one
/// without the other. The item editor lists every site beside the practice fee for exactly
/// that reason, and a site with no row says "practice fee" rather than showing a blank.
/// </para>
/// <para>
/// Per-clinic pricing needs nothing here. <see cref="EntityBase.TenantId"/> already scopes
/// every catalogue item to the clinic that owns it, so two practices on the platform have
/// separate schedules by construction; this entity is only about sites within one practice.
/// </para>
/// <para>
/// Not a price history. The fee charged is copied onto the invoice line when the line is
/// raised — see <c>InvoiceLine.UnitFee</c> — so changing a row here never restates an
/// invoice that has already gone out. What this entity cannot answer is "what did we charge
/// at Newtown last March": that needs a dated schedule, and the invoices are the record
/// until there is one.
/// </para>
/// </remarks>
public sealed class ProcedureCodeFee : EntityBase
{
    public Guid ProcedureCodeId { get; set; }

    public Guid PracticeLocationId { get; set; }

    /// <summary>
    /// This site's fee. Overrides <see cref="ProcedureCode.Fee"/> outright rather than
    /// adjusting it by a percentage or a margin: a stored "+10%" re-prices itself every
    /// time the practice fee moves, which is a surprise on the screen a practice quotes
    /// from. A site loading is entered once as a number and stays that number.
    /// </summary>
    public decimal Fee { get; set; }
}

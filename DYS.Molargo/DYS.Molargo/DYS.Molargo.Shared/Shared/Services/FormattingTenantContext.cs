using DYS.Molargo.Shared.Components;

namespace DYS.Molargo.Shared.Services;

/// <summary>
/// The heads' tenant context: the clinic, plus the currency every figure is rendered in.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MolargoFormat"/> is static — every money figure in the app goes through it,
/// and a static cannot take a dependency. Pushing the resolved currency into it is safe
/// here and only here: a head is one process serving one signed-in person, so there is
/// exactly one right answer at a time.
/// </para>
/// <para>
/// It is not safe in the API, which is why the base class no longer does it. There, two
/// requests for two clinics share a process, and whichever resolved last would decide how
/// the other's invoices were rendered. The base class holds the state; this subclass is
/// the part that only makes sense when the process belongs to one person.
/// </para>
/// </remarks>
public sealed class FormattingTenantContext : TenantContext
{
    public override void Use(
        Guid tenantId, string? tenantName = null, string? currencyCode = null)
    {
        base.Use(tenantId, tenantName, currencyCode);

        // After the base call, not before: CurrencyCode is only updated where the incoming
        // code is one the app knows, so reading it back is what keeps an unrecognised code
        // from blanking a good one.
        MolargoFormat.UseCurrency(CurrencyCode);
    }
}

using Microsoft.AspNetCore.Components;

namespace DYS.Molargo.Shared.Services;

/// <summary>
/// Navigation as a view model sees it.
///
/// MvvmCross's own <c>IMvxNavigationService</c> resolves native views by type, which means
/// nothing in a Razor host, so intent is expressed here instead and the host routes it.
/// Named methods rather than raw URLs keep route strings out of view models: changing a
/// route is then one edit in <see cref="AppNavigator"/> instead of a grep across every
/// view model that linked to it.
/// </summary>
public interface IAppNavigator
{
    void ToHome();

    void ToPatientList();

    /// <summary>The appointments diary.</summary>
    void ToDiary();

    /// <summary>Prescriptions, referral letters and certificates.</summary>
    void ToPrescribing();

    /// <summary>Invoices, payments, claims and debtors.</summary>
    void ToBilling();

    /// <summary>
    /// The invoice builder — catalogue on the left, the invoice on the right.
    /// </summary>
    /// <remarks>
    /// A screen rather than a pane on the billing list, because building an invoice and
    /// reading a day's takings are different jobs that were sharing one column.
    /// </remarks>
    void ToInvoice(Guid invoiceId);

    /// <summary>Stock, purchase orders, lab cases and sterilisation.</summary>
    void ToInventory();

    /// <summary>Staff, sites, registrations, the audit trail and the local database.</summary>
    void ToAdmin();

    /// <summary>Production, collections, attendance and the procedure mix.</summary>
    void ToReports();

    /// <summary>Templates, campaigns, the portal inbox and communication consent.</summary>
    void ToComms();

    /// <summary>The stock-item form, empty.</summary>
    void ToNewStockItem();

    /// <summary>The stock-item form for an existing item.</summary>
    void ToStockItem(Guid id);

    /// <summary>
    /// The booking form for a new appointment, optionally aimed at a diary slot or
    /// started from a patient's record.
    /// </summary>
    /// <remarks>
    /// The slot travels as a query string rather than as route segments. It is optional
    /// context — "New appointment" in the header carries none — and a route with four
    /// optional segments needs sixteen route templates to express.
    /// </remarks>
    /// <param name="patientId">
    /// Who the booking is for, where it was started from their record. The name is not
    /// carried with it: the form reads it back from the id, so an edited URL cannot put
    /// one person's name above another's booking.
    /// </param>
    /// <param name="planId">
    /// The treatment plan a visit is being booked from, with <paramref name="visit"/> naming
    /// which visit. Only the ids travel; the form reads the rest back from the plan.
    /// </param>
    void ToNewAppointment(
        DateOnly? date = null,
        TimeOnly? time = null,
        Guid? operatoryId = null,
        Guid? patientId = null,
        Guid? planId = null,
        int? visit = null);

    /// <summary>The booking form for an existing appointment.</summary>
    void ToAppointment(Guid id);

    /// <summary>
    /// A patient's record, optionally opened on a named tab.
    /// </summary>
    /// <param name="tab">
    /// The tab's own name — "documents", "medical-history". A string rather than the tab
    /// enum, which lives in the patient feature: this navigator is used by every feature
    /// and taking one feature's type here would make the others depend on it.
    /// </param>
    void ToPatientRecord(Guid id, string? tab = null);

    void ToNewPatient();

    void ToPatientEdit(Guid id);

    /// <summary>The patient-facing medical-history questionnaire.</summary>
    void ToMedicalHistory(Guid id);

    /// <summary>
    /// The patient-facing consent form.
    /// </summary>
    /// <param name="consentId">
    /// A form already raised, to be signed. Null raises a new one, which means the screen
    /// opens on the clinician's half instead of the patient's.
    /// </param>
    void ToConsent(Guid patientId, Guid? consentId = null);

    /// <summary>The clinical chart.</summary>
    void ToChart(Guid id);

    /// <summary>
    /// The treatment-plan builder — a new plan for the patient, or an existing one.
    /// </summary>
    /// <remarks>
    /// The patient is in the route even when editing an existing plan, so "back to record"
    /// works without a read and the URL says who the plan is for. A plan id alone would
    /// make the address unreadable and the back button a database call.
    /// </remarks>
    void ToPlanBuilder(Guid patientId, Guid planId = default);

    /// <summary>The chairside presentation for one plan.</summary>
    void ToPlanPresentation(Guid planId);

    /// <summary>
    /// Escape hatch for a destination with no named method yet. Prefer adding a method —
    /// a literal URL at a call site is exactly what this interface exists to prevent.
    /// </summary>
    void To(string relativeUri);

    void Back();
}

/// <summary>
/// Routes navigation intent through Blazor's <see cref="NavigationManager"/>. One
/// implementation serves both heads: the MAUI BlazorWebView and the Blazor Server circuit
/// each supply their own <see cref="NavigationManager"/>, so nothing here is
/// head-specific.
/// </summary>
public sealed class AppNavigator : IAppNavigator
{
    private readonly NavigationManager _navigation;

    public AppNavigator(NavigationManager navigation) => _navigation = navigation;

    public void ToHome() => _navigation.NavigateTo("/");

    public void ToPatientList() => _navigation.NavigateTo("/patients");

    public void ToDiary() => _navigation.NavigateTo("/diary");

    public void ToPrescribing() => _navigation.NavigateTo("/rx");

    public void ToBilling() => _navigation.NavigateTo("/billing");

    public void ToInvoice(Guid invoiceId) =>
        _navigation.NavigateTo($"/billing/invoices/{invoiceId}");

    public void ToInventory() => _navigation.NavigateTo("/inventory");

    public void ToComms() => _navigation.NavigateTo("/comms");

    public void ToReports() => _navigation.NavigateTo("/reports");

    public void ToAdmin() => _navigation.NavigateTo("/admin");

    public void ToNewStockItem() => _navigation.NavigateTo("/inventory/stock/new");

    public void ToStockItem(Guid id) => _navigation.NavigateTo($"/inventory/stock/{id}");

    public void ToNewAppointment(
        DateOnly? date = null,
        TimeOnly? time = null,
        Guid? operatoryId = null,
        Guid? patientId = null,
        Guid? planId = null,
        int? visit = null)
    {
        var query = new List<string>();

        // ISO in the URL, whatever the form displays. A URL is not read by a clinician,
        // and dd/MM/yyyy in a query string is ambiguous to everything that parses it.
        if (date is { } day) query.Add($"date={day:yyyy-MM-dd}");
        if (time is { } at) query.Add($"time={at:HH:mm}");
        if (operatoryId is { } chair) query.Add($"chair={chair}");
        if (patientId is { } patient && patient != Guid.Empty) query.Add($"patient={patient}");

        // Both or neither: a visit number without a plan names nothing, and a plan without
        // a visit would have the form guessing which one of three was meant.
        if (planId is { } plan && plan != Guid.Empty && visit is > 0)
        {
            query.Add($"plan={plan}");
            query.Add($"visit={visit}");
        }

        var suffix = query.Count == 0 ? string.Empty : "?" + string.Join("&", query);

        _navigation.NavigateTo($"/diary/appointments/new{suffix}");
    }

    public void ToAppointment(Guid id) => _navigation.NavigateTo($"/diary/appointments/{id}");

    public void ToPatientRecord(Guid id, string? tab = null) =>
        _navigation.NavigateTo(
            tab is { Length: > 0 }
                ? $"/patients/{id}?tab={Uri.EscapeDataString(tab)}"
                : $"/patients/{id}");

    public void ToNewPatient() => _navigation.NavigateTo("/patients/new");

    public void ToPatientEdit(Guid id) => _navigation.NavigateTo($"/patients/{id}/edit");

    public void ToMedicalHistory(Guid id) => _navigation.NavigateTo($"/patients/{id}/medical-history");

    public void ToConsent(Guid patientId, Guid? consentId = null) =>
        _navigation.NavigateTo(
            consentId is { } id
                ? $"/patients/{patientId}/consent/{id}"
                : $"/patients/{patientId}/consent");

    public void ToChart(Guid id) => _navigation.NavigateTo($"/patients/{id}/chart");

    public void ToPlanBuilder(Guid patientId, Guid planId = default) =>
        _navigation.NavigateTo(planId == Guid.Empty
            ? $"/patients/{patientId}/plans/new"
            : $"/patients/{patientId}/plans/{planId}");

    public void ToPlanPresentation(Guid planId) =>
        _navigation.NavigateTo($"/plans/{planId}/present");

    public void To(string relativeUri) => _navigation.NavigateTo(relativeUri);

    /// <summary>
    /// There is no history stack to pop without JS interop, and interop is unavailable
    /// during prerender on the web head. Sending the user to the list is the honest
    /// approximation: every "back" in this app is from a record to the list it came from.
    /// </summary>
    public void Back() => ToPatientList();
}

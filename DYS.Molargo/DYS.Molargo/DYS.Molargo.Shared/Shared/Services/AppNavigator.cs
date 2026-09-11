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
    /// The booking form for a new appointment, optionally aimed at a diary slot.
    /// </summary>
    /// <remarks>
    /// The slot travels as a query string rather than as route segments. It is optional
    /// context — "New appointment" in the header carries none — and a route with three
    /// optional segments needs four route templates to express.
    /// </remarks>
    void ToNewAppointment(DateOnly? date = null, TimeOnly? time = null, Guid? operatoryId = null);

    /// <summary>The booking form for an existing appointment.</summary>
    void ToAppointment(Guid id);

    void ToPatientRecord(Guid id);

    void ToNewPatient();

    void ToPatientEdit(Guid id);

    /// <summary>The patient-facing medical-history questionnaire.</summary>
    void ToMedicalHistory(Guid id);

    /// <summary>The clinical chart.</summary>
    void ToChart(Guid id);

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

    public void ToInventory() => _navigation.NavigateTo("/inventory");

    public void ToComms() => _navigation.NavigateTo("/comms");

    public void ToReports() => _navigation.NavigateTo("/reports");

    public void ToAdmin() => _navigation.NavigateTo("/admin");

    public void ToNewStockItem() => _navigation.NavigateTo("/inventory/stock/new");

    public void ToStockItem(Guid id) => _navigation.NavigateTo($"/inventory/stock/{id}");

    public void ToNewAppointment(
        DateOnly? date = null, TimeOnly? time = null, Guid? operatoryId = null)
    {
        var query = new List<string>();

        // ISO in the URL, whatever the form displays. A URL is not read by a clinician,
        // and dd/MM/yyyy in a query string is ambiguous to everything that parses it.
        if (date is { } day) query.Add($"date={day:yyyy-MM-dd}");
        if (time is { } at) query.Add($"time={at:HH:mm}");
        if (operatoryId is { } chair) query.Add($"chair={chair}");

        var suffix = query.Count == 0 ? string.Empty : "?" + string.Join("&", query);

        _navigation.NavigateTo($"/diary/appointments/new{suffix}");
    }

    public void ToAppointment(Guid id) => _navigation.NavigateTo($"/diary/appointments/{id}");

    public void ToPatientRecord(Guid id) => _navigation.NavigateTo($"/patients/{id}");

    public void ToNewPatient() => _navigation.NavigateTo("/patients/new");

    public void ToPatientEdit(Guid id) => _navigation.NavigateTo($"/patients/{id}/edit");

    public void ToMedicalHistory(Guid id) => _navigation.NavigateTo($"/patients/{id}/medical-history");

    public void ToChart(Guid id) => _navigation.NavigateTo($"/patients/{id}/chart");

    public void To(string relativeUri) => _navigation.NavigateTo(relativeUri);

    /// <summary>
    /// There is no history stack to pop without JS interop, and interop is unavailable
    /// during prerender on the web head. Sending the user to the list is the honest
    /// approximation: every "back" in this app is from a record to the list it came from.
    /// </summary>
    public void Back() => ToPatientList();
}

using DYS.Molargo.Domain.Enums;
using DYS.Molargo.Shared.Features.Inventory.Services;

namespace DYS.Molargo.Shared.Components;

/// <summary>
/// How the inventory screens name and colour stock, orders, lab cases and cycles.
/// </summary>
/// <remarks>
/// Whole literal class strings, never composed at runtime: Tailwind scans source text, so
/// a class built by concatenation is a class that never reaches the stylesheet.
/// </remarks>
public static class InventoryCss
{
    public static string StockFlagLabel(StockFlag flag) => flag switch
    {
        StockFlag.OutOfStock => "Out of stock",
        StockFlag.Expired => "Expired",
        StockFlag.Low => "Low",
        StockFlag.ExpiringSoon => "Expiring soon",
        _ => "OK",
    };

    /// <summary>
    /// A stock status chip.
    /// </summary>
    /// <remarks>
    /// Only the three that need doing something take the accent. "OK" is the normal case
    /// and the majority of rows, and colouring it would bury the handful that matter.
    /// </remarks>
    public static string StockFlagTag(StockFlag flag) => flag switch
    {
        StockFlag.OutOfStock or StockFlag.Expired => "tag tag-accent",
        StockFlag.Low => "tag tag-accent2",
        StockFlag.ExpiringSoon => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    public static string OrderStatusLabel(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "Draft",
        PurchaseOrderStatus.Ordered => "Sent",
        PurchaseOrderStatus.PartiallyReceived => "Part received",
        PurchaseOrderStatus.Received => "Received",
        PurchaseOrderStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static string OrderStatusTag(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "tag tag-accent2",
        PurchaseOrderStatus.Cancelled => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    public static string LabStatusLabel(LabCaseStatus status) => status switch
    {
        LabCaseStatus.Prepared => "Prepared",
        LabCaseStatus.SentToLab => "At lab",
        LabCaseStatus.InProduction => "In production",
        LabCaseStatus.Received => "Back — ready to fit",
        LabCaseStatus.Fitted => "Fitted",
        LabCaseStatus.Remake => "Remake",
        LabCaseStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static string LabStatusTag(LabCaseStatus status) => status switch
    {
        LabCaseStatus.Remake => "tag tag-accent",
        LabCaseStatus.Received => "tag tag-accent2",
        LabCaseStatus.Fitted or LabCaseStatus.Cancelled => "tag tag-outline",
        _ => "tag tag-neutral",
    };

    /// <summary>
    /// A lab case's due date, marked when it has passed and the case is still out.
    /// </summary>
    /// <remarks>
    /// The overdue test lives on the row rather than here, because it needs today's date
    /// and a CSS mapper has no business knowing what day it is.
    /// </remarks>
    public static string LabDueCell(bool isOverdue) => isOverdue
        ? "body-cell font-bold text-accent-800"
        : "body-cell";

    public static string CycleResultLabel(SterilisationResult result) => result switch
    {
        SterilisationResult.InProgress => "Running",
        SterilisationResult.Passed => "Passed",
        SterilisationResult.Failed => "Failed",
        SterilisationResult.Aborted => "Aborted",
        _ => result.ToString(),
    };

    /// <summary>
    /// A cycle result chip.
    /// </summary>
    /// <remarks>
    /// A failed or aborted load is the one row on this screen that must be impossible to
    /// skim past — its instruments are not sterile and are still in the practice.
    /// </remarks>
    public static string CycleResultTag(SterilisationResult result) => result switch
    {
        SterilisationResult.Failed or SterilisationResult.Aborted => "tag tag-accent",
        SterilisationResult.InProgress => "tag tag-outline",
        _ => "tag tag-neutral",
    };
}

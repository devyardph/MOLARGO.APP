using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Data;

/// <summary>
/// Suppliers, stock, purchase orders, lab cases and today's sterilisation cycles.
/// </summary>
/// <remarks>
/// <para>
/// Seeded to make the screen's own logic visible rather than to look tidy: something out
/// of stock, something low and orderable, something already on an open order, something
/// expiring inside the warning window, something already expired, a case overdue at the
/// lab, and a failed autoclave load whose tray has already been used on a patient. A stock
/// list where every row reads OK proves nothing about the code that decides it does not.
/// </para>
/// <para>
/// Every quantity arrives as a movement, never as a typed balance. <c>QuantityOnHand</c>
/// is the sum of the ledger, so seeding the figure directly would start the app with the
/// same drift <c>AlignPatientBalances</c> exists to undo for invoices.
/// </para>
/// </remarks>
internal static partial class SampleData
{
    private static IEnumerable<Supplier> Suppliers()
    {
        // The prototype's three, plus the laboratory its lab cases go to. The design lists
        // the merchants and the lab separately, and so does the entity — IsLaboratory,
        // because a lab is not somewhere you send a purchase order for gloves.
        yield return Supplier("schein", "Henry Schein", "consumables", "T. Nguyen",
            leadTimeDays: 3, account: "HS-40218");

        yield return Supplier("straumann", "Straumann", "implants · consignment stock on site",
            "M. Farrow", leadTimeDays: 5, account: "STR-9922");

        yield return Supplier("erskine", "Erskine Dental", "equipment & service",
            contact: null, leadTimeDays: 10, account: null);

        yield return Supplier("adl", "Aurora Dental Laboratory", "crown & bridge",
            "D. Petrakis", leadTimeDays: 7, account: "ADL-118", isLaboratory: true);
    }

    private static Supplier Supplier(
        string key,
        string name,
        string? notes,
        string? contact,
        int? leadTimeDays,
        string? account,
        bool isLaboratory = false) =>
        new()
        {
            Id = Id($"supplier:{key}"),
            Name = name,
            Notes = notes,
            ContactName = contact,
            LeadTimeDays = leadTimeDays,
            AccountNumber = account,
            IsLaboratory = isLaboratory,
            Email = $"orders@{key}.example",
        };

    /// <summary>
    /// The stock list, with the movements that put each figure where it is.
    /// </summary>
    private static (IEnumerable<StockItem> Items, IEnumerable<StockMovement> Movements)
        Stock(DateOnly today)
    {
        var items = new List<StockItem>();
        var movements = new List<StockMovement>();

        void Item(
            string key,
            string name,
            string category,
            string supplierKey,
            string unit,
            decimal reorderAt,
            decimal reorderQuantity,
            decimal cost,
            decimal onHand,
            bool batchTracked = false,
            string? batch = null,
            int? expiresInDays = null,
            int openedDaysAgo = 120,
            string? sku = null)
        {
            var id = Id($"stock:{key}");

            items.Add(new StockItem
            {
                Id = id,
                PracticeLocationId = SydneyCbd,
                Name = name,
                Category = category,
                SupplierId = Id($"supplier:{supplierKey}"),
                UnitOfMeasure = unit,
                ReorderLevel = reorderAt,
                ReorderQuantity = reorderQuantity,
                UnitCost = cost,
                RequiresBatchTracking = batchTracked,
                Sku = sku,

                // Set from the movements below rather than independently. Kept here so the
                // seeded rows are queryable before anything recalculates them, and equal
                // to the ledger by construction.
                QuantityOnHand = onHand,
                EarliestExpiry = expiresInDays is { } days ? today.AddDays(days) : null,
            });

            movements.Add(new StockMovement
            {
                Id = Id($"stock-move:{key}:opening"),
                StockItemId = id,
                Kind = StockMovementKind.OpeningBalance,
                QuantityChange = onHand,
                BalanceAfter = onHand,
                OccurredUtc = today.AddDays(-openedDaysAgo)
                    .ToDateTime(new TimeOnly(9, 0), DateTimeKind.Local).ToUniversalTime(),
                BatchNumber = batch,
                ExpiryDate = expiresInDays is { } expiry ? today.AddDays(expiry) : null,
                UnitCost = cost,
                RecordedByProviderId = Id("provider:brennan"),
                Notes = "Opening count",
            });
        }

        // Comfortable — the majority case, and the one the flag logic has to leave alone.
        // Opened at 10; the twelve received against PO-1041 below bring it to 22.
        Item("gloves-m", "Nitrile gloves, medium", "Infection control", "schein",
            "box of 100", reorderAt: 6m, reorderQuantity: 12m, cost: 14.50m, onHand: 10m,
            sku: "GLV-M");

        Item("bibs", "Patient bibs", "Infection control", "schein",
            "box of 500", reorderAt: 2m, reorderQuantity: 4m, cost: 38m, onHand: 5m);

        Item("burs-fg", "FG diamond burs, assorted", "Restorative", "schein",
            "pack of 25", reorderAt: 3m, reorderQuantity: 6m, cost: 62m, onHand: 8m);

        // Low and orderable: the row the reorder list exists for. No open order on it, so
        // Add to PO is offered.
        Item("composite-a2", "Composite, A2 shade", "Restorative", "schein",
            "syringe", reorderAt: 8m, reorderQuantity: 12m, cost: 32m, onHand: 6m,
            batchTracked: true, batch: "CMP-A2-2419", expiresInDays: 240,
            openedDaysAgo: 60);

        // Low as well, and already on an open order — the "On PO ✓" row, which is what
        // stops the same box being ordered twice while the first is in transit.
        Item("septanest", "Septanest 4% 1:100,000", "Anaesthetics", "schein",
            "box of 50", reorderAt: 4m, reorderQuantity: 8m, cost: 46m, onHand: 3m,
            batchTracked: true, batch: "SEP-88214", expiresInDays: 310,
            openedDaysAgo: 45, sku: "ANA-SEP4");

        // Expiring inside the sixty-day window but not low, so the flag is about the date
        // rather than the count.
        Item("etchant", "Etchant gel, 37% phosphoric", "Restorative", "schein",
            "syringe", reorderAt: 3m, reorderQuantity: 6m, cost: 18m, onHand: 7m,
            batchTracked: true, batch: "ETC-7120", expiresInDays: 34, openedDaysAgo: 200);

        // Already past its expiry, and still counted. Expired beats a healthy count: the
        // seven on the shelf cannot be used, and a row reading "OK · 7" would send someone
        // to fetch one.
        Item("hypochlorite", "Sodium hypochlorite 4%", "Endodontic", "schein",
            "500ml bottle", reorderAt: 2m, reorderQuantity: 4m, cost: 12m, onHand: 4m,
            batchTracked: true, batch: "NAOCL-3318", expiresInDays: -11,
            openedDaysAgo: 300);

        // Out of stock, and the worst flag on the list, so it sorts to the top.
        Item("gp-points", "Gutta percha points, 04 taper", "Endodontic", "schein",
            "box of 60", reorderAt: 2m, reorderQuantity: 4m, cost: 44m, onHand: 0m,
            openedDaysAgo: 260);

        // Implants: batch-tracked, from the implant supplier, and comfortable. Here so the
        // batch-tracking guard has a row where it matters most.
        Item("implant-41", "Straumann BLT implant 4.1 × 10mm", "Surgical", "straumann",
            "each", reorderAt: 2m, reorderQuantity: 3m, cost: 410m, onHand: 4m,
            batchTracked: true, batch: "STR-BLT-77341", expiresInDays: 700,
            openedDaysAgo: 90, sku: "IMP-BLT-4110");

        Item("impression", "Impression material, heavy body", "Impression", "schein",
            "cartridge", reorderAt: 4m, reorderQuantity: 8m, cost: 27m, onHand: 9m,
            batchTracked: true, batch: "IMP-5540", expiresInDays: 150, openedDaysAgo: 75);

        // The delivery behind PO-1041's received line.
        //
        // Seeded as a movement rather than folded into the opening count, because the
        // order says twelve boxes were booked in and the shelf figure has to be the reason
        // it says so. Without this the pane contradicts itself — a line reading "received
        // 12" against an item whose count never moved, which is precisely the drift the
        // ledger exists to prevent.
        movements.Add(new StockMovement
        {
            Id = Id("stock-move:gloves-m:po-1041"),
            StockItemId = Id("stock:gloves-m"),
            Kind = StockMovementKind.Received,
            QuantityChange = 12m,
            BalanceAfter = 22m,
            OccurredUtc = today.AddDays(-2)
                .ToDateTime(new TimeOnly(9, 40), DateTimeKind.Local).ToUniversalTime(),
            UnitCost = 14.50m,
            RecordedByProviderId = Id("provider:brennan"),
            Reference = "PO-1041",
        });

        var gloves = items.First(item => item.Id == Id("stock:gloves-m"));
        gloves.QuantityOnHand = 22m;

        // A little history on one item, so the movement panel on the item screen has
        // something to show and the balance is visibly a sum rather than a figure.
        var composite = Id("stock:composite-a2");
        var running = 12m;

        foreach (var (offset, change, kind) in new[]
        {
            (34, -2m, StockMovementKind.Consumed),
            (21, -3m, StockMovementKind.Consumed),
            (12, -1m, StockMovementKind.Wastage),
        })
        {
            running += change;

            movements.Add(new StockMovement
            {
                Id = Id($"stock-move:composite-a2:{offset}"),
                StockItemId = composite,
                Kind = kind,
                QuantityChange = change,
                BalanceAfter = running,
                OccurredUtc = today.AddDays(-offset)
                    .ToDateTime(new TimeOnly(14, 30), DateTimeKind.Local).ToUniversalTime(),
                BatchNumber = "CMP-A2-2419",
                ExpiryDate = today.AddDays(240),
                RecordedByProviderId = Id("provider:vance"),
            });
        }

        // The opening count has to be the figure the later movements start from, not the
        // figure they end at, or the ledger sums to something other than what is on the
        // shelf. Corrected here rather than by hand above so the two cannot drift.
        var opening = movements.First(movement => movement.Id == Id("stock-move:composite-a2:opening"));
        opening.QuantityChange = 12m;
        opening.BalanceAfter = 12m;

        return (items, movements);
    }

    /// <summary>
    /// One sent order part-received and one draft, so both halves of the pane have work.
    /// </summary>
    private static (IEnumerable<PurchaseOrder> Orders, IEnumerable<PurchaseOrderLine> Lines)
        PurchaseOrders(DateOnly today)
    {
        var orders = new List<PurchaseOrder>();
        var lines = new List<PurchaseOrderLine>();

        // Sent four days ago, due tomorrow, and one line already booked in short. Part
        // received is the normal case rather than the exception — a delivery matching the
        // order exactly is the one that never needs chasing.
        orders.Add(new PurchaseOrder
        {
            Id = Id("po:1041"),
            PracticeLocationId = SydneyCbd,
            SupplierId = Id("supplier:schein"),
            OrderNumber = "PO-1041",
            Status = PurchaseOrderStatus.PartiallyReceived,
            OrderedUtc = today.AddDays(-4)
                .ToDateTime(new TimeOnly(11, 20), DateTimeKind.Local).ToUniversalTime(),
            ExpectedOn = today.AddDays(1),
            RaisedByProviderId = Id("provider:brennan"),
        });

        // Received in full, and the movement behind it is seeded with the stock above, so
        // the count on the shelf is what this line says put it there.
        lines.Add(Line("1041-gloves", "po:1041", "stock:gloves-m",
            "Nitrile gloves, medium", ordered: 12m, received: 12m, cost: 14.50m));

        // Still outstanding, which is why Septanest reads "On PO ✓" on the stock list
        // rather than offering to be ordered again.
        lines.Add(Line("1041-septanest", "po:1041", "stock:septanest",
            "Septanest 4% 1:100,000", ordered: 8m, received: 0m, cost: 46m));

        lines.Add(Line("1041-gp", "po:1041", "stock:gp-points",
            "Gutta percha points, 04 taper", ordered: 4m, received: 0m, cost: 44m));

        // A draft with nothing on it yet would be invisible; a draft with a line is what
        // the Send button is for.
        orders.Add(new PurchaseOrder
        {
            Id = Id("po:draft-straumann"),
            PracticeLocationId = SydneyCbd,
            SupplierId = Id("supplier:straumann"),
            Status = PurchaseOrderStatus.Draft,
            RaisedByProviderId = Id("provider:vance"),
        });

        lines.Add(Line("draft-implant", "po:draft-straumann", "stock:implant-41",
            "Straumann BLT implant 4.1 × 10mm", ordered: 3m, received: 0m, cost: 410m));

        return (orders, lines);
    }

    private static PurchaseOrderLine Line(
        string key,
        string orderKey,
        string itemKey,
        string description,
        decimal ordered,
        decimal received,
        decimal cost,
        string? batch = null,
        DateOnly? expiry = null) =>
        new()
        {
            Id = Id($"po-line:{key}"),
            PurchaseOrderId = Id(orderKey),
            StockItemId = Id(itemKey),
            Description = description,
            QuantityOrdered = ordered,
            QuantityReceived = received,
            UnitCost = cost,
            BatchNumber = batch,
            ExpiryDate = expiry,
        };

    /// <summary>
    /// Lab cases, including the Yuen crown the front desk already has a task about.
    /// </summary>
    private static IEnumerable<LabCase> LabCases(DateOnly today)
    {
        // Margaret Yuen's crown: prepped, at the lab, due before the fit visit already in
        // her record. The front desk's "Call lab — Yuen crown due Wed" task is about this
        // case, and the two disagreeing is exactly what a shared record is meant to stop.
        yield return LabCase(today, "yuen-crown", "10201", "vance", "adl",
            "Crown, porcelain fused to zirconia", "46", LabCaseStatus.InProduction,
            sentDaysAgo: 6, dueInDays: 2, fee: 385m,
            specification: "A3 body, A2 incisal · chamfer margin 360°");

        // Overdue and still out: the row the due-date colouring and the overdue count are
        // for, and the one that costs a fit appointment if nobody chases it.
        yield return LabCase(today, "whitely-denture", "10208", "ellery", "adl",
            "Upper partial denture, acrylic", null, LabCaseStatus.SentToLab,
            sentDaysAgo: 19, dueInDays: -3, fee: 640m,
            specification: "Shade A2 · clasps 13, 23");

        // Back from the lab, so the fit visit is no longer blocked.
        yield return LabCase(today, "reyes-nightguard", "10205", "ellery", "adl",
            "Occlusal splint, hard acrylic", null, LabCaseStatus.Received,
            sentDaysAgo: 11, dueInDays: -1, fee: 295m, receivedDaysAgo: 1,
            specification: "Full coverage upper · 2mm");

        // A remake, which is the lab-quality signal the entity carries a reason for.
        yield return LabCase(today, "braddon-crown", "10206", "vance", "adl",
            "Crown, e.max — remake", "26", LabCaseStatus.Remake,
            sentDaysAgo: 3, dueInDays: 5, fee: 0m,
            specification: "Remake at lab's cost",
            remakeReason: "Margin open distally at try-in");

        // Fitted, so the list is not only work in flight — it sorts last.
        yield return LabCase(today, "nair-crown", "10207", "vance", "adl",
            "Crown, zirconia", "36", LabCaseStatus.Fitted,
            sentDaysAgo: 40, dueInDays: -26, fee: 385m, receivedDaysAgo: 27,
            fittedDaysAgo: 22, specification: "A2 · chamfer");
    }

    private static LabCase LabCase(
        DateOnly today,
        string key,
        string patientNumber,
        string providerKey,
        string labKey,
        string description,
        string? toothNumber,
        LabCaseStatus status,
        int sentDaysAgo,
        int dueInDays,
        decimal fee,
        string? specification,
        int? receivedDaysAgo = null,
        int? fittedDaysAgo = null,
        string? remakeReason = null) =>
        new()
        {
            Id = Id($"lab-case:{key}"),
            PatientId = Id($"patient:{patientNumber}"),
            ProviderId = Id($"provider:{providerKey}"),
            SupplierId = Id($"supplier:{labKey}"),
            Status = status,
            Description = description,
            ToothNumber = toothNumber,
            Specification = specification,
            LabReference = $"ADL-{key.ToUpperInvariant()}",
            SentUtc = today.AddDays(-sentDaysAgo)
                .ToDateTime(new TimeOnly(16, 0), DateTimeKind.Local).ToUniversalTime(),
            DueOn = today.AddDays(dueInDays),
            ReceivedUtc = receivedDaysAgo is { } back
                ? today.AddDays(-back)
                    .ToDateTime(new TimeOnly(10, 30), DateTimeKind.Local).ToUniversalTime()
                : null,
            FittedUtc = fittedDaysAgo is { } fitted
                ? today.AddDays(-fitted)
                    .ToDateTime(new TimeOnly(11, 15), DateTimeKind.Local).ToUniversalTime()
                : null,
            LabFee = fee == 0m ? null : fee,
            RemakeReason = remakeReason,
        };

    /// <summary>
    /// Today's autoclave cycles and the trays used out of them.
    /// </summary>
    private static (IEnumerable<SterilisationCycle> Cycles, IEnumerable<SterilisationCycleUse> Uses)
        Sterilisation(DateOnly today)
    {
        var cycles = new List<SterilisationCycle>();
        var uses = new List<SterilisationCycleUse>();

        // Passed and released, which is the ordinary morning.
        cycles.Add(Cycle("2214", "Autoclave A", 2214, today, new TimeOnly(7, 10),
            new TimeOnly(7, 48), SterilisationResult.Passed, chemical: true,
            biological: true, "Exam kits ×6, surgical kit ×1", releasedBy: "ito",
            releasedAt: new TimeOnly(7, 52)));

        // Passed but not signed off: the row the "awaiting release" count is for. Running
        // the cycle and taking responsibility for the load are separate acts, and this is
        // what the second one looks like before anyone does it.
        cycles.Add(Cycle("2215", "Autoclave A", 2215, today, new TimeOnly(10, 5),
            new TimeOnly(10, 43), SterilisationResult.Passed, chemical: true,
            biological: null, "Hygiene kits ×8", releasedBy: null, releasedAt: null));

        // Still running — nothing to release yet, and release refuses it.
        cycles.Add(Cycle("2216", "Autoclave A", 2216, today, new TimeOnly(13, 20),
            completed: null, SterilisationResult.InProgress, chemical: false,
            biological: null, "Restorative kits ×5", releasedBy: null, releasedAt: null));

        // Failed on the chemical indicator, and a tray from it has already been used. This
        // is the case the whole traceability panel exists for: the load is not sterile, the
        // patient is named, and release is refused outright.
        cycles.Add(Cycle("2189", "Autoclave B", 2189, today, new TimeOnly(8, 0),
            new TimeOnly(8, 36), SterilisationResult.Failed, chemical: false,
            biological: null, "Surgical kit ×2", releasedBy: null, releasedAt: null,
            failureNotes: "Chemical indicator did not turn. Load reprocessed; "
                + "unit taken out of service pending service call."));

        // Trays. The design's two, on the cycle it names, plus one from the failed load.
        uses.Add(Use("t118", "2214", "10201", "Tray T-118 (surgical kit)",
            today, new TimeOnly(8, 47)));

        uses.Add(Use("t104", "2214", "10204", "Tray T-104 (exam kit)",
            today, new TimeOnly(8, 2)));

        uses.Add(Use("t221", "2189", "10205", "Tray T-221 (surgical kit)",
            today, new TimeOnly(9, 35)));

        return (cycles, uses);
    }

    private static SterilisationCycle Cycle(
        string key,
        string unit,
        int number,
        DateOnly day,
        TimeOnly started,
        TimeOnly? completed,
        SterilisationResult result,
        bool chemical,
        bool? biological,
        string contents,
        string? releasedBy,
        TimeOnly? releasedAt,
        string? failureNotes = null) =>
        new()
        {
            Id = Id($"steri-cycle:{key}"),
            PracticeLocationId = SydneyCbd,
            SterilisorName = unit,
            CycleNumber = number,
            StartedUtc = day.ToDateTime(started, DateTimeKind.Local).ToUniversalTime(),
            CompletedUtc = completed is { } done
                ? day.ToDateTime(done, DateTimeKind.Local).ToUniversalTime()
                : null,
            Result = result,
            CycleType = "B",
            PeakTemperatureCelsius = result == SterilisationResult.InProgress ? null : 134m,
            HoldTimeMinutes = result == SterilisationResult.InProgress ? null : 3.5m,
            ChemicalIndicatorPassed = chemical,
            BiologicalIndicatorPassed = biological,
            LoadContents = contents,
            OperatedByProviderId = Id("provider:ito"),
            FailureNotes = failureNotes,
            ReleasedUtc = releasedAt is { } at
                ? day.ToDateTime(at, DateTimeKind.Local).ToUniversalTime()
                : null,
            ReleasedByProviderId = releasedBy is null ? null : Id($"provider:{releasedBy}"),
        };

    private static SterilisationCycleUse Use(
        string key,
        string cycleKey,
        string patientNumber,
        string pack,
        DateOnly day,
        TimeOnly at) =>
        new()
        {
            Id = Id($"steri-use:{key}"),
            SterilisationCycleId = Id($"steri-cycle:{cycleKey}"),
            PatientId = Id($"patient:{patientNumber}"),
            UsedUtc = day.ToDateTime(at, DateTimeKind.Local).ToUniversalTime(),
            PackIdentifier = pack,
            RecordedByProviderId = Id("provider:ito"),
        };
}

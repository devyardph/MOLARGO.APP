using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;
using PatientEntity = DYS.Molargo.Domain.Entities.Patient;

namespace DYS.Molargo.Shared.Data;

/// <summary>
/// A fully-populated clinical record for one patient — the prototype's Margaret Yuen.
/// </summary>
/// <remarks>
/// <para>
/// Depth on one record rather than a shallow row for all twenty-five. The record screen's
/// six tabs are only exercised by a patient who genuinely has conflicting allergies, a
/// presented plan awaiting acceptance, an invoice part-paid by a fund, and a comms history
/// with a bounce in it — and a patient with none of that hides every layout problem those
/// tabs have.
/// </para>
/// <para>
/// Everything is dated relative to the day the app first runs, so the record stays
/// internally consistent — the medical history is overdue, the crown fit is next week —
/// however long after this was written it is seeded.
/// </para>
/// </remarks>
internal static partial class SampleData
{
    /// <summary>The prototype's example patient: Margaret Yuen, #10201.</summary>
    private const string RecordPatientNumber = "10201";

    private static Guid RecordPatient => Id($"patient:{RecordPatientNumber}");

    private static void AddRecordFor(MolargoDbContext db, DateOnly today)
    {
        // Fund and Medicare details, set on the already-tracked patient rather than
        // widening the Patient factory for the one row that needs them. The record
        // screen's funding chip and the claims below both depend on these.
        var patient = db.ChangeTracker.Entries<PatientEntity>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(entity => entity.Id == RecordPatient);

        if (patient is not null)
        {
            patient.HealthFund = "HCF";
            patient.HealthFundMemberNumber = "90218843";
            patient.MedicareNumber = "2951 88421 1";
            patient.MedicareReferenceNumber = 2;
            patient.Sex = Sex.Female;
            patient.AddressLine = "2/14 Enmore Rd";
            patient.Suburb = "Newtown";
            patient.State = "NSW";
            patient.Postcode = "2042";
            patient.EmergencyContactName = "Kevin Yuen";
            patient.EmergencyContactRelationship = "Spouse";
            patient.EmergencyContactPhone = "0412 883 022";
        }

        db.ToothChartEntries.AddRange(ToothChart(today));
        db.PatientAlerts.AddRange(Alerts(today));
        db.MedicalHistoryForms.AddRange(MedicalHistory(today));
        db.MedicalHistoryAnswers.AddRange(MedicalHistoryAnswers());
        db.TreatmentPlans.AddRange(Plans(today));
        db.TreatmentPlanItems.AddRange(PlanItems());
        db.Appointments.AddRange(UpcomingAppointments(today));
        db.Recalls.AddRange(Recalls(today));

        db.FormularyMedicines.AddRange(Formulary());

        var (scripts, scriptItems, referrals, certificates) = Prescribing(today);
        db.Prescriptions.AddRange(scripts);
        db.PrescriptionItems.AddRange(scriptItems);
        db.Referrals.AddRange(referrals);
        db.MedicalCertificates.AddRange(certificates);

        var (perioExams, perioSites, perioTeeth) = Perio(today);
        db.PerioExams.AddRange(perioExams);
        db.PerioSiteReadings.AddRange(perioSites);
        db.PerioToothReadings.AddRange(perioTeeth);
        db.PatientDocuments.AddRange(Documents(today));
        db.ConsentForms.AddRange(Consents(today));
        db.CommunicationLogs.AddRange(Communications(today));
        db.Invoices.AddRange(Invoices(today));
        db.InvoiceLines.AddRange(InvoiceLines(today));
        db.Payments.AddRange(Payments(today));
        db.Claims.AddRange(Claims(today));

        var (practiceInvoices, practiceLines, practicePayments, practiceClaims) =
            PracticeBilling(today);
        db.Invoices.AddRange(practiceInvoices);
        db.InvoiceLines.AddRange(practiceLines);
        db.Payments.AddRange(practicePayments);
        db.Claims.AddRange(practiceClaims);

        db.Suppliers.AddRange(Suppliers());

        var (stockItems, stockMovements) = Stock(today);
        db.StockItems.AddRange(stockItems);
        db.StockMovements.AddRange(stockMovements);

        var (orders, orderLines) = PurchaseOrders(today);
        db.PurchaseOrders.AddRange(orders);
        db.PurchaseOrderLines.AddRange(orderLines);

        db.LabCases.AddRange(LabCases(today));

        var (cycles, cycleUses) = Sterilisation(today);
        db.SterilisationCycles.AddRange(cycles);
        db.SterilisationCycleUses.AddRange(cycleUses);

        db.MessageTemplates.AddRange(Templates());
        db.CommunicationLogs.AddRange(PortalMessages(today));

        AlignPatientBalances(db);
        AlignConsent(db, today);
        AlignReferralSources(db);
        AlignRegistrations(db, today);
    }

    /// <summary>
    /// Recomputes every patient's balance from the invoices just seeded.
    /// </summary>
    /// <remarks>
    /// <c>Patient.Balance</c> is a cache of what the invoices say, and the seed set it by
    /// hand — so once invoices were seeded for other patients the two disagreed, and the
    /// front desk reported $2,276 owing while the billing screen reported $2,700 from the
    /// same data. Derived here so the cache starts out true, the same way
    /// <c>BillingService</c> keeps it true afterwards.
    /// </remarks>
    private static void AlignPatientBalances(MolargoDbContext db)
    {
        var invoices = db.ChangeTracker.Entries<Invoice>()
            .Select(entry => entry.Entity)
            .Where(invoice => invoice.Status is not (InvoiceStatus.Voided
                or InvoiceStatus.WrittenOff or InvoiceStatus.Draft))
            .ToList();

        var payments = db.ChangeTracker.Entries<Payment>()
            .Select(entry => entry.Entity)
            .Where(payment => payment.InvoiceId is not null)
            .GroupBy(payment => payment.InvoiceId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(payment => payment.Amount));

        var owing = invoices
            .GroupBy(invoice => invoice.PatientId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(invoice =>
                    invoice.Total - payments.GetValueOrDefault(invoice.Id)));

        foreach (var entity in db.ChangeTracker.Entries<PatientEntity>()
            .Select(entry => entry.Entity))
        {
            entity.Balance = owing.GetValueOrDefault(entity.Id);
        }

        // The stored AmountPaid follows too, so the invoice list is right before anything
        // has been clicked.
        foreach (var invoice in db.ChangeTracker.Entries<Invoice>()
            .Select(entry => entry.Entity))
        {
            invoice.AmountPaid = payments.GetValueOrDefault(invoice.Id);
        }
    }

    // ---- tooth chart -----------------------------------------------------

    /// <summary>
    /// The prototype's charted mouth, as real findings.
    /// </summary>
    /// <remarks>
    /// The design encodes each tooth as one word — "filling", "rct", "missing". Those map
    /// onto conditions plus surfaces here, because a restoration without surfaces is not a
    /// finding a clinician can act on: "16 restored" says nothing about which cusp.
    /// </remarks>
    private static IEnumerable<ToothChartEntry> ToothChart(DateOnly today)
    {
        // Long-standing work, dated years back so the chart reads as history rather than
        // as this course's treatment.
        var old = today.AddYears(-4);
        var recent = today.AddDays(-19);

        yield return Tooth("18", ToothCondition.Missing, ToothSurface.None, old,
            "Extracted 2019, elsewhere.");

        yield return Tooth("38", ToothCondition.Missing, ToothSurface.None, old,
            "Extracted 2019, elsewhere.");

        yield return Tooth("16", ToothCondition.Restoration,
            ToothSurface.Mesial | ToothSurface.Occlusal, old, "Amalgam, sound margins.");

        yield return Tooth("25", ToothCondition.Restoration,
            ToothSurface.Occlusal, old, "Composite.");

        yield return Tooth("27", ToothCondition.Restoration,
            ToothSurface.Occlusal | ToothSurface.Distal, old, "Composite.");

        yield return Tooth("47", ToothCondition.Restoration,
            ToothSurface.Occlusal, old, "Amalgam.");

        yield return Tooth("36", ToothCondition.RootCanalTreated, ToothSurface.None,
            today.AddYears(-2), "RCT Nov 2024, cusp-coverage onlay. PA review due.");

        // The tooth the whole seeded record is about: cracked cusp, crown under way.
        yield return Tooth("46", ToothCondition.Fractured,
            ToothSurface.Mesial | ToothSurface.Occlusal | ToothSurface.Distal, recent,
            "Cracked disto-buccal cusp under a large restoration. Crown planned.");

        yield return Tooth("14", ToothCondition.Restoration,
            ToothSurface.Occlusal, recent, "Composite placed this course.");

        // Watched rather than treated — the finding that makes a recall worth keeping.
        yield return Tooth("26", ToothCondition.Watch, ToothSurface.Occlusal, recent,
            "Early occlusal demineralisation. Fluoride, review at recall.");

        yield return Tooth("33", ToothCondition.Periodontal, ToothSurface.None, recent,
            "2mm recession, stable since Feb.");
    }

    private static ToothChartEntry Tooth(
        string fdi,
        ToothCondition condition,
        ToothSurface surfaces,
        DateOnly observed,
        string detail) =>
        new()
        {
            Id = Id($"tooth:{fdi}"),
            PatientId = RecordPatient,
            ToothNumber = fdi,

            // FDI stored always, whatever notation the clinician views it in.
            Notation = ToothNotation.Fdi,
            Condition = condition,
            Surfaces = surfaces,
            Detail = detail,
            ObservedOn = observed,
            ChartedByProviderId = Id("provider:vance"),
        };

    // ---- alerts ----------------------------------------------------------

    /// <summary>
    /// The prototype's medical alerts. Two allergies and an anticoagulant are
    /// <see cref="AlertSeverity.Critical"/> because they have to interrupt prescribing;
    /// the controlled conditions are warnings, and the hypertension is context.
    /// </summary>
    private static IEnumerable<PatientAlert> Alerts(DateOnly today)
    {
        // Recorded at the last medical history, over a year ago — which is what makes the
        // re-confirmation prompt on the overview tab overdue.
        var recorded = today.AddMonths(-14);

        yield return Alert("penicillin", AlertKind.Allergy, AlertSeverity.Critical,
            "Penicillin", "Rash, confirmed 2019. Use clindamycin for prophylaxis.", recorded);

        yield return Alert("latex", AlertKind.Allergy, AlertSeverity.Critical,
            "Latex", "Use nitrile gloves and a latex-free dam.", recorded);

        yield return Alert("warfarin", AlertKind.Medication, AlertSeverity.Critical,
            "Warfarin 5mg daily", "INR check required before any extraction.", recorded);

        yield return Alert("diabetes", AlertKind.MedicalCondition, AlertSeverity.Warning,
            "Type 2 diabetes", "Well controlled, HbA1c 6.8. Morning appointments preferred.", recorded);

        yield return Alert("hypertension", AlertKind.MedicalCondition, AlertSeverity.Information,
            "Hypertension", "Perindopril. Avoid adrenaline-heavy local anaesthetic.", recorded);
    }

    private static PatientAlert Alert(
        string key,
        AlertKind kind,
        AlertSeverity severity,
        string summary,
        string detail,
        DateOnly recorded) =>
        new()
        {
            Id = Id($"alert:{key}"),
            PatientId = RecordPatient,
            Kind = kind,
            Severity = severity,
            Summary = summary,
            Detail = detail,
            OnsetDate = recorded,
            RecordedByProviderId = Id("provider:vance"),
        };

    // ---- medical history -------------------------------------------------

    private static IEnumerable<MedicalHistoryForm> MedicalHistory(DateOnly today)
    {
        var completed = today.AddMonths(-14);

        yield return new MedicalHistoryForm
        {
            Id = Id("medical-history:1"),
            PatientId = RecordPatient,
            FormVersion = 1,
            CompletedUtc = completed.ToDateTime(new TimeOnly(9, 12), DateTimeKind.Local).ToUniversalTime(),
            SignedUtc = completed.ToDateTime(new TimeOnly(9, 14), DateTimeKind.Local).ToUniversalTime(),
            SignedByName = "Margaret Yuen",
            ReviewedByProviderId = Id("provider:vance"),
            ReviewedUtc = completed.ToDateTime(new TimeOnly(9, 30), DateTimeKind.Local).ToUniversalTime(),
            AdditionalNotes = "Prefers morning appointments. Daughter interprets if needed.",
        };
    }

    private static IEnumerable<MedicalHistoryAnswer> MedicalHistoryAnswers()
    {
        yield return Answer("allergies", "Do you have any allergies?", true, "Penicillin, latex");
        yield return Answer("anticoagulants", "Are you taking blood thinners?", true, "Warfarin 5mg daily");
        yield return Answer("diabetes", "Have you been diagnosed with diabetes?", true, "Type 2, diet and metformin");
        yield return Answer("heart", "Any heart conditions or high blood pressure?", true, "Hypertension");
        yield return Answer("pregnant", "Are you pregnant or breastfeeding?", false, null);

        // Deliberately left unanswered. A skipped question is not a "no", and the record
        // screen has to be able to show the difference.
        yield return Answer("smoking", "Do you smoke?", null, null);
    }

    private static MedicalHistoryAnswer Answer(string code, string question, bool? yesNo, string? detail) =>
        new()
        {
            Id = Id($"medical-answer:{code}"),
            MedicalHistoryFormId = Id("medical-history:1"),
            QuestionCode = code,
            QuestionText = question,
            YesNo = yesNo,
            Detail = detail,
        };

    // ---- treatment plans -------------------------------------------------

    private static IEnumerable<TreatmentPlan> Plans(DateOnly today)
    {
        yield return new TreatmentPlan
        {
            Id = Id("plan:crown-46"),
            PatientId = RecordPatient,
            ProviderId = Id("provider:vance"),
            PracticeLocationId = SydneyCbd,
            Title = "Tooth 46 — crown, 3 visits",
            Status = TreatmentPlanStatus.Presented,
            Rationale =
                "Cracked cusp with an existing large restoration. A crown restores the "
                + "cusp and protects the remaining tooth. Alternatives discussed: large "
                + "direct restoration (cheaper, shorter life) or extraction and implant.",
            PresentedUtc = today.AddDays(-19).ToDateTime(new TimeOnly(9, 40), DateTimeKind.Local).ToUniversalTime(),

            // An estimate stops being reliable once the fund's annual limits reset.
            EstimateValidUntil = today.AddMonths(2),
            QuotedTotal = 6850m,
            EstimatedBenefit = 1220m,
            IsRecommended = true,
            DisplayOrder = 0,
        };

        yield return new TreatmentPlan
        {
            Id = Id("plan:whitening"),
            PatientId = RecordPatient,
            ProviderId = Id("provider:vance"),
            PracticeLocationId = SydneyCbd,
            Title = "Whitening — take-home trays",
            Status = TreatmentPlanStatus.Draft,
            QuotedTotal = 650m,
            EstimatedBenefit = 0m,
            DisplayOrder = 1,
        };
    }

    private static IEnumerable<TreatmentPlanItem> PlanItems()
    {
        yield return PlanItem("crown-prep", "plan:crown-46", "613", "Full crown, veneered, indirect",
            "46", 1850m, 600m, TreatmentItemStatus.Completed, stage: 1);

        yield return PlanItem("crown-fit", "plan:crown-46", "613", "Crown fit and cementation",
            "46", 1720m, 620m, TreatmentItemStatus.Scheduled, stage: 2,
            appointmentKey: "appointment:crown-fit");

        yield return PlanItem("rct-46", "plan:crown-46", "415", "Root canal treatment",
            "46", 2280m, 0m, TreatmentItemStatus.Planned, stage: 3);

        yield return PlanItem("radiograph", "plan:crown-46", "037", "Panoramic radiograph",
            null, 1000m, 0m, TreatmentItemStatus.Completed, stage: 1);

        yield return PlanItem("whitening", "plan:whitening", "911", "Take-home whitening trays",
            null, 650m, 0m, TreatmentItemStatus.Planned, stage: 1);
    }

    private static TreatmentPlanItem PlanItem(
        string key,
        string planKey,
        string itemNumber,
        string description,
        string? tooth,
        decimal fee,
        decimal benefit,
        TreatmentItemStatus status,
        int stage,
        string? appointmentKey = null) =>
        new()
        {
            Id = Id($"plan-item:{key}"),
            AppointmentId = appointmentKey is null ? null : Id(appointmentKey),
            TreatmentPlanId = Id(planKey),
            ProcedureCodeId = Id($"procedure:{itemNumber}"),
            ItemNumber = itemNumber,
            Description = description,
            ToothNumber = tooth,
            Surfaces = tooth is null ? ToothSurface.None : ToothSurface.Occlusal | ToothSurface.Mesial,
            Fee = fee,
            EstimatedBenefit = benefit,
            Status = status,
            StageNumber = stage,
        };

    // ---- diary -----------------------------------------------------------

    /// <summary>
    /// This patient's future visits, for the overview tab. Distinct from
    /// <c>TodaysAppointments</c>, which is the whole practice's day.
    /// </summary>
    private static IEnumerable<Appointment> UpcomingAppointments(DateOnly today)
    {
        yield return Appointment("crown-fit", today.AddDays(3), 8, 45, RecordPatientNumber,
            "vance", "chair-1", "crown-prep", 60, AppointmentStatus.Confirmed, "Crown fit 46");

        yield return Appointment("hygiene-recall", today.AddMonths(5), 9, 30, RecordPatientNumber,
            "ito", "chair-3", "hygiene", 40, AppointmentStatus.Scheduled, "Hygiene recall");
    }

    private static IEnumerable<Recall> Recalls(DateOnly today)
    {
        yield return new Recall
        {
            Id = Id("recall:hygiene"),
            PatientId = RecordPatient,
            RecallType = "Perio maintenance",

            // Three months, not the usual six: a periodontal patient is seen more often.
            IntervalMonths = 3,
            DueOn = today.AddMonths(5),
            Status = RecallStatus.Booked,
            BookedAppointmentId = Id("appointment:hygiene-recall"),
            Notes = "Perio maintenance — 3-monthly while pocketing persists.",
        };

        // Pending recalls across the patient list, so the recall worklist has work in it.
        // Dated relative to today rather than absolutely: a seed whose overdue recalls
        // quietly become future-dated is a demo of an empty screen.
        yield return PendingRecall(today, "hollis", "10223", "Exam & clean", 6,
            today.AddMonths(-5), attempts: 2, contactedDaysAgo: 34);

        yield return PendingRecall(today, "papas", "10209", "Perio maintenance", 3,
            today.AddDays(-7), attempts: 1, contactedDaysAgo: 5);

        yield return PendingRecall(today, "chen", "10210", "Hygiene", 4, today.AddDays(2));

        yield return PendingRecall(today, "kovac", "10218", "Ortho review", 5, today.AddDays(3));

        yield return PendingRecall(today, "marsh", "10212", "Exam & clean", 6, today.AddDays(9));
    }

    /// <param name="contactedDaysAgo">
    /// Null where nobody has chased it yet, which is the common case and the one the
    /// worklist puts first.
    /// </param>
    private static Recall PendingRecall(
        DateOnly today,
        string key,
        string patientNumber,
        string recallType,
        int intervalMonths,
        DateOnly dueOn,
        int attempts = 0,
        int? contactedDaysAgo = null) =>
        new()
        {
            Id = Id($"recall:{key}"),
            PatientId = Id($"patient:{patientNumber}"),
            RecallType = recallType,
            IntervalMonths = intervalMonths,
            DueOn = dueOn,
            Status = RecallStatus.Pending,
            ContactAttempts = attempts,
            // A plausible mid-morning, not midnight. Seeding the bare date wrote midnight
            // UTC, which rendered as the same odd early hour on every row — a contact log
            // that looks like a bug is a contact log nobody trusts.
            LastContactedUtc = contactedDaysAgo is { } days
                ? today.AddDays(-days)
                    .ToDateTime(new TimeOnly(10, 15), DateTimeKind.Local)
                    .ToUniversalTime()
                : null,
        };

    // ---- documents and consent -------------------------------------------

    private static IEnumerable<PatientDocument> Documents(DateOnly today)
    {
        yield return Document("referral", DocumentKind.Referral, "Referral — Dr Osman (perio)",
            "Perio consult", today.AddDays(-27), "application/pdf", 184_320);

        yield return Document("consent", DocumentKind.Consent, "Consent — crown 46",
            "Tooth 46", today.AddDays(-19), "application/pdf", 96_140);

        yield return Document("opg", DocumentKind.Radiograph, "OPG radiograph",
            "Full mouth", today.AddDays(-19), "application/dicom", 8_412_600);

        yield return Document("medicare", DocumentKind.Other, "Medicare card scan",
            null, today.AddMonths(-14), "image/jpeg", 412_800);

        yield return Document("history", DocumentKind.Consent, "Medical history 2025",
            null, today.AddMonths(-14), "application/pdf", 74_200);
    }

    private static PatientDocument Document(
        string key,
        DocumentKind kind,
        string name,
        string? relatesTo,
        DateOnly date,
        string contentType,
        long sizeBytes) =>
        new()
        {
            Id = Id($"document:{key}"),
            PatientId = RecordPatient,
            Kind = kind,
            Name = name,
            RelatesTo = relatesTo,

            // Relative to the app's document store, never absolute: an absolute path
            // recorded on one device means nothing on another, and on iOS it changes
            // between installs of the same app.
            RelativePath = $"patients/{RecordPatientNumber}/{key}",
            ContentType = contentType,
            SizeBytes = sizeBytes,
            DocumentDateUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime(),
            UploadedByProviderId = Id("provider:brennan"),
        };

    private static IEnumerable<ConsentForm> Consents(DateOnly today)
    {
        var signed = today.AddDays(-19);

        yield return new ConsentForm
        {
            Id = Id("consent:crown-46"),
            PatientId = RecordPatient,
            TreatmentPlanId = Id("plan:crown-46"),
            TreatmentPlanItemId = Id("plan-item:crown-prep"),
            Title = "Crown — tooth 46",
            Body =
                "Risks discussed: post-operative sensitivity, the possibility that the "
                + "tooth later needs root canal treatment, and crown fracture under "
                + "grinding. Alternatives and costs presented. Questions answered.",
            Status = ConsentStatus.Signed,
            SignedUtc = signed.ToDateTime(new TimeOnly(9, 52), DateTimeKind.Local).ToUniversalTime(),
            SignedByName = "Margaret Yuen",
            SignedByRelationship = "Patient",
            WitnessedByProviderId = Id("provider:vance"),
        };

        yield return new ConsentForm
        {
            Id = Id("consent:rct-46"),
            PatientId = RecordPatient,
            TreatmentPlanId = Id("plan:crown-46"),
            TreatmentPlanItemId = Id("plan-item:rct-46"),
            Title = "Root canal treatment — tooth 46",
            Status = ConsentStatus.Pending,
        };
    }

    // ---- communications --------------------------------------------------

    private static IEnumerable<CommunicationLog> Communications(DateOnly today)
    {
        yield return Message("reminder", CommunicationChannel.Sms, MessagePurpose.AppointmentReminder,
            CommunicationStatus.Responded, today.AddDays(1), new TimeOnly(7, 2),
            "Molargo Dental: reminder for your crown fit with Dr Vance. Reply YES to confirm.",
            response: "YES",
            appointmentKey: "appointment:crown-fit");

        yield return Message("receipt", CommunicationChannel.Email, MessagePurpose.AccountNotice,
            CommunicationStatus.Delivered, today.AddDays(-19), new TimeOnly(16, 40),
            "Receipt for your visit on the 20th — crown preparation, $1,120 paid.",
            subject: "Receipt — Molargo Dental");

        yield return Message("postop", CommunicationChannel.Phone, MessagePurpose.Aftercare,
            CommunicationStatus.Delivered, today.AddDays(-18), new TimeOnly(9, 15),
            "Post-op check-in after the crown prep. No concerns, temporary crown comfortable.",
            loggedBy: "provider:brennan");

        yield return Message("recall", CommunicationChannel.Sms, MessagePurpose.Recall,
            CommunicationStatus.Delivered, today.AddDays(-29), new TimeOnly(8, 0),
            "Molargo Dental: your hygiene visit is due. Book online at molargo.example/book");

        // A failure, on purpose. An unnoticed bounce is a reminder the patient never got,
        // which surfaces later as an FTA nobody can explain — so the comms log has to
        // show one.
        yield return Message("marketing", CommunicationChannel.Email, MessagePurpose.Marketing,
            CommunicationStatus.Failed, today.AddMonths(-3), new TimeOnly(10, 30),
            "Spring whitening offer — 20% off take-home trays.",
            subject: "Whitening offer",
            failureReason: "Rejected by recipient server: mailbox full.");
    }

    private static CommunicationLog Message(
        string key,
        CommunicationChannel channel,
        MessagePurpose purpose,
        CommunicationStatus status,
        DateOnly day,
        TimeOnly time,
        string body,
        string? subject = null,
        string? response = null,
        string? failureReason = null,
        string? appointmentKey = null,
        string? loggedBy = null)
    {
        var sentUtc = day.ToDateTime(time, DateTimeKind.Local).ToUniversalTime();

        return new CommunicationLog
        {
            Id = Id($"comms:{key}"),
            PatientId = RecordPatient,
            Channel = channel,
            Direction = CommunicationDirection.Outbound,
            Purpose = purpose,
            Status = status,
            Subject = subject,
            Body = body,
            Recipient = channel == CommunicationChannel.Email
                ? "m.yuen@example.com"
                : "0412 883 021",
            SentUtc = sentUtc,
            DeliveredUtc = status is CommunicationStatus.Delivered or CommunicationStatus.Responded
                ? sentUtc.AddSeconds(9)
                : null,
            ResponseBody = response,
            RespondedUtc = response is null ? null : sentUtc.AddMinutes(4),
            FailureReason = failureReason,
            AppointmentId = appointmentKey is null ? null : Id(appointmentKey),
            SentByProviderId = loggedBy is null ? null : Id(loggedBy),

            // CreatedUtc is what the log sorts by, and the seed stamps every row with one
            // timestamp — so it is set here to the message's own time instead.
            CreatedUtc = sentUtc,
        };
    }

    // ---- billing ---------------------------------------------------------

    private static IEnumerable<Invoice> Invoices(DateOnly today)
    {
        // Part paid: $1,720 charged, $600 from the fund, $780 from the patient, leaving
        // the $340 that shows on the patients list and the record header.
        yield return Invoice("crown-prep", "10442", today.AddDays(-19),
            subtotal: 1720m, total: 1720m, paid: 1380m, InvoiceStatus.PartiallyPaid);

        yield return Invoice("opg", "10441", today.AddDays(-19),
            subtotal: 110m, total: 110m, paid: 110m, InvoiceStatus.Paid);

        yield return Invoice("exam", "10388", today.AddMonths(-2),
            subtotal: 245m, total: 245m, paid: 245m, InvoiceStatus.Paid);
    }

    private static Invoice Invoice(
        string key,
        string number,
        DateOnly issued,
        decimal subtotal,
        decimal total,
        decimal paid,
        InvoiceStatus status) =>
        new()
        {
            Id = Id($"invoice:{key}"),
            PatientId = RecordPatient,
            PracticeLocationId = SydneyCbd,
            ProviderId = Id("provider:vance"),
            InvoiceNumber = number,
            Status = status,
            IssuedUtc = issued.ToDateTime(new TimeOnly(17, 0), DateTimeKind.Local).ToUniversalTime(),
            DueOn = issued.AddDays(14),
            Subtotal = subtotal,
            Total = total,
            AmountPaid = paid,
        };

    private static IEnumerable<InvoiceLine> InvoiceLines(DateOnly today)
    {
        yield return Line("crown-prep", "invoice:crown-prep", "613",
            "Full crown, veneered, indirect", "46", 1720m, today.AddDays(-19));

        yield return Line("opg", "invoice:opg", "037",
            "Panoramic radiograph", null, 110m, today.AddDays(-19));

        yield return Line("exam", "invoice:exam", "011",
            "Comprehensive oral examination", null, 100m, today.AddMonths(-2));

        yield return Line("clean", "invoice:exam", "114",
            "Removal of calculus", null, 145m, today.AddMonths(-2));
    }

    private static InvoiceLine Line(
        string key,
        string invoiceKey,
        string itemNumber,
        string description,
        string? tooth,
        decimal fee,
        DateOnly serviceDate) =>
        new()
        {
            Id = Id($"invoice-line:{key}"),
            InvoiceId = Id(invoiceKey),
            ProcedureCodeId = Id($"procedure:{itemNumber}"),
            ItemNumber = itemNumber,
            Description = description,
            ToothNumber = tooth,
            Quantity = 1,
            UnitFee = fee,
            LineTotal = fee,
            ServiceDate = serviceDate,
            ProviderId = Id("provider:vance"),
        };

    private static IEnumerable<Payment> Payments(DateOnly today)
    {
        yield return Payment("crown-fund", "invoice:crown-prep", PaymentMethod.HicapsFundBenefit,
            600m, today.AddDays(-19), "HICAPS 884213", claimKey: "claim:crown-prep");

        yield return Payment("crown-card", "invoice:crown-prep", PaymentMethod.EftposCard,
            780m, today.AddDays(-19), "EFTPOS 4412");

        yield return Payment("opg-fund", "invoice:opg", PaymentMethod.HicapsFundBenefit,
            88m, today.AddDays(-19), "HICAPS 884214", claimKey: "claim:opg");

        yield return Payment("opg-card", "invoice:opg", PaymentMethod.EftposCard,
            22m, today.AddDays(-19), "EFTPOS 4412");

        yield return Payment("exam", "invoice:exam", PaymentMethod.HicapsFundBenefit,
            245m, today.AddMonths(-2), "HICAPS 871902", claimKey: "claim:exam");
    }

    private static Payment Payment(
        string key,
        string invoiceKey,
        PaymentMethod method,
        decimal amount,
        DateOnly received,
        string reference,
        string? claimKey = null) =>
        new()
        {
            Id = Id($"payment:{key}"),
            PatientId = RecordPatient,
            PracticeLocationId = SydneyCbd,
            InvoiceId = Id(invoiceKey),
            Method = method,
            Amount = amount,
            ReceivedUtc = received.ToDateTime(new TimeOnly(17, 5), DateTimeKind.Local).ToUniversalTime(),
            Reference = reference,
            ClaimId = claimKey is null ? null : Id(claimKey),
            ReceivedByProviderId = Id("provider:brennan"),
        };

    private static IEnumerable<Claim> Claims(DateOnly today)
    {
        // Approved at less than claimed: the fund's crown limit was already partly used
        // this year, and the gap fell to the patient. That is the normal case, not an
        // error, and the billing tab has to show it as such.
        yield return Claim("crown-prep", "invoice:crown-prep", 1720m, 600m,
            ClaimStatus.PartiallyApproved, today.AddDays(-19),
            "Annual major dental limit reached. Benefit paid to remaining limit.");

        yield return Claim("opg", "invoice:opg", 110m, 88m,
            ClaimStatus.Approved, today.AddDays(-19), null);

        yield return Claim("exam", "invoice:exam", 245m, 245m,
            ClaimStatus.Approved, today.AddMonths(-2), null);
    }

    private static Claim Claim(
        string key,
        string invoiceKey,
        decimal claimed,
        decimal approved,
        ClaimStatus status,
        DateOnly submitted,
        string? message) =>
        new()
        {
            Id = Id($"claim:{key}"),
            PatientId = RecordPatient,
            InvoiceId = Id(invoiceKey),
            Type = ClaimType.PrivateHealthFund,
            Status = status,
            PayerName = "HCF",
            MemberNumber = "90218843",
            PayerReference = $"HCF-{key.ToUpperInvariant()}",
            AmountClaimed = claimed,
            AmountApproved = approved,
            SubmittedUtc = submitted.ToDateTime(new TimeOnly(17, 2), DateTimeKind.Local).ToUniversalTime(),
            AssessedUtc = submitted.ToDateTime(new TimeOnly(17, 3), DateTimeKind.Local).ToUniversalTime(),
            AssessmentMessage = message,
            SubmittedByProviderId = Id("provider:brennan"),
        };
}

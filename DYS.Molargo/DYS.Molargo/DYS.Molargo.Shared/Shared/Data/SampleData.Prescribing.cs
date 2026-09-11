using DYS.Molargo.Domain.Entities;
using DYS.Molargo.Domain.Enums;

namespace DYS.Molargo.Shared.Data;

/// <summary>
/// The practice formulary, and the example patient's scripts, referrals and certificates.
/// </summary>
internal static partial class SampleData
{
    /// <summary>
    /// The medicines a general dental practice actually prescribes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Short and dental, not a national drug list. Every entry carries the allergy family
    /// it belongs to, because that is what makes the safety screen work: a penicillin
    /// allergy has to stop amoxicillin, and "penicillin" appears nowhere in the word
    /// "amoxicillin".
    /// </para>
    /// <para>
    /// The interactions encoded here are the ones that come up in dentistry — an NSAID or
    /// metronidazole against warfarin, and antibiotics against methotrexate. This is a
    /// screen written by hand, not a pharmacological database, and the prescribing screen
    /// says so where a clinician will read it.
    /// </para>
    /// </remarks>
    private static IEnumerable<FormularyMedicine> Formulary()
    {
        yield return new FormularyMedicine
        {
            Id = Id("formulary:amoxicillin"),
            GenericName = "Amoxicillin",
            Strength = "500mg",
            Form = "Capsule",
            Class = MedicineClass.Antibiotic,
            DefaultDirections = "One capsule three times a day for five days. "
                + "Take until the course is finished.",
            DefaultQuantity = 15,
            AllergyClasses = "penicillin;amoxicillin;beta-lactam;amoxil",
            InteractsWith = "methotrexate;warfarin",
            InteractionCaution = "Antibiotics can raise INR and methotrexate levels. "
                + "Check INR if the patient is anticoagulated.",
            DisplayOrder = 1,
        };

        yield return new FormularyMedicine
        {
            Id = Id("formulary:amoxiclav"),
            GenericName = "Amoxicillin with clavulanic acid",
            Strength = "875/125mg",
            Form = "Tablet",
            Class = MedicineClass.Antibiotic,
            DefaultDirections = "One tablet twice a day for five days, with food.",
            DefaultQuantity = 10,
            AllergyClasses = "penicillin;amoxicillin;beta-lactam;clavulan",
            InteractsWith = "methotrexate;warfarin",
            InteractionCaution = "Raises INR. Check before and after the course.",
            DisplayOrder = 2,
        };

        yield return new FormularyMedicine
        {
            Id = Id("formulary:metronidazole"),
            GenericName = "Metronidazole",
            Strength = "400mg",
            Form = "Tablet",
            Class = MedicineClass.Antibiotic,
            DefaultDirections = "One tablet three times a day for five days. "
                + "Do not drink alcohol during the course or for 48 hours after.",
            DefaultQuantity = 15,
            AllergyClasses = "metronidazole;nitroimidazole;flagyl",
            InteractsWith = "warfarin;alcohol",
            InteractionCaution = "Markedly potentiates warfarin — INR must be monitored. "
                + "Also causes a severe reaction with alcohol.",
            DisplayOrder = 3,
        };

        yield return new FormularyMedicine
        {
            Id = Id("formulary:clindamycin"),
            GenericName = "Clindamycin",
            Strength = "150mg",
            Form = "Capsule",
            Class = MedicineClass.Antibiotic,

            // The reason this one is on the list: no cross-reactivity with penicillin, so
            // it is the alternative when the screen blocks amoxicillin.
            DefaultDirections = "One capsule four times a day for five days. "
                + "Report any diarrhoea promptly.",
            DefaultQuantity = 20,
            AllergyClasses = "clindamycin;lincosamide;dalacin",
            DisplayOrder = 4,
        };

        yield return new FormularyMedicine
        {
            Id = Id("formulary:ibuprofen"),
            GenericName = "Ibuprofen",
            Strength = "400mg",
            Form = "Tablet",
            Class = MedicineClass.AntiInflammatory,
            DefaultDirections = "One tablet three times a day with food, as needed for pain. "
                + "Maximum three tablets a day.",
            DefaultQuantity = 20,
            AllergyClasses = "ibuprofen;nsaid;aspirin;nurofen",
            InteractsWith = "warfarin;apixaban;rivaroxaban;lithium;methotrexate",
            InteractionCaution = "Bleeding risk with anticoagulants. "
                + "Use paracetamol instead unless there is a specific reason not to.",
            ConditionCautions = "asthma;renal;kidney;ulcer;reflux;pregnan",
            ConditionCaution = "Avoid in asthma, renal impairment, peptic ulcer disease and "
                + "the third trimester.",
            DisplayOrder = 5,
        };

        yield return new FormularyMedicine
        {
            Id = Id("formulary:paracetamol"),
            GenericName = "Paracetamol",
            Strength = "500mg",
            Form = "Tablet",
            Class = MedicineClass.Analgesic,
            DefaultDirections = "Two tablets every six hours as needed for pain. "
                + "Maximum eight tablets in 24 hours.",
            DefaultQuantity = 20,
            AllergyClasses = "paracetamol;acetaminophen;panadol",
            ConditionCautions = "liver;hepatic;alcohol dependence",
            ConditionCaution = "Reduce the daily maximum in hepatic impairment.",
            DisplayOrder = 6,
        };

        yield return new FormularyMedicine
        {
            Id = Id("formulary:chlorhexidine"),
            GenericName = "Chlorhexidine gluconate",
            Strength = "0.2%",
            Form = "Mouthwash",
            Class = MedicineClass.Antiseptic,
            DefaultDirections = "Rinse 10ml for one minute twice a day. "
                + "Do not use within 30 minutes of toothpaste. Will stain teeth with "
                + "prolonged use.",
            DefaultQuantity = 1,
            AllergyClasses = "chlorhexidine",
            DisplayOrder = 7,
        };

        yield return new FormularyMedicine
        {
            Id = Id("formulary:nystatin"),
            GenericName = "Nystatin",
            Strength = "100,000 units/mL",
            Form = "Oral drops",
            Class = MedicineClass.Antifungal,
            DefaultDirections = "1mL in the mouth four times a day after food for seven "
                + "days. Hold in the mouth as long as possible before swallowing.",
            DefaultQuantity = 1,
            AllergyClasses = "nystatin",
            DisplayOrder = 8,
        };

        yield return new FormularyMedicine
        {
            Id = Id("formulary:aciclovir"),
            GenericName = "Aciclovir",
            Strength = "5%",
            Form = "Cream",
            Class = MedicineClass.Antiviral,
            DefaultDirections = "Apply to the affected area five times a day for five days, "
                + "starting at the first tingle.",
            DefaultQuantity = 1,
            AllergyClasses = "aciclovir;acyclovir",
            DisplayOrder = 9,
        };

        yield return new FormularyMedicine
        {
            Id = Id("formulary:diazepam"),
            GenericName = "Diazepam",
            Strength = "5mg",
            Form = "Tablet",
            Class = MedicineClass.Anxiolytic,
            DefaultDirections = "One tablet one hour before the appointment. "
                + "Do not drive. Bring an escort.",
            DefaultQuantity = 2,
            AllergyClasses = "diazepam;benzodiazepine;valium",
            InteractsWith = "opioid;oxycodone;codeine;alcohol",
            InteractionCaution = "Respiratory depression with opioids. Avoid the combination.",
            ConditionCautions = "sleep apnoea;apnea;copd;pregnan",
            ConditionCaution = "Avoid in sleep apnoea and significant respiratory disease.",
            DisplayOrder = 10,
        };
    }

    /// <summary>
    /// The example patient's prescribing and referral history.
    /// </summary>
    /// <remarks>
    /// Margaret is penicillin-allergic and on warfarin, which is what makes her the right
    /// patient for this screen: amoxicillin is blocked outright, ibuprofen and
    /// metronidazole warn, and clindamycin plus paracetamol are the way through. Her past
    /// scripts are the ones a prescriber would actually have written given that.
    /// </remarks>
    private static (List<Prescription> Scripts, List<PrescriptionItem> Items,
        List<Referral> Referrals, List<MedicalCertificate> Certificates)
        Prescribing(DateOnly today)
    {
        var scripts = new List<Prescription>();
        var items = new List<PrescriptionItem>();
        var referrals = new List<Referral>();
        var certificates = new List<MedicalCertificate>();

        // After the extraction: an antibiotic she can actually have, plus analgesia that
        // is not an NSAID.
        var afterExtraction = new Prescription
        {
            Id = Id("rx:post-extraction"),
            PatientId = RecordPatient,
            ProviderId = Id("provider:vance"),
            Status = PrescriptionStatus.Dispensed,
            IssuedUtc = Stamp(today.AddMonths(-7), 11, 20),
            AllergyCheckedUtc = Stamp(today.AddMonths(-7), 11, 19),
            ValidUntil = today.AddMonths(5),
            DispensedUtc = Stamp(today.AddMonths(-7), 15, 05),
            PharmacyName = "Angel Street Pharmacy",
        };

        scripts.Add(afterExtraction);

        items.Add(Line(afterExtraction.Id, "clindamycin", "Clindamycin", "150mg", "Capsule",
            "One capsule four times a day for five days. Report any diarrhoea promptly.", 20));

        items.Add(Line(afterExtraction.Id, "paracetamol", "Paracetamol", "500mg", "Tablet",
            "Two tablets every six hours as needed for pain. "
                + "Maximum eight tablets in 24 hours.", 20));

        // Perio maintenance.
        var mouthwash = new Prescription
        {
            Id = Id("rx:chlorhexidine"),
            PatientId = RecordPatient,
            ProviderId = Id("provider:ito"),
            Status = PrescriptionStatus.Issued,
            IssuedUtc = Stamp(today.AddMonths(-2), 9, 45),
            AllergyCheckedUtc = Stamp(today.AddMonths(-2), 9, 44),
            ValidUntil = today.AddMonths(10),
        };

        scripts.Add(mouthwash);

        items.Add(Line(mouthwash.Id, "chlorhexidine", "Chlorhexidine gluconate", "0.2%",
            "Mouthwash",
            "Rinse 10ml for one minute twice a day for two weeks. "
                + "Do not use within 30 minutes of toothpaste.", 1));

        // The outbound referral the design's letter is about — sent, nothing back yet,
        // which is what the in-flight list exists to surface.
        referrals.Add(new Referral
        {
            Id = Id("referral:perio"),
            PatientId = RecordPatient,
            ProviderId = Id("provider:vance"),
            Direction = ReferralDirection.Outbound,
            Status = ReferralStatus.Sent,
            CounterpartyName = "Dr Osman",
            CounterpartySpecialty = "Periodontics",
            CounterpartyPractice = "Newtown Periodontics",
            Reason = "For periodontal assessment and management.",
            RelatesTo = "16",
            LetterBody = "Dear Dr Osman,\n\nRe: Margaret Yuen, DOB 12 Mar 1968\n\n"
                + "Thank you for seeing Margaret for periodontal assessment and management.\n\n"
                + "Localised 5mm pocketing at 16 with bleeding on probing despite "
                + "three-monthly maintenance; attachment loss 2mm at 16 mesial. Grade I "
                + "mobility and Grade I furcation involvement at 16.\n\n"
                + "Medical history: Latex; Penicillin; Warfarin 5mg daily; Type 2 diabetes; "
                + "Hypertension.\n\nKind regards,\nDr R. Vance",
            SentUtc = Stamp(today.AddDays(-24), 16, 10),
        });

        referrals.Add(new Referral
        {
            Id = Id("referral:in-gp"),
            PatientId = RecordPatient,
            ProviderId = Id("provider:vance"),
            Direction = ReferralDirection.Inbound,
            Status = ReferralStatus.Attended,
            CounterpartyName = "Dr Helen Park",
            CounterpartySpecialty = "General practice",
            CounterpartyPractice = "Erskineville Medical",
            Reason = "Dental assessment before starting bisphosphonate therapy.",
            SentUtc = Stamp(today.AddMonths(-8), 10, 0),
            AttendedUtc = Stamp(today.AddMonths(-7), 11, 0),
        });

        referrals.Add(new Referral
        {
            Id = Id("referral:in-ortho"),
            PatientId = Id("patient:10222"),
            Direction = ReferralDirection.Inbound,
            ProviderId = Id("provider:ellery"),
            Status = ReferralStatus.Sent,
            CounterpartyName = "Dr Lindqvist",
            CounterpartySpecialty = "Orthodontics",
            Reason = "Restorative review during aligner treatment.",
            SentUtc = Stamp(today.AddDays(-9), 14, 30),
        });

        referrals.Add(new Referral
        {
            Id = Id("referral:in-omfs"),
            PatientId = Id("patient:10211"),
            ProviderId = Id("provider:vance"),
            Direction = ReferralDirection.Inbound,
            Status = ReferralStatus.Sent,
            CounterpartyName = "Oral Medicine Unit",
            CounterpartySpecialty = "Oral medicine",
            Reason = "Suspected lichenoid reaction — assessment requested.",
            IsUrgent = true,
            SentUtc = Stamp(today.AddDays(-2), 8, 55),
        });

        return (scripts, items, referrals, certificates);
    }

    private static PrescriptionItem Line(
        Guid prescriptionId,
        string key,
        string name,
        string strength,
        string form,
        string directions,
        int quantity) =>
        new()
        {
            Id = Id($"rx:item:{prescriptionId:N}:{key}"),
            PrescriptionId = prescriptionId,
            MedicineName = name,
            Strength = strength,
            Form = form,
            Directions = directions,
            Quantity = quantity,
        };

    private static DateTime Stamp(DateOnly day, int hour, int minute) =>
        day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Local).ToUniversalTime();
}

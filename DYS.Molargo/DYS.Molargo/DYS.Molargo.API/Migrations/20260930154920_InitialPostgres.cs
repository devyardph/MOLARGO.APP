using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DYS.Molargo.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TreatmentPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentSeriesId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeriesPosition = table.Column<int>(type: "integer", nullable: true),
                    ConfirmedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckedInUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "text", nullable: true),
                    FilledFromWaitlist = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "appointment_reminder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    OffsetDays = table.Column<int>(type: "integer", nullable: false),
                    SentUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_reminder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "appointment_series",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PlannedVisits = table.Column<int>(type: "integer", nullable: false),
                    TreatmentPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_series", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "appointment_type",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    DefaultDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Colour = table.Column<string>(type: "text", nullable: true),
                    AllowedRoles = table.Column<string>(type: "text", nullable: true),
                    RequiresSurgicalRoom = table.Column<bool>(type: "boolean", nullable: false),
                    IsBookableOnline = table.Column<bool>(type: "boolean", nullable: false),
                    RecallIntervalMonths = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_type", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_entry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    EntityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderName = table.Column<string>(type: "text", nullable: true),
                    OccurredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeviceId = table.Column<string>(type: "text", nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "claim",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PayerName = table.Column<string>(type: "text", nullable: true),
                    MemberNumber = table.Column<string>(type: "text", nullable: true),
                    PayerReference = table.Column<string>(type: "text", nullable: true),
                    AmountClaimed = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    AmountApproved = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AssessedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AssessmentMessage = table.Column<string>(type: "text", nullable: true),
                    SubmittedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "clinical_note",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TreatmentDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Presenting = table.Column<string>(type: "text", nullable: true),
                    Examination = table.Column<string>(type: "text", nullable: true),
                    Diagnosis = table.Column<string>(type: "text", nullable: true),
                    TreatmentProvided = table.Column<string>(type: "text", nullable: true),
                    Plan = table.Column<string>(type: "text", nullable: true),
                    LockedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AmendsNoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_note", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "communication_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MessageTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Recipient = table.Column<string>(type: "text", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecallId = table.Column<Guid>(type: "uuid", nullable: true),
                    SentUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    RespondedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    SentByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communication_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "consent_form",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    TreatmentPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    TreatmentPlanItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SignedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignedByName = table.Column<string>(type: "text", nullable: true),
                    SignedByRelationship = table.Column<string>(type: "text", nullable: true),
                    SignatureImage = table.Column<string>(type: "text", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    WitnessedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_form", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "consent_template",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "formulary_medicine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GenericName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BrandName = table.Column<string>(type: "text", nullable: true),
                    Strength = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Form = table.Column<string>(type: "text", nullable: true),
                    Class = table.Column<int>(type: "integer", nullable: false),
                    DefaultDirections = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DefaultQuantity = table.Column<int>(type: "integer", nullable: false),
                    DefaultRepeats = table.Column<int>(type: "integer", nullable: false),
                    AllergyClasses = table.Column<string>(type: "text", nullable: true),
                    InteractsWith = table.Column<string>(type: "text", nullable: true),
                    InteractionCaution = table.Column<string>(type: "text", nullable: true),
                    ConditionCautions = table.Column<string>(type: "text", nullable: true),
                    ConditionCaution = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_formulary_medicine", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "help_article",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Keywords = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_help_article", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "invoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TreatmentPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssuedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    AdjustmentReason = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_line",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcedureCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ToothNumber = table.Column<string>(type: "text", nullable: true),
                    Surfaces = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitFee = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TreatmentPlanItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_line", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lab_case",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ToothNumber = table.Column<string>(type: "text", nullable: true),
                    Specification = table.Column<string>(type: "text", nullable: true),
                    LabReference = table.Column<string>(type: "text", nullable: true),
                    SentUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FitAppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    FittedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LabFee = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    RemakeReason = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_case", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "local_metadata",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_metadata", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "medical_certificate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttendedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    UnfitFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    UnfitTo = table.Column<DateOnly>(type: "date", nullable: false),
                    IsForStudy = table.Column<bool>(type: "boolean", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    IssuedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Signature = table.Column<string>(type: "text", nullable: true),
                    SignedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_certificate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "medical_history_answer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalHistoryFormId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QuestionText = table.Column<string>(type: "text", nullable: false),
                    YesNo = table.Column<bool>(type: "boolean", nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_history_answer", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "medical_history_form",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    FormVersion = table.Column<int>(type: "integer", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignedByName = table.Column<string>(type: "text", nullable: true),
                    SignatureImage = table.Column<string>(type: "text", nullable: true),
                    ReviewedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdditionalNotes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_history_form", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "medical_history_question",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    DetailPrompt = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AlertKind = table.Column<int>(type: "integer", nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    PromptsForDetail = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_history_question", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "message_template",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Trigger = table.Column<int>(type: "integer", nullable: false),
                    SendHoursBeforeAppointment = table.Column<int>(type: "integer", nullable: true),
                    SendHoursAfterAppointment = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SenderAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    SenderName = table.Column<string>(type: "text", nullable: true),
                    ReplyToAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    AlertsToAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    NotifyReceipts = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyLowStock = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyDailySummary = table.Column<bool>(type: "boolean", nullable: false),
                    DailySummaryRecipients = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DailySummaryAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    RemindersEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderOffsetsDays = table.Column<string>(type: "text", nullable: true),
                    AppPassword = table.Column<string>(type: "text", nullable: true),
                    SmtpHost = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SmtpPort = table.Column<int>(type: "integer", nullable: false),
                    LastTestUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastTestResult = table.Column<string>(type: "text", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "operatory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsSurgical = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operatory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_code",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_code", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "patient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreferredName = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    PatientNumber = table.Column<string>(type: "text", nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Sex = table.Column<int>(type: "integer", nullable: false),
                    Mobile = table.Column<string>(type: "text", nullable: true),
                    HomePhone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    AddressLine = table.Column<string>(type: "text", nullable: true),
                    Suburb = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    Postcode = table.Column<string>(type: "text", nullable: true),
                    PreferredLanguage = table.Column<string>(type: "text", nullable: true),
                    EmergencyContactName = table.Column<string>(type: "text", nullable: true),
                    EmergencyContactRelationship = table.Column<string>(type: "text", nullable: true),
                    EmergencyContactPhone = table.Column<string>(type: "text", nullable: true),
                    HealthFund = table.Column<string>(type: "text", nullable: true),
                    HealthFundMemberNumber = table.Column<string>(type: "text", nullable: true),
                    MedicareNumber = table.Column<string>(type: "text", nullable: true),
                    MedicareReferenceNumber = table.Column<int>(type: "integer", nullable: true),
                    DvaNumber = table.Column<string>(type: "text", nullable: true),
                    ConcessionCardNumber = table.Column<string>(type: "text", nullable: true),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreferredProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferralSource = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<int>(type: "integer", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Balance = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    FailedToAttendCount = table.Column<int>(type: "integer", nullable: false),
                    MarketingConsent = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderConsent = table.Column<bool>(type: "boolean", nullable: false),
                    MarketingConsentUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReminderConsentUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsentSource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "patient_alert",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    OnsetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ResolvedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RecordedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_alert", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "patient_document",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    RelativePath = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DocumentDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RelatesTo = table.Column<string>(type: "text", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_document", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ReceivedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "perio_exam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExamDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perio_exam", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "perio_site_reading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerioExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToothNumber = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Site = table.Column<int>(type: "integer", nullable: false),
                    ProbingDepthMm = table.Column<int>(type: "integer", nullable: true),
                    RecessionMm = table.Column<int>(type: "integer", nullable: true),
                    Bleeding = table.Column<bool>(type: "boolean", nullable: false),
                    Suppuration = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perio_site_reading", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "perio_tooth_reading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerioExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToothNumber = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Mobility = table.Column<int>(type: "integer", nullable: true),
                    Furcation = table.Column<int>(type: "integer", nullable: true),
                    Plaque = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perio_tooth_reading", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MonthlyBase = table.Column<decimal>(type: "numeric(12,2)", precision: 18, scale: 2, nullable: false),
                    IncludedSites = table.Column<int>(type: "integer", nullable: false),
                    IncludedSeatsPerSite = table.Column<int>(type: "integer", nullable: false),
                    IncludedSmsPerSite = table.Column<int>(type: "integer", nullable: false),
                    PricePerExtraSite = table.Column<decimal>(type: "numeric(12,2)", precision: 18, scale: 2, nullable: false),
                    PricePerExtraSeat = table.Column<decimal>(type: "numeric(12,2)", precision: 18, scale: 2, nullable: false),
                    AnnualMonthsCharged = table.Column<int>(type: "integer", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "practice_location",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShortName = table.Column<string>(type: "text", nullable: true),
                    AddressLine = table.Column<string>(type: "text", nullable: true),
                    Suburb = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    Postcode = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Abn = table.Column<string>(type: "text", nullable: true),
                    TimeZoneId = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    OpeningDays = table.Column<int>(type: "integer", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practice_location", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "practice_task",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedToProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practice_task", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "prescription",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IssuedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    PharmacyName = table.Column<string>(type: "text", nullable: true),
                    AllergyCheckedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    DispensedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PrescriberSignature = table.Column<string>(type: "text", nullable: true),
                    SignedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prescription", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "prescription_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicineName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BrandName = table.Column<string>(type: "text", nullable: true),
                    Strength = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Form = table.Column<string>(type: "text", nullable: true),
                    Directions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Repeats = table.Column<int>(type: "integer", nullable: false),
                    BrandSubstitutionNotPermitted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prescription_item", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "printer_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Connection = table.Column<int>(type: "integer", nullable: false),
                    DeviceAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NetworkPort = table.Column<int>(type: "integer", nullable: false),
                    ReceiptPrinterName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Paper = table.Column<int>(type: "integer", nullable: false),
                    PrintReceiptAutomatically = table.Column<bool>(type: "boolean", nullable: false),
                    OpenCashDrawer = table.Column<bool>(type: "boolean", nullable: false),
                    PrintLogo = table.Column<bool>(type: "boolean", nullable: false),
                    ReceiptFooter = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DocumentPrinterName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DocumentPaper = table.Column<int>(type: "integer", nullable: false),
                    PrescriptionCopies = table.Column<int>(type: "integer", nullable: false),
                    PrintPrescriberDetails = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_printer_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "procedure_code",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PatientFriendlyName = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    Fee = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    IsPerTooth = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresSurface = table.Column<bool>(type: "boolean", nullable: false),
                    TypicalDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    IsCdbsEligible = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procedure_code", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "procedure_code_fee",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcedureCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(12,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procedure_code_fee", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "provider",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsOwner = table.Column<bool>(type: "boolean", nullable: false),
                    Permissions = table.Column<int>(type: "integer", nullable: false),
                    ProviderNumber = table.Column<string>(type: "text", nullable: true),
                    LicenceNumber = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Mobile = table.Column<string>(type: "text", nullable: true),
                    PrimaryLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkingDays = table.Column<int>(type: "integer", nullable: false),
                    WorkingFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    WorkingTo = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    DiaryColour = table.Column<string>(type: "text", nullable: true),
                    PayBasis = table.Column<int>(type: "integer", nullable: false),
                    PayRate = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    PayBonusTarget = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    PayBonusPercent = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    LicenceExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Username = table.Column<string>(type: "text", nullable: true),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    PasswordUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSignInUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedSignInCount = table.Column<int>(type: "integer", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OrderedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpectedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RaisedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_line",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    QuantityOrdered = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    QuantityReceived = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    BatchNumber = table.Column<string>(type: "text", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_line", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recall",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecallType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IntervalMonths = table.Column<int>(type: "integer", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastAppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookedAppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastContactedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContactAttempts = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recall", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "referral",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterpartyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CounterpartySpecialty = table.Column<string>(type: "text", nullable: true),
                    CounterpartyPractice = table.Column<string>(type: "text", nullable: true),
                    CounterpartyPhone = table.Column<string>(type: "text", nullable: true),
                    CounterpartyEmail = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RelatesTo = table.Column<string>(type: "text", nullable: true),
                    LetterBody = table.Column<string>(type: "text", nullable: true),
                    SentUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttendedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReportReceivedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReportDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsUrgent = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referral", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sign_in_code",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sign_in_code", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "signup_code",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signup_code", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sms_gateway",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ApiUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AuthType = table.Column<int>(type: "integer", nullable: false),
                    AuthHeaderName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AuthUsername = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SenderId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PricePerMessage = table.Column<decimal>(type: "numeric(12,2)", precision: 18, scale: 4, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PayloadTemplate = table.Column<string>(type: "text", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: false),
                    Base64EncodeApiKey = table.Column<bool>(type: "boolean", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreditLimit = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastTestUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastTestResult = table.Column<string>(type: "text", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_gateway", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sterilisation_cycle",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SterilisorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CycleNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    CycleType = table.Column<string>(type: "text", nullable: true),
                    PeakTemperatureCelsius = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    HoldTimeMinutes = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    ChemicalIndicatorPassed = table.Column<bool>(type: "boolean", nullable: false),
                    BiologicalIndicatorPassed = table.Column<bool>(type: "boolean", nullable: true),
                    LoadContents = table.Column<string>(type: "text", nullable: true),
                    OperatedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureNotes = table.Column<string>(type: "text", nullable: true),
                    ReleasedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sterilisation_cycle", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sterilisation_cycle_use",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SterilisationCycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PackIdentifier = table.Column<string>(type: "text", nullable: true),
                    RecordedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sterilisation_cycle_use", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_category",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_category", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierItemCode = table.Column<string>(type: "text", nullable: true),
                    UnitOfMeasure = table.Column<string>(type: "text", nullable: true),
                    QuantityOnHand = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ReorderLevel = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ReorderQuantity = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    EarliestExpiry = table.Column<DateOnly>(type: "date", nullable: true),
                    RequiresBatchTracking = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_item", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_movement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BatchNumber = table.Column<string>(type: "text", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_charge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 18, scale: 2, nullable: false),
                    SmsCount = table.Column<int>(type: "integer", nullable: false),
                    SmsPricePerMessage = table.Column<decimal>(type: "numeric(12,2)", precision: 18, scale: 4, nullable: true),
                    SmsIncluded = table.Column<int>(type: "integer", nullable: false),
                    SmsAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 18, scale: 2, nullable: false),
                    SmsPeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    SmsPeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Sites = table.Column<int>(type: "integer", nullable: false),
                    Clinicians = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SettledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_charge", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AccountNumber = table.Column<string>(type: "text", nullable: true),
                    ContactName = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    IsLaboratory = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Abn = table.Column<string>(type: "text", nullable: true),
                    ContactEmail = table.Column<string>(type: "text", nullable: true),
                    ContactPhone = table.Column<string>(type: "text", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    SmsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SubscribedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TrialEndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TermsAcceptedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsPlatform = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tooth_chart_entry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToothNumber = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Notation = table.Column<int>(type: "integer", nullable: false),
                    Condition = table.Column<int>(type: "integer", nullable: false),
                    Surfaces = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    ObservedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ChartedByProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TreatmentPlanItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersededOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tooth_chart_entry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "treatment_plan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Rationale = table.Column<string>(type: "text", nullable: true),
                    PresentedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EstimateValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    QuotedTotal = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    EstimatedBenefit = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsRecommended = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_treatment_plan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "treatment_plan_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TreatmentPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcedureCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ToothNumber = table.Column<string>(type: "text", nullable: true),
                    Surfaces = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    EstimatedBenefit = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StageNumber = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvoiceLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_treatment_plan_item", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "waitlist_entry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PracticeLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferredProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Availability = table.Column<string>(type: "text", nullable: true),
                    AvailableFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    AvailableUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentAppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    FulfilledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastContactedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waitlist_entry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_AppointmentSeriesId_SeriesPosition",
                table: "appointment",
                columns: new[] { "AppointmentSeriesId", "SeriesPosition" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_IsDeleted",
                table: "appointment",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_PatientId_StartUtc",
                table: "appointment",
                columns: new[] { "PatientId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_PracticeLocationId_StartUtc",
                table: "appointment",
                columns: new[] { "PracticeLocationId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_ProviderId_StartUtc",
                table: "appointment",
                columns: new[] { "ProviderId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_Status",
                table: "appointment",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_TenantId",
                table: "appointment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_reminder_AppointmentId_OffsetDays",
                table: "appointment_reminder",
                columns: new[] { "AppointmentId", "OffsetDays" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_appointment_reminder_IsDeleted",
                table: "appointment_reminder",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_reminder_TenantId",
                table: "appointment_reminder",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_series_IsDeleted",
                table: "appointment_series",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_series_PatientId",
                table: "appointment_series",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_series_TenantId",
                table: "appointment_series",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_type_IsDeleted",
                table: "appointment_type",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_type_TenantId",
                table: "appointment_type",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_EntityName_EntityId",
                table: "audit_entry",
                columns: new[] { "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_IsDeleted",
                table: "audit_entry",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_PatientId_OccurredUtc",
                table: "audit_entry",
                columns: new[] { "PatientId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_ProviderId_OccurredUtc",
                table: "audit_entry",
                columns: new[] { "ProviderId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_TenantId",
                table: "audit_entry",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_TenantId_IsDeleted_OccurredUtc",
                table: "audit_entry",
                columns: new[] { "TenantId", "IsDeleted", "OccurredUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_claim_InvoiceId",
                table: "claim",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_claim_IsDeleted",
                table: "claim",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_claim_PatientId",
                table: "claim",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_claim_Status_SubmittedUtc",
                table: "claim",
                columns: new[] { "Status", "SubmittedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_claim_TenantId",
                table: "claim",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_note_AppointmentId",
                table: "clinical_note",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_note_IsDeleted",
                table: "clinical_note",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_note_PatientId_TreatmentDateUtc",
                table: "clinical_note",
                columns: new[] { "PatientId", "TreatmentDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_note_TenantId",
                table: "clinical_note",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_communication_log_AppointmentId",
                table: "communication_log",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_communication_log_IsDeleted",
                table: "communication_log",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_communication_log_PatientId_CreatedUtc",
                table: "communication_log",
                columns: new[] { "PatientId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_communication_log_Status",
                table: "communication_log",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_communication_log_TenantId",
                table: "communication_log",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_consent_form_IsDeleted",
                table: "consent_form",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_consent_form_PatientId_Status",
                table: "consent_form",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_consent_form_TenantId",
                table: "consent_form",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_consent_template_Category_IsActive",
                table: "consent_template",
                columns: new[] { "Category", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_consent_template_IsDeleted",
                table: "consent_template",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_consent_template_TenantId",
                table: "consent_template",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_formulary_medicine_IsActive_DisplayOrder",
                table: "formulary_medicine",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_formulary_medicine_IsDeleted",
                table: "formulary_medicine",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_formulary_medicine_TenantId",
                table: "formulary_medicine",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_help_article_IsDeleted",
                table: "help_article",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_help_article_IsPublished_Category_DisplayOrder",
                table: "help_article",
                columns: new[] { "IsPublished", "Category", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_help_article_Slug",
                table: "help_article",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_help_article_TenantId",
                table: "help_article",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_InvoiceNumber",
                table: "invoice",
                column: "InvoiceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_IsDeleted",
                table: "invoice",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_IssuedUtc",
                table: "invoice",
                column: "IssuedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_PatientId_Status",
                table: "invoice",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_TenantId",
                table: "invoice",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_TenantId_PracticeLocationId_IsDeleted_IssuedUtc_Cre~",
                table: "invoice",
                columns: new[] { "TenantId", "PracticeLocationId", "IsDeleted", "IssuedUtc", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_line_InvoiceId",
                table: "invoice_line",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_line_IsDeleted",
                table: "invoice_line",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_line_ServiceDate",
                table: "invoice_line",
                column: "ServiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_line_TenantId",
                table: "invoice_line",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_case_IsDeleted",
                table: "lab_case",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_lab_case_PatientId",
                table: "lab_case",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_case_Status_DueOn",
                table: "lab_case",
                columns: new[] { "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_lab_case_SupplierId",
                table: "lab_case",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_case_TenantId",
                table: "lab_case",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_certificate_IsDeleted",
                table: "medical_certificate",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_medical_certificate_PatientId_AttendedOn",
                table: "medical_certificate",
                columns: new[] { "PatientId", "AttendedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_certificate_TenantId",
                table: "medical_certificate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_answer_IsDeleted",
                table: "medical_history_answer",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_answer_MedicalHistoryFormId_QuestionCode",
                table: "medical_history_answer",
                columns: new[] { "MedicalHistoryFormId", "QuestionCode" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_answer_QuestionCode",
                table: "medical_history_answer",
                column: "QuestionCode");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_answer_TenantId",
                table: "medical_history_answer",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_form_AppointmentId",
                table: "medical_history_form",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_form_IsDeleted",
                table: "medical_history_form",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_form_PatientId",
                table: "medical_history_form",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_form_TenantId",
                table: "medical_history_form",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_question_IsDeleted",
                table: "medical_history_question",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_question_TenantId",
                table: "medical_history_question",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_question_TenantId_Code",
                table: "medical_history_question",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_message_template_Channel_Purpose",
                table: "message_template",
                columns: new[] { "Channel", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_message_template_IsDeleted",
                table: "message_template",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_message_template_TenantId",
                table: "message_template",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_message_template_Trigger",
                table: "message_template",
                column: "Trigger");

            migrationBuilder.CreateIndex(
                name: "IX_notification_settings_IsDeleted",
                table: "notification_settings",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_notification_settings_TenantId",
                table: "notification_settings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operatory_IsDeleted",
                table: "operatory",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_operatory_PracticeLocationId_DisplayOrder",
                table: "operatory",
                columns: new[] { "PracticeLocationId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_operatory_TenantId",
                table: "operatory",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_code_IsDeleted",
                table: "password_reset_code",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_code_ProviderId_UsedUtc",
                table: "password_reset_code",
                columns: new[] { "ProviderId", "UsedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_code_TenantId",
                table: "password_reset_code",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_HouseholdId",
                table: "patient",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_IsDeleted",
                table: "patient",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_patient_LastName_FirstName",
                table: "patient",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_Mobile",
                table: "patient",
                column: "Mobile");

            migrationBuilder.CreateIndex(
                name: "IX_patient_PatientNumber",
                table: "patient",
                column: "PatientNumber");

            migrationBuilder.CreateIndex(
                name: "IX_patient_PracticeLocationId_MarketingConsent",
                table: "patient",
                columns: new[] { "PracticeLocationId", "MarketingConsent" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_Status",
                table: "patient",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_patient_TenantId",
                table: "patient",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_alert_IsDeleted",
                table: "patient_alert",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_patient_alert_PatientId_Kind",
                table: "patient_alert",
                columns: new[] { "PatientId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_alert_TenantId",
                table: "patient_alert",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_document_IsDeleted",
                table: "patient_document",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_patient_document_PatientId_Kind",
                table: "patient_document",
                columns: new[] { "PatientId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_document_TenantId",
                table: "patient_document",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_InvoiceId",
                table: "payment",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_IsDeleted",
                table: "payment",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_payment_PatientId",
                table: "payment",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_PracticeLocationId_ReceivedUtc",
                table: "payment",
                columns: new[] { "PracticeLocationId", "ReceivedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_TenantId",
                table: "payment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_perio_exam_IsDeleted",
                table: "perio_exam",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_perio_exam_PatientId_ExamDate",
                table: "perio_exam",
                columns: new[] { "PatientId", "ExamDate" });

            migrationBuilder.CreateIndex(
                name: "IX_perio_exam_TenantId",
                table: "perio_exam",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_perio_site_reading_IsDeleted",
                table: "perio_site_reading",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_perio_site_reading_PerioExamId_ToothNumber",
                table: "perio_site_reading",
                columns: new[] { "PerioExamId", "ToothNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_perio_site_reading_PerioExamId_ToothNumber_Site",
                table: "perio_site_reading",
                columns: new[] { "PerioExamId", "ToothNumber", "Site" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_perio_site_reading_TenantId",
                table: "perio_site_reading",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_perio_tooth_reading_IsDeleted",
                table: "perio_tooth_reading",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_perio_tooth_reading_PerioExamId_ToothNumber",
                table: "perio_tooth_reading",
                columns: new[] { "PerioExamId", "ToothNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_perio_tooth_reading_TenantId",
                table: "perio_tooth_reading",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_plan_Code_CountryCode",
                table: "plan",
                columns: new[] { "Code", "CountryCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plan_CountryCode_IsActive",
                table: "plan",
                columns: new[] { "CountryCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_plan_IsDeleted",
                table: "plan",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_plan_TenantId",
                table: "plan",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_practice_location_IsDeleted",
                table: "practice_location",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_practice_location_TenantId",
                table: "practice_location",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_practice_task_IsDeleted",
                table: "practice_task",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_practice_task_PracticeLocationId_CompletedUtc_DueOn",
                table: "practice_task",
                columns: new[] { "PracticeLocationId", "CompletedUtc", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_practice_task_TenantId",
                table: "practice_task",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_IsDeleted",
                table: "prescription",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_PatientId_IssuedUtc",
                table: "prescription",
                columns: new[] { "PatientId", "IssuedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_prescription_Status",
                table: "prescription",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_TenantId",
                table: "prescription",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_item_IsDeleted",
                table: "prescription_item",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_item_PrescriptionId",
                table: "prescription_item",
                column: "PrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_item_TenantId",
                table: "prescription_item",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_printer_settings_IsDeleted",
                table: "printer_settings",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_printer_settings_TenantId",
                table: "printer_settings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_Category",
                table: "procedure_code",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_IsDeleted",
                table: "procedure_code",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_TenantId",
                table: "procedure_code",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_TenantId_ItemNumber",
                table: "procedure_code",
                columns: new[] { "TenantId", "ItemNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_fee_IsDeleted",
                table: "procedure_code_fee",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_fee_PracticeLocationId",
                table: "procedure_code_fee",
                column: "PracticeLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_fee_ProcedureCodeId_PracticeLocationId",
                table: "procedure_code_fee",
                columns: new[] { "ProcedureCodeId", "PracticeLocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_fee_TenantId",
                table: "procedure_code_fee",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_IsActive",
                table: "provider",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_provider_IsDeleted",
                table: "provider",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_provider_TenantId",
                table: "provider",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_IsDeleted",
                table: "purchase_order",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_PracticeLocationId_Status",
                table: "purchase_order",
                columns: new[] { "PracticeLocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_SupplierId",
                table: "purchase_order",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_TenantId",
                table: "purchase_order",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_line_IsDeleted",
                table: "purchase_order_line",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_line_PurchaseOrderId",
                table: "purchase_order_line",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_line_StockItemId",
                table: "purchase_order_line",
                column: "StockItemId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_line_TenantId",
                table: "purchase_order_line",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_recall_IsDeleted",
                table: "recall",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_recall_PatientId",
                table: "recall",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_recall_Status_DueOn",
                table: "recall",
                columns: new[] { "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_recall_TenantId",
                table: "recall",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_referral_Direction_Status",
                table: "referral",
                columns: new[] { "Direction", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_referral_IsDeleted",
                table: "referral",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_referral_PatientId",
                table: "referral",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_referral_TenantId",
                table: "referral",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sign_in_code_IsDeleted",
                table: "sign_in_code",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_sign_in_code_ProviderId_UsedUtc",
                table: "sign_in_code",
                columns: new[] { "ProviderId", "UsedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_sign_in_code_TenantId",
                table: "sign_in_code",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_signup_code_Email_ConsumedUtc",
                table: "signup_code",
                columns: new[] { "Email", "ConsumedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_signup_code_IsDeleted",
                table: "signup_code",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_signup_code_TenantId",
                table: "signup_code",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sms_gateway_CountryCode",
                table: "sms_gateway",
                column: "CountryCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sms_gateway_IsDeleted",
                table: "sms_gateway",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_sms_gateway_TenantId",
                table: "sms_gateway",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_IsDeleted",
                table: "sterilisation_cycle",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_PracticeLocationId_StartedUtc",
                table: "sterilisation_cycle",
                columns: new[] { "PracticeLocationId", "StartedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_Result",
                table: "sterilisation_cycle",
                column: "Result");

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_SterilisorName_CycleNumber",
                table: "sterilisation_cycle",
                columns: new[] { "SterilisorName", "CycleNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_TenantId",
                table: "sterilisation_cycle",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_use_IsDeleted",
                table: "sterilisation_cycle_use",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_use_PatientId",
                table: "sterilisation_cycle_use",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_use_SterilisationCycleId",
                table: "sterilisation_cycle_use",
                column: "SterilisationCycleId");

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_use_TenantId",
                table: "sterilisation_cycle_use",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_category_IsDeleted",
                table: "stock_category",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_stock_category_TenantId",
                table: "stock_category",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_category_TenantId_Name",
                table: "stock_category",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_item_EarliestExpiry",
                table: "stock_item",
                column: "EarliestExpiry");

            migrationBuilder.CreateIndex(
                name: "IX_stock_item_IsDeleted",
                table: "stock_item",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_stock_item_PracticeLocationId_Category",
                table: "stock_item",
                columns: new[] { "PracticeLocationId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_item_Sku",
                table: "stock_item",
                column: "Sku");

            migrationBuilder.CreateIndex(
                name: "IX_stock_item_TenantId",
                table: "stock_item",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_BatchNumber",
                table: "stock_movement",
                column: "BatchNumber");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_IsDeleted",
                table: "stock_movement",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_PatientId",
                table: "stock_movement",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_StockItemId_OccurredUtc",
                table: "stock_movement",
                columns: new[] { "StockItemId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_TenantId",
                table: "stock_movement",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_charge_IsDeleted",
                table: "subscription_charge",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_charge_PeriodStart",
                table: "subscription_charge",
                column: "PeriodStart");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_charge_TenantId",
                table: "subscription_charge",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_charge_TenantId_PeriodStart",
                table: "subscription_charge",
                columns: new[] { "TenantId", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_IsDeleted",
                table: "supplier",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_IsLaboratory",
                table: "supplier",
                column: "IsLaboratory");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_TenantId",
                table: "supplier",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_IsDeleted",
                table: "tenant",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_Slug",
                table: "tenant",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_TenantId",
                table: "tenant",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tooth_chart_entry_IsDeleted",
                table: "tooth_chart_entry",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_tooth_chart_entry_PatientId_SupersededOn",
                table: "tooth_chart_entry",
                columns: new[] { "PatientId", "SupersededOn" });

            migrationBuilder.CreateIndex(
                name: "IX_tooth_chart_entry_PatientId_ToothNumber",
                table: "tooth_chart_entry",
                columns: new[] { "PatientId", "ToothNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_tooth_chart_entry_TenantId",
                table: "tooth_chart_entry",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_IsDeleted",
                table: "treatment_plan",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_PatientId_Status",
                table: "treatment_plan",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_Status",
                table: "treatment_plan",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_TenantId",
                table: "treatment_plan",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_item_AppointmentId",
                table: "treatment_plan_item",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_item_IsDeleted",
                table: "treatment_plan_item",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_item_TenantId",
                table: "treatment_plan_item",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_item_TreatmentPlanId_StageNumber_DisplayOrder",
                table: "treatment_plan_item",
                columns: new[] { "TreatmentPlanId", "StageNumber", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entry_IsDeleted",
                table: "waitlist_entry",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entry_PatientId",
                table: "waitlist_entry",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entry_PracticeLocationId_Priority_FulfilledUtc",
                table: "waitlist_entry",
                columns: new[] { "PracticeLocationId", "Priority", "FulfilledUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entry_TenantId",
                table: "waitlist_entry",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment");

            migrationBuilder.DropTable(
                name: "appointment_reminder");

            migrationBuilder.DropTable(
                name: "appointment_series");

            migrationBuilder.DropTable(
                name: "appointment_type");

            migrationBuilder.DropTable(
                name: "audit_entry");

            migrationBuilder.DropTable(
                name: "claim");

            migrationBuilder.DropTable(
                name: "clinical_note");

            migrationBuilder.DropTable(
                name: "communication_log");

            migrationBuilder.DropTable(
                name: "consent_form");

            migrationBuilder.DropTable(
                name: "consent_template");

            migrationBuilder.DropTable(
                name: "formulary_medicine");

            migrationBuilder.DropTable(
                name: "help_article");

            migrationBuilder.DropTable(
                name: "invoice");

            migrationBuilder.DropTable(
                name: "invoice_line");

            migrationBuilder.DropTable(
                name: "lab_case");

            migrationBuilder.DropTable(
                name: "local_metadata");

            migrationBuilder.DropTable(
                name: "medical_certificate");

            migrationBuilder.DropTable(
                name: "medical_history_answer");

            migrationBuilder.DropTable(
                name: "medical_history_form");

            migrationBuilder.DropTable(
                name: "medical_history_question");

            migrationBuilder.DropTable(
                name: "message_template");

            migrationBuilder.DropTable(
                name: "notification_settings");

            migrationBuilder.DropTable(
                name: "operatory");

            migrationBuilder.DropTable(
                name: "password_reset_code");

            migrationBuilder.DropTable(
                name: "patient");

            migrationBuilder.DropTable(
                name: "patient_alert");

            migrationBuilder.DropTable(
                name: "patient_document");

            migrationBuilder.DropTable(
                name: "payment");

            migrationBuilder.DropTable(
                name: "perio_exam");

            migrationBuilder.DropTable(
                name: "perio_site_reading");

            migrationBuilder.DropTable(
                name: "perio_tooth_reading");

            migrationBuilder.DropTable(
                name: "plan");

            migrationBuilder.DropTable(
                name: "practice_location");

            migrationBuilder.DropTable(
                name: "practice_task");

            migrationBuilder.DropTable(
                name: "prescription");

            migrationBuilder.DropTable(
                name: "prescription_item");

            migrationBuilder.DropTable(
                name: "printer_settings");

            migrationBuilder.DropTable(
                name: "procedure_code");

            migrationBuilder.DropTable(
                name: "procedure_code_fee");

            migrationBuilder.DropTable(
                name: "provider");

            migrationBuilder.DropTable(
                name: "purchase_order");

            migrationBuilder.DropTable(
                name: "purchase_order_line");

            migrationBuilder.DropTable(
                name: "recall");

            migrationBuilder.DropTable(
                name: "referral");

            migrationBuilder.DropTable(
                name: "sign_in_code");

            migrationBuilder.DropTable(
                name: "signup_code");

            migrationBuilder.DropTable(
                name: "sms_gateway");

            migrationBuilder.DropTable(
                name: "sterilisation_cycle");

            migrationBuilder.DropTable(
                name: "sterilisation_cycle_use");

            migrationBuilder.DropTable(
                name: "stock_category");

            migrationBuilder.DropTable(
                name: "stock_item");

            migrationBuilder.DropTable(
                name: "stock_movement");

            migrationBuilder.DropTable(
                name: "subscription_charge");

            migrationBuilder.DropTable(
                name: "supplier");

            migrationBuilder.DropTable(
                name: "tenant");

            migrationBuilder.DropTable(
                name: "tooth_chart_entry");

            migrationBuilder.DropTable(
                name: "treatment_plan");

            migrationBuilder.DropTable(
                name: "treatment_plan_item");

            migrationBuilder.DropTable(
                name: "waitlist_entry");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DYS.Molargo.Api.Migrations
{
    /// <inheritdoc />
    public partial class TenantLeadingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_waitlist_entry_IsDeleted",
                table: "waitlist_entry");

            migrationBuilder.DropIndex(
                name: "IX_waitlist_entry_TenantId",
                table: "waitlist_entry");

            migrationBuilder.DropIndex(
                name: "IX_treatment_plan_item_IsDeleted",
                table: "treatment_plan_item");

            migrationBuilder.DropIndex(
                name: "IX_treatment_plan_item_TenantId",
                table: "treatment_plan_item");

            migrationBuilder.DropIndex(
                name: "IX_treatment_plan_IsDeleted",
                table: "treatment_plan");

            migrationBuilder.DropIndex(
                name: "IX_treatment_plan_TenantId",
                table: "treatment_plan");

            migrationBuilder.DropIndex(
                name: "IX_tooth_chart_entry_IsDeleted",
                table: "tooth_chart_entry");

            migrationBuilder.DropIndex(
                name: "IX_tooth_chart_entry_TenantId",
                table: "tooth_chart_entry");

            migrationBuilder.DropIndex(
                name: "IX_tenant_IsDeleted",
                table: "tenant");

            migrationBuilder.DropIndex(
                name: "IX_tenant_TenantId",
                table: "tenant");

            migrationBuilder.DropIndex(
                name: "IX_supplier_IsDeleted",
                table: "supplier");

            migrationBuilder.DropIndex(
                name: "IX_supplier_TenantId",
                table: "supplier");

            migrationBuilder.DropIndex(
                name: "IX_subscription_charge_IsDeleted",
                table: "subscription_charge");

            migrationBuilder.DropIndex(
                name: "IX_subscription_charge_TenantId",
                table: "subscription_charge");

            migrationBuilder.DropIndex(
                name: "IX_stock_movement_IsDeleted",
                table: "stock_movement");

            migrationBuilder.DropIndex(
                name: "IX_stock_movement_TenantId",
                table: "stock_movement");

            migrationBuilder.DropIndex(
                name: "IX_stock_item_IsDeleted",
                table: "stock_item");

            migrationBuilder.DropIndex(
                name: "IX_stock_item_TenantId",
                table: "stock_item");

            migrationBuilder.DropIndex(
                name: "IX_stock_category_IsDeleted",
                table: "stock_category");

            migrationBuilder.DropIndex(
                name: "IX_stock_category_TenantId",
                table: "stock_category");

            migrationBuilder.DropIndex(
                name: "IX_sterilisation_cycle_use_IsDeleted",
                table: "sterilisation_cycle_use");

            migrationBuilder.DropIndex(
                name: "IX_sterilisation_cycle_use_TenantId",
                table: "sterilisation_cycle_use");

            migrationBuilder.DropIndex(
                name: "IX_sterilisation_cycle_IsDeleted",
                table: "sterilisation_cycle");

            migrationBuilder.DropIndex(
                name: "IX_sterilisation_cycle_TenantId",
                table: "sterilisation_cycle");

            migrationBuilder.DropIndex(
                name: "IX_sms_gateway_IsDeleted",
                table: "sms_gateway");

            migrationBuilder.DropIndex(
                name: "IX_sms_gateway_TenantId",
                table: "sms_gateway");

            migrationBuilder.DropIndex(
                name: "IX_signup_code_IsDeleted",
                table: "signup_code");

            migrationBuilder.DropIndex(
                name: "IX_signup_code_TenantId",
                table: "signup_code");

            migrationBuilder.DropIndex(
                name: "IX_sign_in_code_IsDeleted",
                table: "sign_in_code");

            migrationBuilder.DropIndex(
                name: "IX_sign_in_code_TenantId",
                table: "sign_in_code");

            migrationBuilder.DropIndex(
                name: "IX_referral_IsDeleted",
                table: "referral");

            migrationBuilder.DropIndex(
                name: "IX_referral_TenantId",
                table: "referral");

            migrationBuilder.DropIndex(
                name: "IX_recall_IsDeleted",
                table: "recall");

            migrationBuilder.DropIndex(
                name: "IX_recall_TenantId",
                table: "recall");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_line_IsDeleted",
                table: "purchase_order_line");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_line_TenantId",
                table: "purchase_order_line");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_IsDeleted",
                table: "purchase_order");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_TenantId",
                table: "purchase_order");

            migrationBuilder.DropIndex(
                name: "IX_provider_IsDeleted",
                table: "provider");

            migrationBuilder.DropIndex(
                name: "IX_provider_TenantId",
                table: "provider");

            migrationBuilder.DropIndex(
                name: "IX_procedure_code_fee_IsDeleted",
                table: "procedure_code_fee");

            migrationBuilder.DropIndex(
                name: "IX_procedure_code_fee_TenantId",
                table: "procedure_code_fee");

            migrationBuilder.DropIndex(
                name: "IX_procedure_code_IsDeleted",
                table: "procedure_code");

            migrationBuilder.DropIndex(
                name: "IX_procedure_code_TenantId",
                table: "procedure_code");

            migrationBuilder.DropIndex(
                name: "IX_printer_settings_IsDeleted",
                table: "printer_settings");

            migrationBuilder.DropIndex(
                name: "IX_prescription_item_IsDeleted",
                table: "prescription_item");

            migrationBuilder.DropIndex(
                name: "IX_prescription_item_TenantId",
                table: "prescription_item");

            migrationBuilder.DropIndex(
                name: "IX_prescription_IsDeleted",
                table: "prescription");

            migrationBuilder.DropIndex(
                name: "IX_prescription_TenantId",
                table: "prescription");

            migrationBuilder.DropIndex(
                name: "IX_practice_task_IsDeleted",
                table: "practice_task");

            migrationBuilder.DropIndex(
                name: "IX_practice_task_TenantId",
                table: "practice_task");

            migrationBuilder.DropIndex(
                name: "IX_practice_location_IsDeleted",
                table: "practice_location");

            migrationBuilder.DropIndex(
                name: "IX_practice_location_TenantId",
                table: "practice_location");

            migrationBuilder.DropIndex(
                name: "IX_plan_IsDeleted",
                table: "plan");

            migrationBuilder.DropIndex(
                name: "IX_plan_TenantId",
                table: "plan");

            migrationBuilder.DropIndex(
                name: "IX_perio_tooth_reading_IsDeleted",
                table: "perio_tooth_reading");

            migrationBuilder.DropIndex(
                name: "IX_perio_tooth_reading_TenantId",
                table: "perio_tooth_reading");

            migrationBuilder.DropIndex(
                name: "IX_perio_site_reading_IsDeleted",
                table: "perio_site_reading");

            migrationBuilder.DropIndex(
                name: "IX_perio_site_reading_TenantId",
                table: "perio_site_reading");

            migrationBuilder.DropIndex(
                name: "IX_perio_exam_IsDeleted",
                table: "perio_exam");

            migrationBuilder.DropIndex(
                name: "IX_perio_exam_TenantId",
                table: "perio_exam");

            migrationBuilder.DropIndex(
                name: "IX_payment_IsDeleted",
                table: "payment");

            migrationBuilder.DropIndex(
                name: "IX_payment_TenantId",
                table: "payment");

            migrationBuilder.DropIndex(
                name: "IX_patient_document_IsDeleted",
                table: "patient_document");

            migrationBuilder.DropIndex(
                name: "IX_patient_document_TenantId",
                table: "patient_document");

            migrationBuilder.DropIndex(
                name: "IX_patient_alert_IsDeleted",
                table: "patient_alert");

            migrationBuilder.DropIndex(
                name: "IX_patient_alert_TenantId",
                table: "patient_alert");

            migrationBuilder.DropIndex(
                name: "IX_patient_IsDeleted",
                table: "patient");

            migrationBuilder.DropIndex(
                name: "IX_patient_LastName_FirstName",
                table: "patient");

            migrationBuilder.DropIndex(
                name: "IX_patient_TenantId",
                table: "patient");

            migrationBuilder.DropIndex(
                name: "IX_password_reset_code_IsDeleted",
                table: "password_reset_code");

            migrationBuilder.DropIndex(
                name: "IX_password_reset_code_TenantId",
                table: "password_reset_code");

            migrationBuilder.DropIndex(
                name: "IX_operatory_IsDeleted",
                table: "operatory");

            migrationBuilder.DropIndex(
                name: "IX_operatory_TenantId",
                table: "operatory");

            migrationBuilder.DropIndex(
                name: "IX_notification_settings_IsDeleted",
                table: "notification_settings");

            migrationBuilder.DropIndex(
                name: "IX_message_template_IsDeleted",
                table: "message_template");

            migrationBuilder.DropIndex(
                name: "IX_message_template_TenantId",
                table: "message_template");

            migrationBuilder.DropIndex(
                name: "IX_medical_history_question_IsDeleted",
                table: "medical_history_question");

            migrationBuilder.DropIndex(
                name: "IX_medical_history_question_TenantId",
                table: "medical_history_question");

            migrationBuilder.DropIndex(
                name: "IX_medical_history_form_IsDeleted",
                table: "medical_history_form");

            migrationBuilder.DropIndex(
                name: "IX_medical_history_form_TenantId",
                table: "medical_history_form");

            migrationBuilder.DropIndex(
                name: "IX_medical_history_answer_IsDeleted",
                table: "medical_history_answer");

            migrationBuilder.DropIndex(
                name: "IX_medical_history_answer_TenantId",
                table: "medical_history_answer");

            migrationBuilder.DropIndex(
                name: "IX_medical_certificate_IsDeleted",
                table: "medical_certificate");

            migrationBuilder.DropIndex(
                name: "IX_medical_certificate_TenantId",
                table: "medical_certificate");

            migrationBuilder.DropIndex(
                name: "IX_lab_case_IsDeleted",
                table: "lab_case");

            migrationBuilder.DropIndex(
                name: "IX_lab_case_TenantId",
                table: "lab_case");

            migrationBuilder.DropIndex(
                name: "IX_invoice_line_IsDeleted",
                table: "invoice_line");

            migrationBuilder.DropIndex(
                name: "IX_invoice_line_TenantId",
                table: "invoice_line");

            migrationBuilder.DropIndex(
                name: "IX_invoice_IsDeleted",
                table: "invoice");

            migrationBuilder.DropIndex(
                name: "IX_invoice_TenantId",
                table: "invoice");

            migrationBuilder.DropIndex(
                name: "IX_help_article_IsDeleted",
                table: "help_article");

            migrationBuilder.DropIndex(
                name: "IX_help_article_TenantId",
                table: "help_article");

            migrationBuilder.DropIndex(
                name: "IX_formulary_medicine_IsDeleted",
                table: "formulary_medicine");

            migrationBuilder.DropIndex(
                name: "IX_formulary_medicine_TenantId",
                table: "formulary_medicine");

            migrationBuilder.DropIndex(
                name: "IX_consent_template_IsDeleted",
                table: "consent_template");

            migrationBuilder.DropIndex(
                name: "IX_consent_template_TenantId",
                table: "consent_template");

            migrationBuilder.DropIndex(
                name: "IX_consent_form_IsDeleted",
                table: "consent_form");

            migrationBuilder.DropIndex(
                name: "IX_consent_form_TenantId",
                table: "consent_form");

            migrationBuilder.DropIndex(
                name: "IX_communication_log_IsDeleted",
                table: "communication_log");

            migrationBuilder.DropIndex(
                name: "IX_communication_log_TenantId",
                table: "communication_log");

            migrationBuilder.DropIndex(
                name: "IX_clinical_note_IsDeleted",
                table: "clinical_note");

            migrationBuilder.DropIndex(
                name: "IX_clinical_note_TenantId",
                table: "clinical_note");

            migrationBuilder.DropIndex(
                name: "IX_claim_IsDeleted",
                table: "claim");

            migrationBuilder.DropIndex(
                name: "IX_claim_TenantId",
                table: "claim");

            migrationBuilder.DropIndex(
                name: "IX_audit_entry_IsDeleted",
                table: "audit_entry");

            migrationBuilder.DropIndex(
                name: "IX_audit_entry_TenantId",
                table: "audit_entry");

            migrationBuilder.DropIndex(
                name: "IX_appointment_type_IsDeleted",
                table: "appointment_type");

            migrationBuilder.DropIndex(
                name: "IX_appointment_type_TenantId",
                table: "appointment_type");

            migrationBuilder.DropIndex(
                name: "IX_appointment_series_IsDeleted",
                table: "appointment_series");

            migrationBuilder.DropIndex(
                name: "IX_appointment_series_TenantId",
                table: "appointment_series");

            migrationBuilder.DropIndex(
                name: "IX_appointment_reminder_IsDeleted",
                table: "appointment_reminder");

            migrationBuilder.DropIndex(
                name: "IX_appointment_reminder_TenantId",
                table: "appointment_reminder");

            migrationBuilder.DropIndex(
                name: "IX_appointment_IsDeleted",
                table: "appointment");

            migrationBuilder.DropIndex(
                name: "IX_appointment_TenantId",
                table: "appointment");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entry_TenantId_IsDeleted",
                table: "waitlist_entry",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_item_TenantId_IsDeleted",
                table: "treatment_plan_item",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_TenantId_IsDeleted",
                table: "treatment_plan",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_tooth_chart_entry_TenantId_IsDeleted",
                table: "tooth_chart_entry",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_TenantId_IsDeleted",
                table: "tenant",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_TenantId_IsDeleted",
                table: "supplier",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_charge_TenantId_IsDeleted",
                table: "subscription_charge",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_TenantId_IsDeleted",
                table: "stock_movement",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_item_TenantId_IsDeleted",
                table: "stock_item",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_category_TenantId_IsDeleted",
                table: "stock_category",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_use_TenantId_IsDeleted",
                table: "sterilisation_cycle_use",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_TenantId_IsDeleted",
                table: "sterilisation_cycle",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_sms_gateway_TenantId_IsDeleted",
                table: "sms_gateway",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_signup_code_TenantId_IsDeleted",
                table: "signup_code",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_sign_in_code_TenantId_IsDeleted",
                table: "sign_in_code",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_referral_TenantId_IsDeleted",
                table: "referral",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_recall_TenantId_IsDeleted",
                table: "recall",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_line_TenantId_IsDeleted",
                table: "purchase_order_line",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_TenantId_IsDeleted",
                table: "purchase_order",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_provider_TenantId_IsDeleted",
                table: "provider",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_fee_TenantId_IsDeleted",
                table: "procedure_code_fee",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_TenantId_IsDeleted",
                table: "procedure_code",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_printer_settings_TenantId_IsDeleted",
                table: "printer_settings",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_prescription_item_TenantId_IsDeleted",
                table: "prescription_item",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_prescription_TenantId_IsDeleted",
                table: "prescription",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_practice_task_TenantId_IsDeleted",
                table: "practice_task",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_practice_location_TenantId_IsDeleted",
                table: "practice_location",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_plan_TenantId_IsDeleted",
                table: "plan",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_perio_tooth_reading_TenantId_IsDeleted",
                table: "perio_tooth_reading",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_perio_site_reading_TenantId_IsDeleted",
                table: "perio_site_reading",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_perio_exam_TenantId_IsDeleted",
                table: "perio_exam",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_TenantId_IsDeleted",
                table: "payment",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_document_TenantId_IsDeleted",
                table: "patient_document",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_alert_TenantId_IsDeleted",
                table: "patient_alert",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_TenantId_IsDeleted",
                table: "patient",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_TenantId_IsDeleted_LastName_FirstName",
                table: "patient",
                columns: new[] { "TenantId", "IsDeleted", "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_code_TenantId_IsDeleted",
                table: "password_reset_code",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_operatory_TenantId_IsDeleted",
                table: "operatory",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_settings_TenantId_IsDeleted",
                table: "notification_settings",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_message_template_TenantId_IsDeleted",
                table: "message_template",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_question_TenantId_IsDeleted",
                table: "medical_history_question",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_form_TenantId_IsDeleted",
                table: "medical_history_form",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_answer_TenantId_IsDeleted",
                table: "medical_history_answer",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_certificate_TenantId_IsDeleted",
                table: "medical_certificate",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_lab_case_TenantId_IsDeleted",
                table: "lab_case",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_line_TenantId_IsDeleted",
                table: "invoice_line",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_TenantId_IsDeleted",
                table: "invoice",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_help_article_TenantId_IsDeleted",
                table: "help_article",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_formulary_medicine_TenantId_IsDeleted",
                table: "formulary_medicine",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_consent_template_TenantId_IsDeleted",
                table: "consent_template",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_consent_form_TenantId_IsDeleted",
                table: "consent_form",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_communication_log_TenantId_IsDeleted",
                table: "communication_log",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_note_TenantId_IsDeleted",
                table: "clinical_note",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_claim_TenantId_IsDeleted",
                table: "claim",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_TenantId_IsDeleted",
                table: "audit_entry",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_type_TenantId_IsDeleted",
                table: "appointment_type",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_series_TenantId_IsDeleted",
                table: "appointment_series",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_reminder_TenantId_IsDeleted",
                table: "appointment_reminder",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_TenantId_IsDeleted",
                table: "appointment",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_TenantId_IsDeleted_StartUtc",
                table: "appointment",
                columns: new[] { "TenantId", "IsDeleted", "StartUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_waitlist_entry_TenantId_IsDeleted",
                table: "waitlist_entry");

            migrationBuilder.DropIndex(
                name: "IX_treatment_plan_item_TenantId_IsDeleted",
                table: "treatment_plan_item");

            migrationBuilder.DropIndex(
                name: "IX_treatment_plan_TenantId_IsDeleted",
                table: "treatment_plan");

            migrationBuilder.DropIndex(
                name: "IX_tooth_chart_entry_TenantId_IsDeleted",
                table: "tooth_chart_entry");

            migrationBuilder.DropIndex(
                name: "IX_tenant_TenantId_IsDeleted",
                table: "tenant");

            migrationBuilder.DropIndex(
                name: "IX_supplier_TenantId_IsDeleted",
                table: "supplier");

            migrationBuilder.DropIndex(
                name: "IX_subscription_charge_TenantId_IsDeleted",
                table: "subscription_charge");

            migrationBuilder.DropIndex(
                name: "IX_stock_movement_TenantId_IsDeleted",
                table: "stock_movement");

            migrationBuilder.DropIndex(
                name: "IX_stock_item_TenantId_IsDeleted",
                table: "stock_item");

            migrationBuilder.DropIndex(
                name: "IX_stock_category_TenantId_IsDeleted",
                table: "stock_category");

            migrationBuilder.DropIndex(
                name: "IX_sterilisation_cycle_use_TenantId_IsDeleted",
                table: "sterilisation_cycle_use");

            migrationBuilder.DropIndex(
                name: "IX_sterilisation_cycle_TenantId_IsDeleted",
                table: "sterilisation_cycle");

            migrationBuilder.DropIndex(
                name: "IX_sms_gateway_TenantId_IsDeleted",
                table: "sms_gateway");

            migrationBuilder.DropIndex(
                name: "IX_signup_code_TenantId_IsDeleted",
                table: "signup_code");

            migrationBuilder.DropIndex(
                name: "IX_sign_in_code_TenantId_IsDeleted",
                table: "sign_in_code");

            migrationBuilder.DropIndex(
                name: "IX_referral_TenantId_IsDeleted",
                table: "referral");

            migrationBuilder.DropIndex(
                name: "IX_recall_TenantId_IsDeleted",
                table: "recall");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_line_TenantId_IsDeleted",
                table: "purchase_order_line");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_TenantId_IsDeleted",
                table: "purchase_order");

            migrationBuilder.DropIndex(
                name: "IX_provider_TenantId_IsDeleted",
                table: "provider");

            migrationBuilder.DropIndex(
                name: "IX_procedure_code_fee_TenantId_IsDeleted",
                table: "procedure_code_fee");

            migrationBuilder.DropIndex(
                name: "IX_procedure_code_TenantId_IsDeleted",
                table: "procedure_code");

            migrationBuilder.DropIndex(
                name: "IX_printer_settings_TenantId_IsDeleted",
                table: "printer_settings");

            migrationBuilder.DropIndex(
                name: "IX_prescription_item_TenantId_IsDeleted",
                table: "prescription_item");

            migrationBuilder.DropIndex(
                name: "IX_prescription_TenantId_IsDeleted",
                table: "prescription");

            migrationBuilder.DropIndex(
                name: "IX_practice_task_TenantId_IsDeleted",
                table: "practice_task");

            migrationBuilder.DropIndex(
                name: "IX_practice_location_TenantId_IsDeleted",
                table: "practice_location");

            migrationBuilder.DropIndex(
                name: "IX_plan_TenantId_IsDeleted",
                table: "plan");

            migrationBuilder.DropIndex(
                name: "IX_perio_tooth_reading_TenantId_IsDeleted",
                table: "perio_tooth_reading");

            migrationBuilder.DropIndex(
                name: "IX_perio_site_reading_TenantId_IsDeleted",
                table: "perio_site_reading");

            migrationBuilder.DropIndex(
                name: "IX_perio_exam_TenantId_IsDeleted",
                table: "perio_exam");

            migrationBuilder.DropIndex(
                name: "IX_payment_TenantId_IsDeleted",
                table: "payment");

            migrationBuilder.DropIndex(
                name: "IX_patient_document_TenantId_IsDeleted",
                table: "patient_document");

            migrationBuilder.DropIndex(
                name: "IX_patient_alert_TenantId_IsDeleted",
                table: "patient_alert");

            migrationBuilder.DropIndex(
                name: "IX_patient_TenantId_IsDeleted",
                table: "patient");

            migrationBuilder.DropIndex(
                name: "IX_patient_TenantId_IsDeleted_LastName_FirstName",
                table: "patient");

            migrationBuilder.DropIndex(
                name: "IX_password_reset_code_TenantId_IsDeleted",
                table: "password_reset_code");

            migrationBuilder.DropIndex(
                name: "IX_operatory_TenantId_IsDeleted",
                table: "operatory");

            migrationBuilder.DropIndex(
                name: "IX_notification_settings_TenantId_IsDeleted",
                table: "notification_settings");

            migrationBuilder.DropIndex(
                name: "IX_message_template_TenantId_IsDeleted",
                table: "message_template");

            migrationBuilder.DropIndex(
                name: "IX_medical_history_question_TenantId_IsDeleted",
                table: "medical_history_question");

            migrationBuilder.DropIndex(
                name: "IX_medical_history_form_TenantId_IsDeleted",
                table: "medical_history_form");

            migrationBuilder.DropIndex(
                name: "IX_medical_history_answer_TenantId_IsDeleted",
                table: "medical_history_answer");

            migrationBuilder.DropIndex(
                name: "IX_medical_certificate_TenantId_IsDeleted",
                table: "medical_certificate");

            migrationBuilder.DropIndex(
                name: "IX_lab_case_TenantId_IsDeleted",
                table: "lab_case");

            migrationBuilder.DropIndex(
                name: "IX_invoice_line_TenantId_IsDeleted",
                table: "invoice_line");

            migrationBuilder.DropIndex(
                name: "IX_invoice_TenantId_IsDeleted",
                table: "invoice");

            migrationBuilder.DropIndex(
                name: "IX_help_article_TenantId_IsDeleted",
                table: "help_article");

            migrationBuilder.DropIndex(
                name: "IX_formulary_medicine_TenantId_IsDeleted",
                table: "formulary_medicine");

            migrationBuilder.DropIndex(
                name: "IX_consent_template_TenantId_IsDeleted",
                table: "consent_template");

            migrationBuilder.DropIndex(
                name: "IX_consent_form_TenantId_IsDeleted",
                table: "consent_form");

            migrationBuilder.DropIndex(
                name: "IX_communication_log_TenantId_IsDeleted",
                table: "communication_log");

            migrationBuilder.DropIndex(
                name: "IX_clinical_note_TenantId_IsDeleted",
                table: "clinical_note");

            migrationBuilder.DropIndex(
                name: "IX_claim_TenantId_IsDeleted",
                table: "claim");

            migrationBuilder.DropIndex(
                name: "IX_audit_entry_TenantId_IsDeleted",
                table: "audit_entry");

            migrationBuilder.DropIndex(
                name: "IX_appointment_type_TenantId_IsDeleted",
                table: "appointment_type");

            migrationBuilder.DropIndex(
                name: "IX_appointment_series_TenantId_IsDeleted",
                table: "appointment_series");

            migrationBuilder.DropIndex(
                name: "IX_appointment_reminder_TenantId_IsDeleted",
                table: "appointment_reminder");

            migrationBuilder.DropIndex(
                name: "IX_appointment_TenantId_IsDeleted",
                table: "appointment");

            migrationBuilder.DropIndex(
                name: "IX_appointment_TenantId_IsDeleted_StartUtc",
                table: "appointment");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entry_IsDeleted",
                table: "waitlist_entry",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entry_TenantId",
                table: "waitlist_entry",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_item_IsDeleted",
                table: "treatment_plan_item",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_item_TenantId",
                table: "treatment_plan_item",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_IsDeleted",
                table: "treatment_plan",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_treatment_plan_TenantId",
                table: "treatment_plan",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tooth_chart_entry_IsDeleted",
                table: "tooth_chart_entry",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_tooth_chart_entry_TenantId",
                table: "tooth_chart_entry",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_IsDeleted",
                table: "tenant",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_TenantId",
                table: "tenant",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_IsDeleted",
                table: "supplier",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_TenantId",
                table: "supplier",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_charge_IsDeleted",
                table: "subscription_charge",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_charge_TenantId",
                table: "subscription_charge",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_IsDeleted",
                table: "stock_movement",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_TenantId",
                table: "stock_movement",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_item_IsDeleted",
                table: "stock_item",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_stock_item_TenantId",
                table: "stock_item",
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
                name: "IX_sterilisation_cycle_use_IsDeleted",
                table: "sterilisation_cycle_use",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_use_TenantId",
                table: "sterilisation_cycle_use",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_IsDeleted",
                table: "sterilisation_cycle",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_sterilisation_cycle_TenantId",
                table: "sterilisation_cycle",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sms_gateway_IsDeleted",
                table: "sms_gateway",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_sms_gateway_TenantId",
                table: "sms_gateway",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_signup_code_IsDeleted",
                table: "signup_code",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_signup_code_TenantId",
                table: "signup_code",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sign_in_code_IsDeleted",
                table: "sign_in_code",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_sign_in_code_TenantId",
                table: "sign_in_code",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_referral_IsDeleted",
                table: "referral",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_referral_TenantId",
                table: "referral",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_recall_IsDeleted",
                table: "recall",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_recall_TenantId",
                table: "recall",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_line_IsDeleted",
                table: "purchase_order_line",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_line_TenantId",
                table: "purchase_order_line",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_IsDeleted",
                table: "purchase_order",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_TenantId",
                table: "purchase_order",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_IsDeleted",
                table: "provider",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_provider_TenantId",
                table: "provider",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_fee_IsDeleted",
                table: "procedure_code_fee",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_fee_TenantId",
                table: "procedure_code_fee",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_IsDeleted",
                table: "procedure_code",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_procedure_code_TenantId",
                table: "procedure_code",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_printer_settings_IsDeleted",
                table: "printer_settings",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_item_IsDeleted",
                table: "prescription_item",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_item_TenantId",
                table: "prescription_item",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_IsDeleted",
                table: "prescription",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_TenantId",
                table: "prescription",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_practice_task_IsDeleted",
                table: "practice_task",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_practice_task_TenantId",
                table: "practice_task",
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
                name: "IX_plan_IsDeleted",
                table: "plan",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_plan_TenantId",
                table: "plan",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_perio_tooth_reading_IsDeleted",
                table: "perio_tooth_reading",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_perio_tooth_reading_TenantId",
                table: "perio_tooth_reading",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_perio_site_reading_IsDeleted",
                table: "perio_site_reading",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_perio_site_reading_TenantId",
                table: "perio_site_reading",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_perio_exam_IsDeleted",
                table: "perio_exam",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_perio_exam_TenantId",
                table: "perio_exam",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_IsDeleted",
                table: "payment",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_payment_TenantId",
                table: "payment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_document_IsDeleted",
                table: "patient_document",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_patient_document_TenantId",
                table: "patient_document",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_alert_IsDeleted",
                table: "patient_alert",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_patient_alert_TenantId",
                table: "patient_alert",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_IsDeleted",
                table: "patient",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_patient_LastName_FirstName",
                table: "patient",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_TenantId",
                table: "patient",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_code_IsDeleted",
                table: "password_reset_code",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_code_TenantId",
                table: "password_reset_code",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_operatory_IsDeleted",
                table: "operatory",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_operatory_TenantId",
                table: "operatory",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_settings_IsDeleted",
                table: "notification_settings",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_message_template_IsDeleted",
                table: "message_template",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_message_template_TenantId",
                table: "message_template",
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
                name: "IX_medical_history_form_IsDeleted",
                table: "medical_history_form",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_form_TenantId",
                table: "medical_history_form",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_answer_IsDeleted",
                table: "medical_history_answer",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_medical_history_answer_TenantId",
                table: "medical_history_answer",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_certificate_IsDeleted",
                table: "medical_certificate",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_medical_certificate_TenantId",
                table: "medical_certificate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_lab_case_IsDeleted",
                table: "lab_case",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_lab_case_TenantId",
                table: "lab_case",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_line_IsDeleted",
                table: "invoice_line",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_line_TenantId",
                table: "invoice_line",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_IsDeleted",
                table: "invoice",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_TenantId",
                table: "invoice",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_help_article_IsDeleted",
                table: "help_article",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_help_article_TenantId",
                table: "help_article",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_formulary_medicine_IsDeleted",
                table: "formulary_medicine",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_formulary_medicine_TenantId",
                table: "formulary_medicine",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_consent_template_IsDeleted",
                table: "consent_template",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_consent_template_TenantId",
                table: "consent_template",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_consent_form_IsDeleted",
                table: "consent_form",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_consent_form_TenantId",
                table: "consent_form",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_communication_log_IsDeleted",
                table: "communication_log",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_communication_log_TenantId",
                table: "communication_log",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_note_IsDeleted",
                table: "clinical_note",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_note_TenantId",
                table: "clinical_note",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_claim_IsDeleted",
                table: "claim",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_claim_TenantId",
                table: "claim",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_IsDeleted",
                table: "audit_entry",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_TenantId",
                table: "audit_entry",
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
                name: "IX_appointment_series_IsDeleted",
                table: "appointment_series",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_series_TenantId",
                table: "appointment_series",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_reminder_IsDeleted",
                table: "appointment_reminder",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_reminder_TenantId",
                table: "appointment_reminder",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_IsDeleted",
                table: "appointment",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_TenantId",
                table: "appointment",
                column: "TenantId");
        }
    }
}

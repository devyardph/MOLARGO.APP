using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DYS.Molargo.Api.Migrations
{
    /// <inheritdoc />
    public partial class CommunicationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "communication_log",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ClaimedBy",
                table: "communication_log",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedUtc",
                table: "communication_log",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptUtc",
                table: "communication_log",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_communication_log_TenantId_Status_NextAttemptUtc",
                table: "communication_log",
                columns: new[] { "TenantId", "Status", "NextAttemptUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_communication_log_TenantId_Status_NextAttemptUtc",
                table: "communication_log");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "communication_log");

            migrationBuilder.DropColumn(
                name: "ClaimedBy",
                table: "communication_log");

            migrationBuilder.DropColumn(
                name: "ClaimedUtc",
                table: "communication_log");

            migrationBuilder.DropColumn(
                name: "NextAttemptUtc",
                table: "communication_log");
        }
    }
}

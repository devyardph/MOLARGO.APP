using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DYS.Molargo.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropLocalMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_metadata");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}

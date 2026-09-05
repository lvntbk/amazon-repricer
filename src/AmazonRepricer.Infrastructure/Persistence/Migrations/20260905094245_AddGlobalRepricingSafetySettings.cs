using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmazonRepricer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalRepricingSafetySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repricing_safety_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    PriceUpdatesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repricing_safety_settings", x => x.Id);
                    table.CheckConstraint("CK_repricing_safety_settings_singleton", "\"Id\" = 1");
                });

            migrationBuilder.InsertData(
                table: "repricing_safety_settings",
                columns: new[]
                {
                    "Id",
                    "PriceUpdatesEnabled",
                    "UpdatedAtUtc"
                },
                values: new object[]
                {
                    1,
                    false,
                    new DateTime(
                        2026,
                        9,
                        5,
                        9,
                        42,
                        45,
                        DateTimeKind.Utc)
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repricing_safety_settings");
        }
    }
}

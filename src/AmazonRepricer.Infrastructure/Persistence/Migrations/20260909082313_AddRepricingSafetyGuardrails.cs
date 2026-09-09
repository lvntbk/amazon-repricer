using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmazonRepricer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepricingSafetyGuardrails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MaxPriceChangePercentage",
                table: "repricing_safety_settings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 10m);

            migrationBuilder.AddColumn<bool>(
                name: "AutomaticRepricingEnabled",
                table: "amazon_stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_repricing_safety_settings_max_price_change",
                table: "repricing_safety_settings",
                sql: "\"MaxPriceChangePercentage\" > 0 AND \"MaxPriceChangePercentage\" <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_repricing_safety_settings_max_price_change",
                table: "repricing_safety_settings");

            migrationBuilder.DropColumn(
                name: "MaxPriceChangePercentage",
                table: "repricing_safety_settings");

            migrationBuilder.DropColumn(
                name: "AutomaticRepricingEnabled",
                table: "amazon_stores");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmazonRepricer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepricingApplicationResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationError",
                table: "repricing_events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAtUtc",
                table: "repricing_events",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicationError",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "ProcessedAtUtc",
                table: "repricing_events");
        }
    }
}

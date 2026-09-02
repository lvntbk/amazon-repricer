using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmazonRepricer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAmazonSubmissionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AmazonSubmissionAccepted",
                table: "repricing_events",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AmazonSubmissionId",
                table: "repricing_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AmazonSubmissionIssues",
                table: "repricing_events",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReconciledAtUtc",
                table: "repricing_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "repricing_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_repricing_events_Status_AmazonSubmissionAccepted_SubmittedA~",
                table: "repricing_events",
                columns: new[] { "Status", "AmazonSubmissionAccepted", "SubmittedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_repricing_events_Status_AmazonSubmissionAccepted_SubmittedA~",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "AmazonSubmissionAccepted",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "AmazonSubmissionId",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "AmazonSubmissionIssues",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "ReconciledAtUtc",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "repricing_events");
        }
    }
}

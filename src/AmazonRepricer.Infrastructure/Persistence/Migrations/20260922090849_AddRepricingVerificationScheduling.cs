using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmazonRepricer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepricingVerificationScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastVerificationAttemptAtUtc",
                table: "repricing_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastVerificationReason",
                table: "repricing_events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextVerificationAttemptAtUtc",
                table: "repricing_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationAttemptCount",
                table: "repricing_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationLeaseExpiresAtUtc",
                table: "repricing_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerificationLeaseId",
                table: "repricing_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VerificationReviewRequired",
                table: "repricing_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_repricing_events_Status_VerificationReviewRequired_NextVeri~",
                table: "repricing_events",
                columns: new[] { "Status", "VerificationReviewRequired", "NextVerificationAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_repricing_events_Status_VerificationReviewRequired_NextVeri~",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "LastVerificationAttemptAtUtc",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "LastVerificationReason",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "NextVerificationAttemptAtUtc",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "VerificationAttemptCount",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "VerificationLeaseExpiresAtUtc",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "VerificationLeaseId",
                table: "repricing_events");

            migrationBuilder.DropColumn(
                name: "VerificationReviewRequired",
                table: "repricing_events");
        }
    }
}

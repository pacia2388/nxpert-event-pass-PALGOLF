using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventRegistration.Api.Migrations
{
    /// <inheritdoc />
    public partial class MailAttachmentsBatchesExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveredRecipientsJson",
                table: "MailPlanSends",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailFile",
                table: "MailPlanSends",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcludedEmailsJson",
                table: "MailPlans",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SendAt",
                table: "MailPlans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SendImmediately",
                table: "MailPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeliveredRecipientsJson",
                table: "InvitationEmails",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailFile",
                table: "InvitationEmails",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveredRecipientsJson",
                table: "MailPlanSends");

            migrationBuilder.DropColumn(
                name: "EmailFile",
                table: "MailPlanSends");

            migrationBuilder.DropColumn(
                name: "ExcludedEmailsJson",
                table: "MailPlans");

            migrationBuilder.DropColumn(
                name: "SendAt",
                table: "MailPlans");

            migrationBuilder.DropColumn(
                name: "SendImmediately",
                table: "MailPlans");

            migrationBuilder.DropColumn(
                name: "DeliveredRecipientsJson",
                table: "InvitationEmails");

            migrationBuilder.DropColumn(
                name: "EmailFile",
                table: "InvitationEmails");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventRegistration.Api.Migrations
{
    /// <inheritdoc />
    public partial class EventFeedbackAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Feedbacks_EventId_UserId",
                table: "Feedbacks");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Feedbacks",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "Feedbacks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FeedbackRecipientId",
                table: "Feedbacks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Improve",
                table: "Feedbacks",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAnonymous",
                table: "Feedbacks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationRating",
                table: "Feedbacks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WentWell",
                table: "Feedbacks",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EventFeedbackCampaigns",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    PreparedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventFeedbackCampaigns", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_EventFeedbackCampaigns_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventFeedbackRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    RegistrationId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventFeedbackRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventFeedbackRecipients_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventFeedbackRecipients_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Feedbacks_EventId_FeedbackRecipientId",
                table: "Feedbacks",
                columns: new[] { "EventId", "FeedbackRecipientId" },
                unique: true,
                filter: "[FeedbackRecipientId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Feedbacks_EventId_UserId",
                table: "Feedbacks",
                columns: new[] { "EventId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Feedbacks_FeedbackRecipientId",
                table: "Feedbacks",
                column: "FeedbackRecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_Feedbacks_UserId",
                table: "Feedbacks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventFeedbackRecipients_EventId_RegistrationId",
                table: "EventFeedbackRecipients",
                columns: new[] { "EventId", "RegistrationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventFeedbackRecipients_RegistrationId",
                table: "EventFeedbackRecipients",
                column: "RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_EventFeedbackRecipients_Token",
                table: "EventFeedbackRecipients",
                column: "Token",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Feedbacks_EventFeedbackRecipients_FeedbackRecipientId",
                table: "Feedbacks",
                column: "FeedbackRecipientId",
                principalTable: "EventFeedbackRecipients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Feedbacks_Users_UserId",
                table: "Feedbacks",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Feedbacks_EventFeedbackRecipients_FeedbackRecipientId",
                table: "Feedbacks");

            migrationBuilder.DropForeignKey(
                name: "FK_Feedbacks_Users_UserId",
                table: "Feedbacks");

            migrationBuilder.DropTable(
                name: "EventFeedbackCampaigns");

            migrationBuilder.DropTable(
                name: "EventFeedbackRecipients");

            migrationBuilder.DropIndex(
                name: "IX_Feedbacks_EventId_FeedbackRecipientId",
                table: "Feedbacks");

            migrationBuilder.DropIndex(
                name: "IX_Feedbacks_EventId_UserId",
                table: "Feedbacks");

            migrationBuilder.DropIndex(
                name: "IX_Feedbacks_FeedbackRecipientId",
                table: "Feedbacks");

            migrationBuilder.DropIndex(
                name: "IX_Feedbacks_UserId",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "FeedbackRecipientId",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "Improve",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "IsAnonymous",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "OrganizationRating",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "WentWell",
                table: "Feedbacks");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Feedbacks",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Feedbacks_EventId_UserId",
                table: "Feedbacks",
                columns: new[] { "EventId", "UserId" },
                unique: true);
        }
    }
}

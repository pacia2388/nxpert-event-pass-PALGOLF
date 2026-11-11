using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventRegistration.Api.Migrations
{
    /// <inheritdoc />
    public partial class TeamMemberRegistrationNotice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotificationFlag",
                table: "TeamMembers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RegistrationToken",
                table: "TeamMembers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegistrationTokenExpiresAt",
                table: "TeamMembers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_RegistrationToken",
                table: "TeamMembers",
                column: "RegistrationToken",
                unique: true,
                filter: "[RegistrationToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamMembers_RegistrationToken",
                table: "TeamMembers");

            migrationBuilder.DropColumn(
                name: "NotificationFlag",
                table: "TeamMembers");

            migrationBuilder.DropColumn(
                name: "RegistrationToken",
                table: "TeamMembers");

            migrationBuilder.DropColumn(
                name: "RegistrationTokenExpiresAt",
                table: "TeamMembers");
        }
    }
}

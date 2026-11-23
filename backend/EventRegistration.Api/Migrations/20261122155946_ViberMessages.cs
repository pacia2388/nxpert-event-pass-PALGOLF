using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventRegistration.Api.Migrations
{
    /// <inheritdoc />
    public partial class ViberMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ViberMessages",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RegistrationId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EmailQueuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmailFile = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViberMessages", x => x.MessageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ViberMessages_AcceptedAt",
                table: "ViberMessages",
                column: "AcceptedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ViberMessages_GroupId",
                table: "ViberMessages",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ViberMessages");
        }
    }
}

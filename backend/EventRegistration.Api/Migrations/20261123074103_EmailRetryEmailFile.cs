using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventRegistration.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmailRetryEmailFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailFile",
                table: "EmailRetries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailRetries_EmailFile",
                table: "EmailRetries",
                column: "EmailFile");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailRetries_EmailFile",
                table: "EmailRetries");

            migrationBuilder.DropColumn(
                name: "EmailFile",
                table: "EmailRetries");
        }
    }
}

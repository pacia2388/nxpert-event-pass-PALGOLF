using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EventRegistration.Api.Migrations
{
    /// <inheritdoc />
    public partial class PhoneCountries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhoneCountries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryCode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneCountries", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PhoneCountries",
                columns: new[] { "Id", "Channel", "CountryCode" },
                values: new object[,]
                {
                    { 1, "Viber", "63" },
                    { 2, "Sms", "63" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneCountries_CountryCode_Channel",
                table: "PhoneCountries",
                columns: new[] { "CountryCode", "Channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneCountries");
        }
    }
}

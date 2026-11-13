using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventRegistration.Api.Migrations
{
    /// <summary>
    /// PhoneCountries: one row per code with a Viber and an Sms flag (was one row per code + channel).
    /// Existing rows are merged per code, keeping the lowest Id, so admin-added codes survive.
    /// </summary>
    public partial class PhoneCountryChannelColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhoneCountries_CountryCode_Channel",
                table: "PhoneCountries");

            migrationBuilder.AddColumn<bool>(
                name: "Sms",
                table: "PhoneCountries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Viber",
                table: "PhoneCountries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE p SET
                    Viber = CASE WHEN EXISTS (SELECT 1 FROM PhoneCountries x WHERE x.CountryCode = p.CountryCode AND x.Channel = 'Viber') THEN 1 ELSE 0 END,
                    Sms   = CASE WHEN EXISTS (SELECT 1 FROM PhoneCountries x WHERE x.CountryCode = p.CountryCode AND x.Channel = 'Sms') THEN 1 ELSE 0 END
                FROM PhoneCountries p;
                DELETE p FROM PhoneCountries p
                WHERE EXISTS (SELECT 1 FROM PhoneCountries x WHERE x.CountryCode = p.CountryCode AND x.Id < p.Id);
                """);

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "PhoneCountries");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneCountries_CountryCode",
                table: "PhoneCountries",
                column: "CountryCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhoneCountries_CountryCode",
                table: "PhoneCountries");

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "PhoneCountries",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            // Viber rows keep their Id; Sms gets new rows; codes with neither channel are dropped.
            migrationBuilder.Sql("""
                INSERT INTO PhoneCountries (CountryCode, Channel, Viber, Sms)
                    SELECT CountryCode, 'Sms', 0, 0 FROM PhoneCountries WHERE Sms = 1 AND Viber = 1;
                UPDATE PhoneCountries SET Channel = CASE WHEN Viber = 1 THEN 'Viber' WHEN Sms = 1 THEN 'Sms' ELSE Channel END
                    WHERE Channel = '';
                DELETE FROM PhoneCountries WHERE Channel = '';
                """);

            migrationBuilder.DropColumn(
                name: "Sms",
                table: "PhoneCountries");

            migrationBuilder.DropColumn(
                name: "Viber",
                table: "PhoneCountries");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneCountries_CountryCode_Channel",
                table: "PhoneCountries",
                columns: new[] { "CountryCode", "Channel" },
                unique: true);
        }
    }
}

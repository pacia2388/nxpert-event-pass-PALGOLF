using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventRegistration.Api.Migrations
{
    /// <summary>The Team Lead role is renamed: stored value "TL" becomes "Captain".</summary>
    public partial class RenameTeamLeadRoleToCaptain : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Users] SET [Role] = N'Captain' WHERE [Role] = N'TL';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Users] SET [Role] = N'TL' WHERE [Role] = N'Captain';");
        }
    }
}

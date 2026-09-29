using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFlux.Zen.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDecoyRedirectUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecoyRedirectUrl",
                table: "Settings",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecoyRedirectUrl",
                table: "Settings");
        }
    }
}

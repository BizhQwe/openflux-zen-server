using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFlux.Zen.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLanguageSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Settings");
        }
    }
}

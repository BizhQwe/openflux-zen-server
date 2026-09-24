using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenFlux.Zen.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SecretPath = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ListenHost = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, defaultValue: "127.0.0.1"),
                    ListenPort = table.Column<int>(type: "INTEGER", nullable: false),
                    PublicUrl = table.Column<string>(type: "TEXT", nullable: true),
                    PublishMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "local"),
                    Domain = table.Column<string>(type: "TEXT", nullable: true),
                    ZrokToken = table.Column<string>(type: "TEXT", nullable: true),
                    ZrokShareUrl = table.Column<string>(type: "TEXT", nullable: true),
                    AutoStartEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tunnels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "exit"),
                    Transport = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "yandex"),
                    Inbound = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Socks5Address = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "l4"),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    MaxToken = table.Column<string>(type: "TEXT", nullable: true),
                    MaxUid = table.Column<string>(type: "TEXT", nullable: true),
                    LocalIp = table.Column<string>(type: "TEXT", nullable: true),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "batched"),
                    EncryptionKey = table.Column<string>(type: "TEXT", nullable: true),
                    BenchBytes = table.Column<int>(type: "INTEGER", nullable: false),
                    BenchCompressible = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExtraArgs = table.Column<string>(type: "TEXT", nullable: true),
                    ClientLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    TrafficLimitBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    UploadBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    DownloadBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ConnectedClients = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastStoppedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RestartAttempts = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tunnels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tunnels_IsEnabled",
                table: "Tunnels",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_Tunnels_Name",
                table: "Tunnels",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Tunnels");
        }
    }
}

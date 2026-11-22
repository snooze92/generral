using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Generral.Core.Datastore.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Metadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Implementation = table.Column<string>(type: "TEXT", nullable: false),
                    Enable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Settings = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Metadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeFileId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    Consumer = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataFiles_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Implementation = table.Column<string>(type: "TEXT", nullable: false),
                    Settings = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    OnGrab = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnDownload = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnUpgrade = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnSeriesAdd = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnSeriesDelete = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnEpisodeFileDelete = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnEpisodeFileDeleteForUpgrade = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Metadata_Name",
                table: "Metadata",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataFiles_SeriesId",
                table: "MetadataFiles",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Name",
                table: "Notifications",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Metadata");

            migrationBuilder.DropTable(
                name: "MetadataFiles");

            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}

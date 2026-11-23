using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Generral.Core.Datastore.Migrations
{
    /// <summary>Plex NFO is enabled by default (spec 10 § Settings → Metadata), like Sonarr's Kodi consumer is present out of the box.</summary>
    public partial class SeedPlexNfoMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO "Metadata" ("Name", "Implementation", "Enable", "Settings")
                SELECT 'Plex NFO', 'PlexNfo', 1, '{}'
                WHERE NOT EXISTS (SELECT 1 FROM "Metadata");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

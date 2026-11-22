using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Generral.Core.Datastore.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Language",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "QualityProfiles");
        }
    }
}

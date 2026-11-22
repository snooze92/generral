using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Generral.Core.Datastore.Migrations
{
    /// <inheritdoc />
    public partial class AddHealthNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeHealthWarnings",
                table: "Notifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OnHealthIssue",
                table: "Notifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OnHealthRestored",
                table: "Notifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludeHealthWarnings",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "OnHealthIssue",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "OnHealthRestored",
                table: "Notifications");
        }
    }
}

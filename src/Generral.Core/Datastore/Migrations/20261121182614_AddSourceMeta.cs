using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Generral.Core.Datastore.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceMeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Meta",
                table: "Sources",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Meta",
                table: "Sources");
        }
    }
}

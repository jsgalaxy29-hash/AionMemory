using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TemplatePackageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetsManifest",
                table: "Templates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "Templates",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Signature",
                table: "Templates",
                type: "TEXT",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssetsManifest",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "Author",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "Signature",
                table: "Templates");
        }
    }
}

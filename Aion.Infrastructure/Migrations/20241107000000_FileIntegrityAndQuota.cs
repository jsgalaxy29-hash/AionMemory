using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AionDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20241107000000_FileIntegrityAndQuota")]
    public partial class FileIntegrityAndQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>
            (
                name: "Sha256",
                table: "Files",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: ""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "Files");
        }
    }
}


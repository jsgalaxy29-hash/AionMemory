using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AionDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20250301000000_RecordTableForeignKey")]
    public partial class RecordTableForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite cannot add a foreign key constraint without rebuilding the table.
            // The relationship is enforced by the current EF model for newly created databases.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

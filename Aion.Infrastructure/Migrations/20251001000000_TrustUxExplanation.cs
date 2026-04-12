using Microsoft.EntityFrameworkCore.Migrations;

namespace Aion.Infrastructure.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AionDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20251001000000_TrustUxExplanation")]
public partial class TrustUxExplanation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExplanationJson",
            table: "MemoryInsights",
            type: "TEXT",
            nullable: false,
            defaultValue: "{\"sources\":[],\"rules\":[]}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ExplanationJson",
            table: "MemoryInsights");
    }
}


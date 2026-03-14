using Microsoft.EntityFrameworkCore.Migrations;

namespace Aion.Infrastructure.Migrations;

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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations;

public partial class SemanticLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CreatedBy",
            table: "Links",
            type: "TEXT",
            nullable: false,
            defaultValue: Guid.Parse("00000000-0000-0000-0000-000000000001"));

        migrationBuilder.AddColumn<string>(
            name: "Reason",
            table: "Links",
            type: "TEXT",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Type",
            table: "Links",
            type: "TEXT",
            maxLength: 64,
            nullable: false,
            defaultValue: "history");

        migrationBuilder.CreateIndex(
            name: "IX_Links_SourceId_TargetId_Type",
            table: "Links",
            columns: new[] { "SourceId", "TargetId", "Type" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Links_SourceId_TargetId_Type",
            table: "Links");

        migrationBuilder.DropColumn(
            name: "CreatedBy",
            table: "Links");

        migrationBuilder.DropColumn(
            name: "Reason",
            table: "Links");

        migrationBuilder.DropColumn(
            name: "Type",
            table: "Links");
    }
}

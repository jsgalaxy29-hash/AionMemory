using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgendaRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecurrenceCount",
                table: "Events",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceFrequency",
                table: "Events",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceInterval",
                table: "Events",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecurrenceUntil",
                table: "Events",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecurrenceCount",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "RecurrenceFrequency",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "RecurrenceInterval",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "RecurrenceUntil",
                table: "Events");
        }
    }
}

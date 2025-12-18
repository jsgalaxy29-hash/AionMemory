using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MetamodelEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tables",
                type: "TEXT",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultView",
                table: "Tables",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasAuditTrail",
                table: "Tables",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "Tables",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RowLabelTemplate",
                table: "Tables",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsSoftDelete",
                table: "Tables",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "Visualization",
                table: "TableViews",
                type: "TEXT",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "QueryDefinition",
                table: "TableViews",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 4000);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "TableViews",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "TableViews",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FilterExpression",
                table: "TableViews",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "TableViews",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PageSize",
                table: "TableViews",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SortExpression",
                table: "TableViews",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LookupTarget",
                table: "TableFields",
                type: "TEXT",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DefaultValue",
                table: "TableFields",
                type: "TEXT",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DataType",
                table: "TableFields",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<string>(
                name: "ComputedExpression",
                table: "TableFields",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LookupField",
                table: "TableFields",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxValue",
                table: "TableFields",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxLength",
                table: "TableFields",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinLength",
                table: "TableFields",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinValue",
                table: "TableFields",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsComputed",
                table: "TableFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFilterable",
                table: "TableFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "TableFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsIndexed",
                table: "TableFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrimaryKey",
                table: "TableFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReadOnly",
                table: "TableFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSearchable",
                table: "TableFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSortable",
                table: "TableFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsUnique",
                table: "TableFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Placeholder",
                table: "TableFields",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "TableFields",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationPattern",
                table: "TableFields",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultView",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "HasAuditTrail",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "RowLabelTemplate",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "SupportsSoftDelete",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "TableViews");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "TableViews");

            migrationBuilder.DropColumn(
                name: "FilterExpression",
                table: "TableViews");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "TableViews");

            migrationBuilder.DropColumn(
                name: "PageSize",
                table: "TableViews");

            migrationBuilder.DropColumn(
                name: "SortExpression",
                table: "TableViews");

            migrationBuilder.DropColumn(
                name: "ComputedExpression",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "LookupField",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "MaxValue",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "MaxLength",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "MinLength",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "MinValue",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "IsComputed",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "IsFilterable",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "IsIndexed",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "IsPrimaryKey",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "IsReadOnly",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "IsSearchable",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "IsSortable",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "IsUnique",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "Placeholder",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "ValidationPattern",
                table: "TableFields");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tables",
                type: "TEXT",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Visualization",
                table: "TableViews",
                type: "TEXT",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "QueryDefinition",
                table: "TableViews",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<string>(
                name: "LookupTarget",
                table: "TableFields",
                type: "TEXT",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DefaultValue",
                table: "TableFields",
                type: "TEXT",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DataType",
                table: "TableFields",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 32);
        }
    }
}

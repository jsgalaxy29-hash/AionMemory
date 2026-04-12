using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AionDbContext))]
    [Migration("20260324000000_ModelAlignment")]
    public partial class ModelAlignment : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workspaces_TenantId",
                table: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_TableViews_TableId",
                table: "TableViews");

            migrationBuilder.DropIndex(
                name: "IX_TableFields_TableId",
                table: "TableFields");

            migrationBuilder.DropIndex(
                name: "IX_Profiles_WorkspaceId",
                table: "Profiles");

            migrationBuilder.DropIndex(
                name: "IX_Links_SourceId",
                table: "Links");

            migrationBuilder.RenameTable(
                name: "SemanticSearchEntries",
                newName: "SemanticSearch");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Tables",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");



            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "TableFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);


            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "EntityTypes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditLogs_WorkspaceId",
                table: "SecurityAuditLogs",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_AiCallLogs_WorkspaceId",
                table: "AiCallLogs",
                column: "WorkspaceId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SecurityAuditLogs_WorkspaceId",
                table: "SecurityAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AiCallLogs_WorkspaceId",
                table: "AiCallLogs");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "EnumValues",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "IsListVisible",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "RelationTargetEntityTypeId",
                table: "TableFields");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "EntityTypes");

            migrationBuilder.RenameTable(
                name: "SemanticSearch",
                newName: "SemanticSearchEntries");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_TenantId",
                table: "Workspaces",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TableViews_TableId",
                table: "TableViews",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_TableFields_TableId",
                table: "TableFields",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_WorkspaceId",
                table: "Profiles",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_SourceId",
                table: "Links",
                column: "SourceId");
        }
    }
}




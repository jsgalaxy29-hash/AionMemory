using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenancyPartitioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workspaces_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Initials = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                    AccentColor = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Profiles_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "AutomationActions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "AutomationConditions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "AutomationExecutions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Widgets",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Files",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "FileLinks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Records",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "RecordIndexes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "RecordAudits",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Embeddings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "EventLinks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "NoteLinks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Marketplace",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Predictions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "MemoryInsights",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "KnowledgeEdges",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "KnowledgeNodes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "SemanticSearchEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "AutomationRules",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "EntityTypes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Fields",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "HistoryEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Links",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Modules",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Notes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Relations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Reports",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Tables",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "VisionAnalyses",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "TableFields",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "TableViews",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Permissions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Roles",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Templates",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Personas",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Events",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67"));

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Name",
                table: "Tenants",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_TenantId",
                table: "Workspaces",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_TenantId_Name",
                table: "Workspaces",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_WorkspaceId",
                table: "Profiles",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_WorkspaceId_DisplayName",
                table: "Profiles",
                columns: new[] { "WorkspaceId", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationActions_WorkspaceId",
                table: "AutomationActions",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_AutomationConditions_WorkspaceId",
                table: "AutomationConditions",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_AutomationExecutions_WorkspaceId",
                table: "AutomationExecutions",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Widgets_WorkspaceId",
                table: "Widgets",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Files_WorkspaceId",
                table: "Files",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_FileLinks_WorkspaceId",
                table: "FileLinks",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Records_WorkspaceId",
                table: "Records",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_RecordIndexes_WorkspaceId",
                table: "RecordIndexes",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_RecordAudits_WorkspaceId",
                table: "RecordAudits",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Embeddings_WorkspaceId",
                table: "Embeddings",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_EventLinks_WorkspaceId",
                table: "EventLinks",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_NoteLinks_WorkspaceId",
                table: "NoteLinks",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Marketplace_WorkspaceId",
                table: "Marketplace",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Predictions_WorkspaceId",
                table: "Predictions",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_MemoryInsights_WorkspaceId",
                table: "MemoryInsights",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeEdges_WorkspaceId",
                table: "KnowledgeEdges",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodes_WorkspaceId",
                table: "KnowledgeNodes",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_SemanticSearchEntries_WorkspaceId",
                table: "SemanticSearchEntries",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_WorkspaceId",
                table: "AutomationRules",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_EntityTypes_WorkspaceId",
                table: "EntityTypes",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Fields_WorkspaceId",
                table: "Fields",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_HistoryEvents_WorkspaceId",
                table: "HistoryEvents",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Links_WorkspaceId",
                table: "Links",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Modules_WorkspaceId",
                table: "Modules",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Notes_WorkspaceId",
                table: "Notes",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Relations_WorkspaceId",
                table: "Relations",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Reports_WorkspaceId",
                table: "Reports",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Tables_WorkspaceId",
                table: "Tables",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_VisionAnalyses_WorkspaceId",
                table: "VisionAnalyses",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_TableFields_WorkspaceId",
                table: "TableFields",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_TableViews_WorkspaceId",
                table: "TableViews",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Permissions_WorkspaceId",
                table: "Permissions",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Roles_WorkspaceId",
                table: "Roles",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Templates_WorkspaceId",
                table: "Templates",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Personas_WorkspaceId",
                table: "Personas",
                column: "WorkspaceId");
            migrationBuilder.CreateIndex(
                name: "IX_Events_WorkspaceId",
                table: "Events",
                column: "WorkspaceId");

            migrationBuilder.DropIndex(
                name: "IX_Tables_Name",
                table: "Tables");

            migrationBuilder.DropIndex(
                name: "IX_SemanticSearchEntries_TargetType_TargetId",
                table: "SemanticSearchEntries");

            migrationBuilder.DropIndex(
                name: "IX_Roles_UserId_Kind",
                table: "Roles");

            migrationBuilder.CreateIndex(
                name: "IX_Tables_WorkspaceId_Name",
                table: "Tables",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticSearchEntries_WorkspaceId_TargetType_TargetId",
                table: "SemanticSearchEntries",
                columns: new[] { "WorkspaceId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_WorkspaceId_UserId_Kind",
                table: "Roles",
                columns: new[] { "WorkspaceId", "UserId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tables_WorkspaceId_Name",
                table: "Tables");

            migrationBuilder.DropIndex(
                name: "IX_SemanticSearchEntries_WorkspaceId_TargetType_TargetId",
                table: "SemanticSearchEntries");

            migrationBuilder.DropIndex(
                name: "IX_Roles_WorkspaceId_UserId_Kind",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_AutomationActions_WorkspaceId",
                table: "AutomationActions");
            migrationBuilder.DropIndex(
                name: "IX_AutomationConditions_WorkspaceId",
                table: "AutomationConditions");
            migrationBuilder.DropIndex(
                name: "IX_AutomationExecutions_WorkspaceId",
                table: "AutomationExecutions");
            migrationBuilder.DropIndex(
                name: "IX_Widgets_WorkspaceId",
                table: "Widgets");
            migrationBuilder.DropIndex(
                name: "IX_Files_WorkspaceId",
                table: "Files");
            migrationBuilder.DropIndex(
                name: "IX_FileLinks_WorkspaceId",
                table: "FileLinks");
            migrationBuilder.DropIndex(
                name: "IX_Records_WorkspaceId",
                table: "Records");
            migrationBuilder.DropIndex(
                name: "IX_RecordIndexes_WorkspaceId",
                table: "RecordIndexes");
            migrationBuilder.DropIndex(
                name: "IX_RecordAudits_WorkspaceId",
                table: "RecordAudits");
            migrationBuilder.DropIndex(
                name: "IX_Embeddings_WorkspaceId",
                table: "Embeddings");
            migrationBuilder.DropIndex(
                name: "IX_EventLinks_WorkspaceId",
                table: "EventLinks");
            migrationBuilder.DropIndex(
                name: "IX_NoteLinks_WorkspaceId",
                table: "NoteLinks");
            migrationBuilder.DropIndex(
                name: "IX_Marketplace_WorkspaceId",
                table: "Marketplace");
            migrationBuilder.DropIndex(
                name: "IX_Predictions_WorkspaceId",
                table: "Predictions");
            migrationBuilder.DropIndex(
                name: "IX_MemoryInsights_WorkspaceId",
                table: "MemoryInsights");
            migrationBuilder.DropIndex(
                name: "IX_KnowledgeEdges_WorkspaceId",
                table: "KnowledgeEdges");
            migrationBuilder.DropIndex(
                name: "IX_KnowledgeNodes_WorkspaceId",
                table: "KnowledgeNodes");
            migrationBuilder.DropIndex(
                name: "IX_SemanticSearchEntries_WorkspaceId",
                table: "SemanticSearchEntries");
            migrationBuilder.DropIndex(
                name: "IX_AutomationRules_WorkspaceId",
                table: "AutomationRules");
            migrationBuilder.DropIndex(
                name: "IX_EntityTypes_WorkspaceId",
                table: "EntityTypes");
            migrationBuilder.DropIndex(
                name: "IX_Fields_WorkspaceId",
                table: "Fields");
            migrationBuilder.DropIndex(
                name: "IX_HistoryEvents_WorkspaceId",
                table: "HistoryEvents");
            migrationBuilder.DropIndex(
                name: "IX_Links_WorkspaceId",
                table: "Links");
            migrationBuilder.DropIndex(
                name: "IX_Modules_WorkspaceId",
                table: "Modules");
            migrationBuilder.DropIndex(
                name: "IX_Notes_WorkspaceId",
                table: "Notes");
            migrationBuilder.DropIndex(
                name: "IX_Relations_WorkspaceId",
                table: "Relations");
            migrationBuilder.DropIndex(
                name: "IX_Reports_WorkspaceId",
                table: "Reports");
            migrationBuilder.DropIndex(
                name: "IX_Tables_WorkspaceId",
                table: "Tables");
            migrationBuilder.DropIndex(
                name: "IX_VisionAnalyses_WorkspaceId",
                table: "VisionAnalyses");
            migrationBuilder.DropIndex(
                name: "IX_TableFields_WorkspaceId",
                table: "TableFields");
            migrationBuilder.DropIndex(
                name: "IX_TableViews_WorkspaceId",
                table: "TableViews");
            migrationBuilder.DropIndex(
                name: "IX_Permissions_WorkspaceId",
                table: "Permissions");
            migrationBuilder.DropIndex(
                name: "IX_Roles_WorkspaceId",
                table: "Roles");
            migrationBuilder.DropIndex(
                name: "IX_Templates_WorkspaceId",
                table: "Templates");
            migrationBuilder.DropIndex(
                name: "IX_Personas_WorkspaceId",
                table: "Personas");
            migrationBuilder.DropIndex(
                name: "IX_Events_WorkspaceId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "AutomationActions");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "AutomationConditions");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "AutomationExecutions");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Widgets");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Files");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "FileLinks");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Records");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "RecordIndexes");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "RecordAudits");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Embeddings");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "EventLinks");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "NoteLinks");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Marketplace");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Predictions");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "MemoryInsights");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "KnowledgeEdges");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "KnowledgeNodes");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "SemanticSearchEntries");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "AutomationRules");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "EntityTypes");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Fields");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "HistoryEvents");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Links");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Modules");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Notes");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Relations");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Reports");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Tables");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "VisionAnalyses");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "TableFields");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "TableViews");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Permissions");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Roles");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Templates");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Personas");
            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Events");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.CreateIndex(
                name: "IX_Tables_Name",
                table: "Tables",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticSearchEntries_TargetType_TargetId",
                table: "SemanticSearchEntries",
                columns: new[] { "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_UserId_Kind",
                table: "Roles",
                columns: new[] { "UserId", "Kind" },
                unique: true);
        }
    }
}

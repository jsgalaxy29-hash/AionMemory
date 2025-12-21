using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.ModuleBuilder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class ModuleBuilderTests
{
    [Fact]
    public async Task Create_simple_module_is_idempotent()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var validator = new ModuleValidator(context);
        var applier = new ModuleApplier(context, validator, NullLogger<ModuleApplier>.Instance);

        var spec = BuildSimpleSpec();
        await validator.ValidateAndThrowAsync(spec);

        await applier.ApplyAsync(spec);
        await applier.ApplyAsync(spec);

        var table = await context.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .SingleAsync(t => t.Name == "tasks");

        Assert.Equal(5, table.Fields.Count);
        Assert.Single(table.Views, v => v.Name == "list");
        Assert.NotNull(table.DefaultView);
        Assert.Contains(table.Views, v => v.IsDefault);
    }

    [Fact]
    public async Task Update_module_adds_field_and_view_is_idempotent()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var validator = new ModuleValidator(context);
        var applier = new ModuleApplier(context, validator, NullLogger<ModuleApplier>.Instance);

        var initialSpec = BuildSimpleSpec();
        await applier.ApplyAsync(initialSpec);

        var updatedSpec = BuildSimpleSpec();
        updatedSpec.Tables[0].Fields.Add(new FieldSpec
        {
            Slug = "priority",
            Label = "Priorité",
            DataType = ModuleFieldDataTypes.Number,
            IsFilterable = true,
            IsSortable = true
        });
        updatedSpec.Tables[0].Views.Add(new ViewSpec
        {
            Slug = "form",
            DisplayName = "Formulaire",
            Visualization = "form",
            IsDefault = false
        });

        await applier.ApplyAsync(updatedSpec);
        await applier.ApplyAsync(updatedSpec);

        var table = await context.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .SingleAsync(t => t.Name == "tasks");

        Assert.Contains(table.Fields, f => f.Name == "priority" && f.IsSortable && f.IsFilterable);
        Assert.Contains(table.Views, v => v.Name == "form");
        Assert.Equal(6, table.Fields.Count);
        Assert.Equal(2, table.Views.Count);
    }

    [Fact]
    public async Task Validate_requires_fields_and_labels()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var validator = new ModuleValidator(context);
        var spec = new ModuleSpec
        {
            Slug = "invalid-module",
            Tables =
            {
                new TableSpec
                {
                    Slug = "empty",
                    Fields = new List<FieldSpec>()
                }
            }
        };

        var result = await validator.ValidateAsync(spec);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one field", StringComparison.OrdinalIgnoreCase));

        spec.Tables[0].Fields.Add(new FieldSpec { Slug = "title", Label = string.Empty, DataType = ModuleFieldDataTypes.Text });
        result = await validator.ValidateAsync(spec);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Label", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Apply_forces_single_default_view()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var validator = new ModuleValidator(context);
        var applier = new ModuleApplier(context, validator, NullLogger<ModuleApplier>.Instance);

        var spec = BuildSimpleSpec();
        spec.Tables[0].Views = new List<ViewSpec>
        {
            new() { Slug = "all", DisplayName = "All", IsDefault = true },
            new() { Slug = "board", DisplayName = "Board", IsDefault = false, Visualization = "board" }
        };

        await applier.ApplyAsync(spec);

        var table = await context.Tables.Include(t => t.Views).SingleAsync(t => t.Name == "tasks");
        var board = table.Views.Single(v => v.Name == "board");
        board.IsDefault = true;
        await context.SaveChangesAsync();

        await applier.ApplyAsync(spec);

        table = await context.Tables.Include(t => t.Views).SingleAsync(t => t.Name == "tasks");
        Assert.Single(table.Views, v => v.IsDefault);
        Assert.Equal("all", table.DefaultView);
        Assert.Contains(table.Views, v => v.Name == "board" && !v.IsDefault);
    }

    private static ModuleSpec BuildSimpleSpec()
        => new()
        {
            Slug = "aion-module",
            Tables =
            {
                new TableSpec
                {
                    Slug = "tasks",
                    DisplayName = "Tâches",
                    Description = "Module de tâches",
                    Fields = new List<FieldSpec>
                    {
                        new() { Slug = "title", Label = "Titre", DataType = ModuleFieldDataTypes.Text, IsRequired = true, IsSearchable = true, IsListVisible = true },
                        new() { Slug = "status", Label = "Statut", DataType = ModuleFieldDataTypes.Enum, EnumValues = new List<string> { "todo", "doing", "done" }, IsFilterable = true },
                        new() { Slug = "assignee", Label = "Assigné à", DataType = ModuleFieldDataTypes.Text, IsSearchable = true },
                        new() { Slug = "dueDate", Label = "Échéance", DataType = ModuleFieldDataTypes.Date },
                        new() { Slug = "estimatedHours", Label = "Heures estimées", DataType = ModuleFieldDataTypes.Decimal, MinValue = 0 }
                    },
                    Views = new List<ViewSpec>
                    {
                        new()
                        {
                            Slug = "list",
                            DisplayName = "Liste",
                            Filter = new Dictionary<string, string?> { ["status"] = "todo" },
                            Sort = "dueDate asc",
                            IsDefault = true
                        }
                    }
                }
            }
        };
}

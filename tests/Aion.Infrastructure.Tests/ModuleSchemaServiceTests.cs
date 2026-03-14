using System;
using System.Text.Json;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.ModuleBuilder;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class ModuleSchemaServiceTests
{
    [Fact]
    public async Task CreateModuleAsync_creates_table_and_fields_and_is_readable_via_metadata_services()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();

        var tableMetadataService = new TableMetadataService(context);
        var fieldMetadataService = new FieldMetadataService(context);
        var validator = new ModuleValidator(context, NullLogger<ModuleValidator>.Instance);
        var schemaService = new ModuleSchemaService(
            tableMetadataService,
            fieldMetadataService,
            validator,
            NullLogger<ModuleSchemaService>.Instance);

        var spec = new ModuleSpec
        {
            Version = ModuleSpecVersions.V1,
            Slug = "crm",
            DisplayName = "CRM",
            Tables =
            [
                new TableSpec
                {
                    Slug = "contacts",
                    DisplayName = "Contacts",
                    Description = "Référentiel contacts",
                    Fields =
                    [
                        new FieldSpec
                        {
                            Slug = "fullName",
                            Label = "Nom complet",
                            DataType = ModuleFieldDataTypes.Text,
                            IsRequired = true,
                            DefaultValue = JsonSerializer.SerializeToElement("Inconnu")
                        },
                        new FieldSpec
                        {
                            Slug = "age",
                            Label = "Âge",
                            DataType = ModuleFieldDataTypes.Number,
                            IsRequired = false,
                            DefaultValue = JsonSerializer.SerializeToElement(0)
                        }
                    ]
                }
            ]
        };

        var createdTable = await schemaService.CreateModuleAsync(spec);

        Assert.NotEqual(Guid.Empty, createdTable.Id);
        Assert.Equal("contacts", createdTable.Name);

        var storedTable = await tableMetadataService.GetByIdAsync(createdTable.Id);
        Assert.NotNull(storedTable);
        Assert.Equal("Contacts", storedTable!.DisplayName);

        var storedFields = await fieldMetadataService.GetByTableAsync(createdTable.Id);
        Assert.Equal(2, storedFields.Count);

        var fullNameField = Assert.Single(storedFields, f => f.Name == "fullName");
        Assert.Equal(FieldDataType.Text, fullNameField.DataType);
        Assert.True(fullNameField.IsRequired);
        Assert.Equal("Inconnu", fullNameField.DefaultValue);
        Assert.Equal(1, fullNameField.Order);

        var ageField = Assert.Single(storedFields, f => f.Name == "age");
        Assert.Equal(FieldDataType.Number, ageField.DataType);
        Assert.False(ageField.IsRequired);
        Assert.Equal("0", ageField.DefaultValue);
        Assert.Equal(2, ageField.Order);
    }
}

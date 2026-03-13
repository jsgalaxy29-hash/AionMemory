using System;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class MetamodelMetadataServicesTests
{
    [Fact]
    public async Task Table_and_field_metadata_services_support_basic_crud_flow()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();

        var tableService = new TableMetadataService(context);
        var fieldService = new FieldMetadataService(context);

        var created = await tableService.CreateAsync(new STable
        {
            Name = "customer",
            DisplayName = "Client",
            Description = "Fiche client"
        });

        Assert.NotEqual(Guid.Empty, created.Id);

        var field = await fieldService.AddFieldAsync(created.Id, new SFieldDefinition
        {
            Name = "firstName",
            Label = "Prénom",
            DataType = FieldDataType.Text,
            IsRequired = true,
            Order = 1
        });

        Assert.Equal(created.Id, field.TableId);

        var reloadedTable = await tableService.GetByIdAsync(created.Id);
        Assert.NotNull(reloadedTable);
        Assert.Contains(reloadedTable!.Fields, f => f.Name == "firstName");

        var fields = await fieldService.GetByTableAsync(created.Id);
        Assert.Single(fields);
        Assert.Equal("firstName", fields[0].Name);
    }
}

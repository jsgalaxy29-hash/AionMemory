using System.IO;
using System.Text.Json;
using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services;
using Aion.Infrastructure.Services.Automation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class AutomationRuleEngineTests
{
    [Fact]
    public async Task Rule_tags_record_and_creates_outputs_for_factures()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;
        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();

        var module = new S_Module { Name = "Automation" };
        await context.Modules.AddAsync(module);
        await context.SaveChangesAsync();

        var table = new STable
        {
            Name = "invoices",
            DisplayName = "Invoices",
            Fields =
            {
                new SFieldDefinition { Name = "Title", Label = "Title", DataType = FieldDataType.Text, IsRequired = true },
                new SFieldDefinition { Name = "Tags", Label = "Tags", DataType = FieldDataType.Tags },
                new SFieldDefinition { Name = "Status", Label = "Status", DataType = FieldDataType.Text }
            }
        };

        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var rule = new S_AutomationRule
        {
            ModuleId = module.Id,
            Name = "Tag facture",
            Trigger = AutomationTriggerType.OnCreate,
            TriggerFilter = "record.created",
            Conditions =
            {
                new AutomationCondition
                {
                    Expression = JsonSerializer.Serialize(
                        new AutomationConditionDefinition("data.Title", AutomationConditionOperator.Contains, "facture"),
                        serializerOptions)
                }
            },
            Actions =
            {
                new AutomationAction
                {
                    ActionType = AutomationActionType.Tag,
                    ParametersJson = "{\"tag\":\"finance\",\"field\":\"Tags\"}"
                },
                new AutomationAction
                {
                    ActionType = AutomationActionType.UpdateField,
                    ParametersJson = "{\"field\":\"Status\",\"value\":\"Nouvelle\"}"
                },
                new AutomationAction
                {
                    ActionType = AutomationActionType.CreateNote,
                    ParametersJson = "{\"title\":\"Nouvelle facture\"}"
                },
                new AutomationAction
                {
                    ActionType = AutomationActionType.ScheduleReminder,
                    ParametersJson = "{\"title\":\"Payer la facture\",\"start\":\"2025-01-01T10:00:00Z\",\"reminderAt\":\"2024-12-31T10:00:00Z\"}"
                }
            }
        };

        await context.AutomationRules.AddAsync(rule);
        await context.SaveChangesAsync();

        var search = new NullSearchService();
        var notes = new RecordingNoteService();
        var agenda = new RecordingAgendaService();
        var operationScopeFactory = new OperationScopeFactory();

        AionDataEngine? dataEngine = null;
        var automationEngine = new AutomationRuleEngine(
            context,
            notes,
            agenda,
            () => dataEngine!,
            NullLogger<AutomationRuleEngine>.Instance);

        dataEngine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, search, operationScopeFactory, automationEngine, new CurrentUserService());
        await dataEngine.CreateTableAsync(table);

        var record = await dataEngine.InsertAsync(table.Id, "{ \"Title\": \"Nouvelle facture\" }");
        var updated = await dataEngine.GetAsync(table.Id, record.Id);

        Assert.NotNull(updated);
        using var payload = JsonDocument.Parse(updated!.DataJson);
        Assert.Equal("finance", payload.RootElement.GetProperty("Tags")[0].GetString());
        Assert.Equal("Nouvelle", payload.RootElement.GetProperty("Status").GetString());

        Assert.Single(notes.Notes);
        Assert.Single(agenda.Events);

        var execution = await context.AutomationExecutions.OrderByDescending(e => e.StartedAt).FirstOrDefaultAsync();
        Assert.NotNull(execution);
        Assert.Equal(AutomationExecutionStatus.Succeeded, execution!.Status);

        var automationPayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tableId"] = table.Id,
            ["recordId"] = record.Id,
            ["data"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Title"] = "Nouvelle facture"
            }
        };
        var duplicateEvent = new AutomationEvent("record.created", AutomationTriggerType.OnCreate, automationPayload, null, table.Id);
        var reExecutions = await automationEngine.ExecuteAsync(duplicateEvent);

        Assert.Single(notes.Notes);
        Assert.Single(agenda.Events);
        Assert.Single(reExecutions);
        Assert.Equal(1, await context.AutomationExecutions.CountAsync());
    }
}

file sealed class RecordingNoteService : INoteService
{
    public List<S_Note> Notes { get; } = new();

    public Task<S_Note> CreateTextNoteAsync(string title, string content, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var note = new S_Note { Title = title, Content = content, Source = NoteSourceType.Generated, Links = links?.ToList() ?? new List<J_Note_Link>() };
        Notes.Add(note);
        return Task.FromResult(note);
    }

    public Task<S_Note> CreateDictatedNoteAsync(string title, Stream audioStream, string fileName, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new S_Note { Title = title, Content = string.Empty, Source = NoteSourceType.Voice });

    public Task<IEnumerable<S_Note>> GetChronologicalAsync(int take = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<S_Note>>(Notes.OrderByDescending(n => n.CreatedAt).Take(take).ToList());
}

file sealed class RecordingAgendaService : IAgendaService
{
    public List<S_Event> Events { get; } = new();

    public Task<S_Event> AddEventAsync(S_Event evt, CancellationToken cancellationToken = default)
    {
        Events.Add(evt);
        return Task.FromResult(evt);
    }

    public Task<S_Event> UpdateEventAsync(S_Event evt, CancellationToken cancellationToken = default)
    {
        var index = Events.FindIndex(existing => existing.Id == evt.Id);
        if (index >= 0)
        {
            Events[index] = evt;
        }
        else
        {
            Events.Add(evt);
        }
        return Task.FromResult(evt);
    }

    public Task DeleteEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        Events.RemoveAll(evt => evt.Id == eventId);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<S_Event>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<S_Event>>(Events);

    public Task<IEnumerable<S_Event>> GetOccurrencesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<S_Event>>(Events);

    public Task<IEnumerable<S_Event>> GetPendingRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<S_Event>>(Events.Where(e => e.ReminderAt <= asOf && !e.IsCompleted).ToList());
}

file sealed class NullSearchService : ISearchService
{
    public Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<SearchHit>>(Array.Empty<SearchHit>());

    public Task IndexNoteAsync(S_Note note, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexRecordAsync(F_Record record, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexFileAsync(F_File file, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

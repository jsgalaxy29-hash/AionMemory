using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.AI;
using Aion.AppHost.Services;
using Aion.Domain;

namespace Aion.AppHost.UI.Tests;

internal sealed class FakeDashboardService : IDashboardService
{
    private readonly List<DashboardWidget> _widgets;
    private DashboardLayout? _layout;

    public FakeDashboardService(IEnumerable<DashboardWidget>? widgets = null, DashboardLayout? layout = null)
    {
        _widgets = widgets?.ToList() ?? new List<DashboardWidget>();
        _layout = layout;
    }

    public Task<IEnumerable<DashboardWidget>> GetWidgetsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<DashboardWidget>>(_widgets);

    public Task<DashboardWidget> SaveWidgetAsync(DashboardWidget widget, CancellationToken cancellationToken = default)
    {
        var index = _widgets.FindIndex(w => w.Id == widget.Id);
        if (index >= 0)
        {
            _widgets[index] = widget;
        }
        else
        {
            _widgets.Add(widget);
        }

        return Task.FromResult(widget);
    }

    public Task<DashboardLayout?> GetLayoutAsync(string dashboardKey, CancellationToken cancellationToken = default)
        => Task.FromResult(_layout is not null && _layout.DashboardKey == dashboardKey ? _layout : null);

    public Task<DashboardLayout> SaveLayoutAsync(DashboardLayout layout, CancellationToken cancellationToken = default)
    {
        _layout = layout;
        return Task.FromResult(layout);
    }
}

internal sealed class FakeAgendaService : IAgendaService
{
    private readonly List<S_Event> _events = new();

    public FakeAgendaService(IEnumerable<S_Event>? events = null)
    {
        if (events is not null)
        {
            _events.AddRange(events);
        }
    }

    public Task<S_Event> AddEventAsync(S_Event evt, CancellationToken cancellationToken = default)
    {
        _events.Add(evt);
        return Task.FromResult(evt);
    }

    public Task<S_Event> UpdateEventAsync(S_Event evt, CancellationToken cancellationToken = default)
    {
        var index = _events.FindIndex(e => e.Id == evt.Id);
        if (index >= 0)
        {
            _events[index] = evt;
        }
        else
        {
            _events.Add(evt);
        }

        return Task.FromResult(evt);
    }

    public Task DeleteEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        _events.RemoveAll(e => e.Id == eventId);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<S_Event>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<S_Event>>(FilterRange(from, to));

    public Task<IEnumerable<S_Event>> GetOccurrencesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<S_Event>>(FilterRange(from, to));

    public Task<IEnumerable<S_Event>> GetPendingRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<S_Event>>(_events.Where(e => e.ReminderAt.HasValue && e.ReminderAt.Value <= asOf).ToList());

    private List<S_Event> FilterRange(DateTimeOffset from, DateTimeOffset to)
        => _events.Where(e => e.Start >= from && e.Start <= to).ToList();
}

internal sealed class FakeNoteService : INoteService
{
    public List<S_Note> Notes { get; } = new();

    public FakeNoteService(IEnumerable<S_Note>? notes = null)
    {
        if (notes is not null)
        {
            Notes.AddRange(notes);
        }
    }

    public Task<S_Note> CreateTextNoteAsync(string title, string content, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var note = new S_Note
        {
            Title = title,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
            Links = links?.ToList() ?? new List<J_Note_Link>()
        };
        Notes.Insert(0, note);
        return Task.FromResult(note);
    }

    public Task<S_Note> CreateDictatedNoteAsync(string title, Stream audioStream, string fileName, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var note = new S_Note
        {
            Title = title,
            Content = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            Links = links?.ToList() ?? new List<J_Note_Link>()
        };
        Notes.Insert(0, note);
        return Task.FromResult(note);
    }

    public Task<IEnumerable<S_Note>> GetChronologicalAsync(int take = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<S_Note>>(Notes.OrderByDescending(n => n.CreatedAt).Take(take).ToList());
}

internal sealed class FakeLifeService : ILifeService
{
    private readonly List<S_HistoryEvent> _events = new();

    public FakeLifeService(IEnumerable<S_HistoryEvent>? events = null)
    {
        if (events is not null)
        {
            _events.AddRange(events);
        }
    }

    public Task<S_HistoryEvent> AddHistoryAsync(S_HistoryEvent evt, CancellationToken cancellationToken = default)
    {
        _events.Add(evt);
        return Task.FromResult(evt);
    }

    public Task<TimelinePage> GetTimelinePageAsync(TimelineQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(new TimelinePage(Array.Empty<S_HistoryEvent>(), 0, 0));

    public Task<IEnumerable<S_HistoryEvent>> GetTimelineAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<S_HistoryEvent>>(_events.Where(e =>
            (!from.HasValue || e.OccurredAt >= from.Value) &&
            (!to.HasValue || e.OccurredAt <= to.Value)).ToList());
}

internal sealed class FakeRecordQueryService : IRecordQueryService
{
    private readonly Dictionary<Guid, int> _counts = new();
    private readonly Dictionary<Guid, ResolvedRecord?> _resolvedRecords = new();

    public FakeRecordQueryService(Dictionary<Guid, int>? counts = null, Dictionary<Guid, ResolvedRecord?>? resolvedRecords = null)
    {
        if (counts is not null)
        {
            foreach (var entry in counts)
            {
                _counts[entry.Key] = entry.Value;
            }
        }

        if (resolvedRecords is not null)
        {
            foreach (var entry in resolvedRecords)
            {
                _resolvedRecords[entry.Key] = entry.Value;
            }
        }
    }

    public Task<RecordPage<F_Record>> QueryAsync(Guid tableId, QuerySpec query, CancellationToken cancellationToken = default)
        => Task.FromResult(new RecordPage<F_Record>(Array.Empty<F_Record>(), 0));

    public Task<int> CountAsync(Guid tableId, QuerySpec query, CancellationToken cancellationToken = default)
        => Task.FromResult(_counts.TryGetValue(tableId, out var count) ? count : 0);

    public Task<F_Record?> GetAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
        => Task.FromResult<F_Record?>(null);

    public Task<ResolvedRecord?> GetResolvedAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
        => Task.FromResult(_resolvedRecords.TryGetValue(recordId, out var record) ? record : null);

    public Task<F_Record> SaveAsync(Guid tableId, Guid? recordId, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        var record = new F_Record
        {
            Id = recordId ?? Guid.NewGuid(),
            TableId = tableId,
            DataJson = "{}"
        };
        return Task.FromResult(record);
    }

    public Task DeleteAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class FakeModuleViewService : IModuleViewService
{
    private readonly IReadOnlyList<STable> _tables;

    public FakeModuleViewService(IEnumerable<STable> tables)
    {
        _tables = tables.ToList();
    }

    public Task<IReadOnlyList<STable>> GetTablesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_tables);

    public Task<STable?> GetTableAsync(Guid tableId, CancellationToken cancellationToken = default)
        => Task.FromResult(_tables.FirstOrDefault(t => t.Id == tableId));

    public Task<STable?> GetTableByNameAsync(string tableName, CancellationToken cancellationToken = default)
        => Task.FromResult(_tables.FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<SViewDefinition>> GetViewsAsync(Guid tableId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SViewDefinition>>(Array.Empty<SViewDefinition>());
}

internal sealed class FakeTableDefinitionService : ITableDefinitionService
{
    private readonly Dictionary<Guid, STable> _tables = new();

    public FakeTableDefinitionService(IEnumerable<STable>? tables = null)
    {
        if (tables is not null)
        {
            foreach (var table in tables)
            {
                _tables[table.Id] = table;
            }
        }
    }

    public Task<STable?> GetTableAsync(Guid entityTypeId, CancellationToken cancellationToken = default)
        => Task.FromResult(_tables.TryGetValue(entityTypeId, out var table) ? table : null);

    public Task<STable> EnsureTableAsync(S_EntityType entityType, CancellationToken cancellationToken = default)
    {
        if (!_tables.TryGetValue(entityType.Id, out var table))
        {
            table = new STable
            {
                Id = entityType.Id,
                Name = entityType.Name,
                DisplayName = entityType.PluralName ?? entityType.Name
            };
            _tables[entityType.Id] = table;
        }

        return Task.FromResult(table);
    }
}

internal sealed class FakeMetadataService : IMetadataService
{
    private readonly IReadOnlyList<S_Module> _modules;

    public FakeMetadataService(IEnumerable<S_Module>? modules = null)
    {
        _modules = modules?.ToList() ?? new List<S_Module>();
    }

    public Task<IEnumerable<S_Module>> GetModulesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<S_Module>>(_modules);

    public Task<S_Module> CreateModuleAsync(S_Module module, CancellationToken cancellationToken = default)
        => Task.FromResult(module);

    public Task<S_EntityType> AddEntityTypeAsync(Guid moduleId, S_EntityType entityType, CancellationToken cancellationToken = default)
        => Task.FromResult(entityType);
}

internal sealed class FakeFileStorageService : IFileStorageService
{
    private readonly Dictionary<Guid, F_File> _files = new();

    public Task<F_File> SaveAsync(string fileName, Stream content, string mimeType, CancellationToken cancellationToken = default)
    {
        var file = new F_File
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            MimeType = mimeType,
            Size = content.Length,
            StoragePath = $"memory://{fileName}",
            Sha256 = string.Empty
        };
        _files[file.Id] = file;
        return Task.FromResult(file);
    }

    public Task<Stream> OpenAsync(Guid fileId, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream(Array.Empty<byte>()));

    public Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        _files.Remove(fileId);
        return Task.CompletedTask;
    }

    public Task<F_FileLink> LinkAsync(Guid fileId, string targetType, Guid targetId, string? relation = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new F_FileLink { FileId = fileId, TargetType = targetType, TargetId = targetId });

    public Task<IEnumerable<F_File>> GetForAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<F_File>>(Array.Empty<F_File>());
}

internal sealed class FakeVisionService : IAionVisionService
{
    public Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new S_VisionAnalysis
        {
            FileId = request.FileId,
            AnalysisType = request.AnalysisType,
            ResultJson = "{}"
        });
}

namespace Aion.AI;

public enum IntentTarget
{
    Chat = 0,
    DataEngine = 1,
    NoteService = 2,
    AgendaService = 3,
    ReportService = 4,
    ModuleDesigner = 5,
    Unknown = 6
}

public readonly record struct IntentClass(string Name, IntentTarget Target);

public static class IntentCatalog
{
    public const string Chat = "chat";
    public const string Data = "data";
    public const string Note = "note";
    public const string Agenda = "agenda";
    public const string Report = "report";
    public const string Module = "module";
    public const string Unknown = "unknown";

    private static readonly IReadOnlyDictionary<string, IntentClass> Classes =
        new Dictionary<string, IntentClass>(StringComparer.OrdinalIgnoreCase)
        {
            [Chat] = new IntentClass(Chat, IntentTarget.Chat),
            [Data] = new IntentClass(Data, IntentTarget.DataEngine),
            [Note] = new IntentClass(Note, IntentTarget.NoteService),
            [Agenda] = new IntentClass(Agenda, IntentTarget.AgendaService),
            [Report] = new IntentClass(Report, IntentTarget.ReportService),
            [Module] = new IntentClass(Module, IntentTarget.ModuleDesigner),
            [Unknown] = new IntentClass(Unknown, IntentTarget.Unknown)
        };

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Chat] = Chat,
            ["conversation"] = Chat,
            ["smalltalk"] = Chat,
            [Data] = Data,
            ["data_engine"] = Data,
            ["dataengine"] = Data,
            ["crud"] = Data,
            ["query"] = Data,
            ["create"] = Data,
            ["read"] = Data,
            ["update"] = Data,
            ["delete"] = Data,
            [Note] = Note,
            ["notes"] = Note,
            ["create_note"] = Note,
            ["note_create"] = Note,
            [Agenda] = Agenda,
            ["calendar"] = Agenda,
            ["event"] = Agenda,
            ["appointment"] = Agenda,
            ["create_task"] = Agenda,
            ["task"] = Agenda,
            ["todo"] = Agenda,
            [Report] = Report,
            ["rapport"] = Report,
            [Module] = Module,
            ["design_module"] = Module,
            ["design"] = Module,
            [Unknown] = Unknown
        };

    public static IReadOnlyCollection<IntentClass> All => Classes.Values.ToArray();

    public static string PromptIntents =>
        string.Join(", ", Classes.Keys.Where(name => !string.Equals(name, Unknown, StringComparison.OrdinalIgnoreCase)));

    public static bool IsKnownName(string? intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
        {
            return false;
        }

        return Aliases.ContainsKey(intent.Trim());
    }

    public static bool IsUnknownName(string? intent)
        => !string.IsNullOrWhiteSpace(intent)
           && string.Equals(intent.Trim(), Unknown, StringComparison.OrdinalIgnoreCase);

    public static IntentClass Normalize(string? intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
        {
            return Classes[Unknown];
        }

        var trimmed = intent.Trim();
        if (Aliases.TryGetValue(trimmed, out var canonical) && Classes.TryGetValue(canonical, out var intentClass))
        {
            return intentClass;
        }

        return Classes[Unknown];
    }
}

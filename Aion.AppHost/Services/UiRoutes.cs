namespace Aion.AppHost.Services;

public static class UiRoutes
{
    public const string ModuleHost = "/ModuleHost/{moduleId:guid}";
    public const string LegacyModule = "/module/{id}";
    public const string Record = "/Record/{tableId:guid}/{recordId:guid}";
    public const string LegacyRecord = "/records/{entity}/{recordId}";
    public const string Notes = "/notes/table/{tableId:guid}/{recordId:guid}";

    public static string Module(Guid moduleId) => $"/ModuleHost/{moduleId}";
    public static string RecordDetail(Guid tableId, Guid recordId) => $"/Record/{tableId}/{recordId}";
    public static string NotesFor(Guid tableId, Guid recordId) => $"/notes/table/{tableId}/{recordId}";
}

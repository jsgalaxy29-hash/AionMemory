using Microsoft.Extensions.Logging;

namespace Aion.AI;

public sealed class IntentRouter : IIntentRouter
{
    private readonly IModuleDesigner _moduleDesigner;
    private readonly ICrudInterpreter _crudInterpreter;
    private readonly IAgendaInterpreter _agendaInterpreter;
    private readonly INoteInterpreter _noteInterpreter;
    private readonly IReportInterpreter _reportInterpreter;
    private readonly IDataEngine _dataEngine;
    private readonly INoteService _noteService;
    private readonly IAgendaService _agendaService;
    private readonly IChatModel _chatModel;
    private readonly ILogger<IntentRouter> _logger;

    public IntentRouter(
        IModuleDesigner moduleDesigner,
        ICrudInterpreter crudInterpreter,
        IAgendaInterpreter agendaInterpreter,
        INoteInterpreter noteInterpreter,
        IReportInterpreter reportInterpreter,
        IDataEngine dataEngine,
        INoteService noteService,
        IAgendaService agendaService,
        IChatModel chatModel,
        ILogger<IntentRouter> logger)
    {
        _moduleDesigner = moduleDesigner;
        _crudInterpreter = crudInterpreter;
        _agendaInterpreter = agendaInterpreter;
        _noteInterpreter = noteInterpreter;
        _reportInterpreter = reportInterpreter;
        _dataEngine = dataEngine;
        _noteService = noteService;
        _agendaService = agendaService;
        _chatModel = chatModel;
        _logger = logger;
    }

    public async Task<IntentRouteResult> RouteAsync(IntentRouteRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Input))
        {
            throw new ArgumentException("Input requis pour router l'intention.", nameof(request));
        }

        var intentClass = ResolveIntentClass(request);
        if (request.IsOffline)
        {
            var offlineMessage = "Mode hors-ligne : l'orchestration IA est limitée. " +
                                 "Veuillez réessayer avec un provider configuré.";
            return new IntentRouteResult(intentClass, offlineMessage, true);
        }

        return intentClass.Target switch
        {
            IntentTarget.ModuleDesigner => await BuildModuleAsync(request.Input, intentClass, cancellationToken).ConfigureAwait(false),
            IntentTarget.AgendaService => await BuildAgendaAsync(request.Input, intentClass, cancellationToken).ConfigureAwait(false),
            IntentTarget.NoteService => await BuildNoteAsync(request.Input, intentClass, cancellationToken).ConfigureAwait(false),
            IntentTarget.ReportService => await BuildReportAsync(request, intentClass, cancellationToken).ConfigureAwait(false),
            IntentTarget.DataEngine => await BuildDataEngineAsync(request, intentClass, cancellationToken).ConfigureAwait(false),
            _ => await BuildChatAsync(request.Input, intentClass, cancellationToken).ConfigureAwait(false)
        };
    }

    private IntentClass ResolveIntentClass(IntentRouteRequest request)
    {
        var normalized = IntentCatalog.Normalize(request.Intent.Intent);
        if (normalized.Name == IntentCatalog.Unknown && !IntentCatalog.IsUnknownName(request.Intent.Intent))
        {
            var heuristic = IntentHeuristics.Detect(request.Input);
            _logger.LogInformation("Intent heuristique appliquée: {Intent}", heuristic.Name);
            return heuristic;
        }

        return normalized;
    }

    private async Task<IntentRouteResult> BuildModuleAsync(string input, IntentClass intentClass, CancellationToken cancellationToken)
    {
        var design = await _moduleDesigner.GenerateModuleAsync(new ModuleDesignRequest { Prompt = input }, cancellationToken).ConfigureAwait(false);
        var module = design.Module;
        var entities = module.EntityTypes.Any()
            ? string.Join(", ", module.EntityTypes.Select(e => $"{e.Name} ({e.Fields.Count} champs)"))
            : "Aucune entité proposée";

        return new IntentRouteResult(intentClass, $"Module généré: {module.Name}. Entités: {entities}.", false);
    }

    private async Task<IntentRouteResult> BuildAgendaAsync(string input, IntentClass intentClass, CancellationToken cancellationToken)
    {
        var evt = await _agendaInterpreter.CreateEventAsync(input, cancellationToken).ConfigureAwait(false);
        var saved = await _agendaService.AddEventAsync(evt, cancellationToken).ConfigureAwait(false);
        var start = saved.Start.ToString("t");
        var end = saved.End?.ToString("t") ?? "?";
        var response = $"Événement créé: {saved.Title} de {start} à {end}.";
        return new IntentRouteResult(intentClass, response, false);
    }

    private async Task<IntentRouteResult> BuildNoteAsync(string input, IntentClass intentClass, CancellationToken cancellationToken)
    {
        var refined = await _noteInterpreter.RefineNoteAsync("Note IA", input, cancellationToken).ConfigureAwait(false);
        var saved = await _noteService.CreateTextNoteAsync(refined.Title, refined.Content, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var response = $"Note créée: {saved.Title}\n{saved.Content}";
        return new IntentRouteResult(intentClass, response, false);
    }

    private async Task<IntentRouteResult> BuildReportAsync(IntentRouteRequest request, IntentClass intentClass, CancellationToken cancellationToken)
    {
        var moduleId = request.ModuleId ?? request.ModuleContext?.Id ?? Guid.Empty;
        if (moduleId == Guid.Empty)
        {
            return new IntentRouteResult(intentClass, "Aucun module disponible pour générer un rapport.", true);
        }

        var report = await _reportInterpreter.BuildReportAsync(new ReportBuildRequest
        {
            ModuleId = moduleId,
            Description = request.Input,
            PreferredVisualization = "list"
        }, cancellationToken).ConfigureAwait(false);

        var response = $"Rapport IA: {report.Report.Name} (visualisation: {report.Report.Visualization ?? "n/a"}).";
        return new IntentRouteResult(intentClass, response, false);
    }

    private async Task<IntentRouteResult> BuildDataEngineAsync(IntentRouteRequest request, IntentClass intentClass, CancellationToken cancellationToken)
    {
        if (request.ModuleContext is null || request.ModuleContext.EntityTypes.Count == 0)
        {
            return new IntentRouteResult(intentClass, "Aucun module de données n'est disponible pour exécuter cette action.", true);
        }

        var interpretation = await _crudInterpreter.GenerateQueryAsync(new CrudQueryRequest
        {
            Intent = request.Intent.Intent,
            Module = request.ModuleContext
        }, cancellationToken).ConfigureAwait(false);

        var tables = (await _dataEngine.GetTablesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var filters = interpretation.Filters.Count == 0
            ? "aucun filtre"
            : string.Join(", ", interpretation.Filters.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var payload = interpretation.Payload.Count == 0
            ? "aucune donnée"
            : string.Join(", ", interpretation.Payload.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var response = $"DataEngine sélectionné ({tables.Count} tables). Action: {interpretation.Action}. " +
                       $"Filtres: {filters}. Payload: {payload}.";
        return new IntentRouteResult(intentClass, response, false);
    }

    private async Task<IntentRouteResult> BuildChatAsync(string input, IntentClass intentClass, CancellationToken cancellationToken)
    {
        var reply = await _chatModel.GenerateAsync(input, cancellationToken).ConfigureAwait(false);
        return new IntentRouteResult(intentClass, reply.Content, false);
    }
}

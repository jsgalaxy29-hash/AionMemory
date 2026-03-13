using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using Aion.Domain;
using Aion.AI.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

public sealed class AgendaInterpreter : IAgendaInterpreter
{
    private readonly IChatModel _provider;

    public AgendaInterpreter(IChatModel provider)
    {
        _provider = provider;
    }
    public async Task<S_Event> CreateEventAsync(string input, CancellationToken cancellationToken = default)
    {
        var prompt = $@"Génère un événement JSON {{""title"":"""",""start"":""ISO"",""end"":""ISO|null"",""reminder"":""ISO|null""}} pour: {input}";
        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        try
        {
            var evt = JsonSerializer.Deserialize<AgendaResponse>(JsonHelper.ExtractJson(response.Content), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (evt is not null)
            {
                return new S_Event
                {
                    Title = evt.Title ?? input,
                    Description = input,
                    Start = ParseDate(evt.Start) ?? DateTimeOffset.UtcNow,
                    End = ParseDate(evt.End),
                    ReminderAt = ParseDate(evt.Reminder)
                };
            }
        }
        catch (JsonException)
        {
        }
        return new S_Event { Title = input, Start = DateTimeOffset.UtcNow };
    }
    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var date) ? date : null;
    private sealed class AgendaResponse
    {
        public string? Title { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? Reminder { get; set; }
    }
}

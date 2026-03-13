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

public sealed class NoteInterpreter : INoteInterpreter
{
    private readonly IChatModel _provider;

    public NoteInterpreter(IChatModel provider)
    {
        _provider = provider;
    }
    public async Task<S_Note> RefineNoteAsync(string title, string content, CancellationToken cancellationToken = default)
    {
        var prompt = $@"Nettoie et synthétise la note suivante. Réponds uniquement avec le texte amélioré.
Titre: {title}
Contenu:
{content}";
        var refined = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        return new S_Note { Title = title, Content = refined.Content, Source = NoteSourceType.Generated, CreatedAt = DateTimeOffset.UtcNow };
    }
}

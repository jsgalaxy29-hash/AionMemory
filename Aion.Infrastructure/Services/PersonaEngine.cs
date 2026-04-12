using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.AI;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class PersonaEngine : IAionPersonaEngine, IPersonaEngine
{
    private readonly AionDbContext _db;

    public PersonaEngine(AionDbContext db)
    {
        _db = db;
    }

    public async Task<UserPersona> GetPersonaAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _db.Personas.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var defaultPersona = new UserPersona { Name = "Default", Tone = PersonaTone.Neutral };
        await _db.Personas.AddAsync(defaultPersona, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return defaultPersona;
    }

    public async Task<UserPersona> SavePersonaAsync(UserPersona persona, CancellationToken cancellationToken = default)
    {
        if (await _db.Personas.AnyAsync(p => p.Id == persona.Id, cancellationToken).ConfigureAwait(false))
        {
            _db.Personas.Update(persona);
        }
        else
        {
            await _db.Personas.AddAsync(persona, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return persona;
    }
}


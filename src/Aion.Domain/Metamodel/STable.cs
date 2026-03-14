using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Aion.Domain;

public class STable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(1024)]
    public string? Description { get; set; }

    public bool IsSystem { get; set; }

    public bool SupportsSoftDelete { get; set; }

    public bool HasAuditTrail { get; set; }

    [StringLength(128)]
    public string? DefaultView { get; set; }

    [StringLength(256)]
    public string? RowLabelTemplate { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SFieldDefinition> Fields { get;  } = new List<SFieldDefinition>();

    public ICollection<SViewDefinition> Views { get; } = new List<SViewDefinition>();

    public static STable Create(
        string name,
        string displayName,
        IEnumerable<SFieldDefinition> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(fields);

        return new STable
        {
            Name = name,
            DisplayName = displayName,
            Fields = fields.ToList()
        };
    }
}

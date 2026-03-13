using System;
using System.ComponentModel.DataAnnotations;

namespace Aion.Domain.Metamodel;

public class SRelation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromTableId { get; set; }
    public Guid ToTableId { get; set; }

    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;

    [StringLength(128)]
    public string? ForeignKeyField { get; set; }
}

using System;
using System.ComponentModel.DataAnnotations;

namespace Aion.Domain.Metamodel;

public class SIndex
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TableId { get; set; }

    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(1024)]
    public string Fields { get; set; } = string.Empty;

    public bool IsUnique { get; set; }
}

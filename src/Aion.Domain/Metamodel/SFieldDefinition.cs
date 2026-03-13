using System;
using System.ComponentModel.DataAnnotations;

namespace Aion.Domain;

public class SFieldDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TableId { get; set; }

    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Label { get; set; } = string.Empty;

    public FieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public bool IsSearchable { get; set; }
    public bool IsListVisible { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsUnique { get; set; }
    public bool IsIndexed { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsSortable { get; set; }
    public bool IsHidden { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsComputed { get; set; }

    [StringLength(1024)]
    public string? DefaultValue { get; set; }

    [StringLength(4000)]
    public string? EnumValues { get; set; }

    public Guid? RelationTargetEntityTypeId { get; set; }

    [StringLength(128)]
    public string? LookupTarget { get; set; }

    [StringLength(128)]
    public string? LookupField { get; set; }

    [StringLength(2048)]
    public string? ComputedExpression { get; set; }

    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }

    [StringLength(512)]
    public string? ValidationPattern { get; set; }

    [StringLength(256)]
    public string? Placeholder { get; set; }

    [StringLength(128)]
    public string? Unit { get; set; }

    public int Order { get; set; }

    public static SFieldDefinition Text(string name, string label, bool required = false, string? defaultValue = null)
        => new()
        {
            Name = name,
            Label = label,
            DataType = FieldDataType.Text,
            IsRequired = required,
            DefaultValue = defaultValue,
            IsSearchable = true,
            IsListVisible = true
        };
}

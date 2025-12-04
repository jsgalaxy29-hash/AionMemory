using System.Text;
using System.Text.RegularExpressions;

namespace Aion.Domain.ValueObjects;

public sealed class Phone : IEquatable<Phone>
{
    private static readonly Regex PhonePattern = new(@"^\+?[1-9]\d{7,14}$", RegexOptions.Compiled);

    public string Number { get; }

    protected Phone()
    {
        Number = string.Empty;
    }

    protected Phone(string number)
    {
        Number = number;
    }

    public static Phone Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Phone cannot be null or empty", nameof(value));
        }

        var normalized = Normalize(value);
        if (!PhonePattern.IsMatch(normalized))
        {
            throw new ArgumentException("Invalid phone format. Use international format like +123456789", nameof(value));
        }

        return new Phone(normalized);
    }

    public static implicit operator Phone(string value) => Create(value);

    public static implicit operator string(Phone phone) => phone.Number;

    public override string ToString() => Number;

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Number);

    public override bool Equals(object? obj) => obj is Phone other && Equals(other);

    public bool Equals(Phone? other) => other is not null && Number == other.Number;

    private static string Normalize(string value)
    {
        var trimmed = value.Trim();
        var builder = new StringBuilder(trimmed.Length);

        foreach (var ch in trimmed)
        {
            if (char.IsDigit(ch))
            {
                builder.Append(ch);
                continue;
            }

            if (ch == '+' && builder.Length == 0)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}

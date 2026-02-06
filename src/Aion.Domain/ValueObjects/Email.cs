using System;
using System.Net.Mail;

namespace Aion.Domain.ValueObjects;

public sealed class Email : IEquatable<Email>
{
    public string Value { get; }

    private Email()
    {
        Value = string.Empty;
    }

    private Email(string value)
    {
        Value = value;
    }

    public static Email Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Email cannot be null or empty", nameof(value));
        }

        var trimmed = value.Trim();
        var address = Parse(trimmed);
        var normalized = address.Address.ToUpperInvariant();
        return new Email(normalized);
    }

    public static Email FromString(string value) => Create(value);

    public static implicit operator Email(string value) => Create(value);

    public static implicit operator string(Email email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Value;
    }

    public override string ToString() => Value;

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override bool Equals(object? obj) => obj is Email other && Equals(other);

    public bool Equals(Email? other)
    {
        return other is not null &&
               string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static MailAddress Parse(string value)
    {
        try
        {
            return new MailAddress(value);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Invalid email format", nameof(value), ex);
        }
    }
}

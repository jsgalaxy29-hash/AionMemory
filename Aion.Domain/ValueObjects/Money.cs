namespace Aion.Domain.ValueObjects;

public sealed class Money : IEquatable<Money>
{
    public decimal Amount { get; }
    public string Currency { get; }

    protected Money()
    {
        Currency = string.Empty;
    }

    protected Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required", nameof(currency));
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsLetter))
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code", nameof(currency));
        }

        return new Money(amount, normalizedCurrency);
    }

    public static implicit operator Money((decimal Amount, string Currency) value) => Create(value.Amount, value.Currency);

    public override string ToString() => $"{Amount:N2} {Currency}";

    public override int GetHashCode() => HashCode.Combine(Amount, StringComparer.OrdinalIgnoreCase.GetHashCode(Currency));

    public override bool Equals(object? obj) => obj is Money other && Equals(other);

    public bool Equals(Money? other)
    {
        return other is not null &&
               Amount == other.Amount &&
               string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase);
    }
}

using Aion.Domain.ValueObjects;
using Xunit;

namespace Aion.Domain.Tests;

public class ValueObjectsTests
{
    [Theory]
    [InlineData("User@example.com", "user@example.com")]
    [InlineData("  USER@Example.Com  ", "user@example.com")]
    public void Email_normalizes_and_validates(string input, string expected)
    {
        var email = Email.Create(input);

        Assert.Equal(expected, email.Value);
        Assert.Equal(expected, (string)email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing_at.domain")]
    public void Email_rejects_invalid(string value)
    {
        Assert.Throws<ArgumentException>(() => Email.Create(value));
    }

    [Theory]
    [InlineData("+33 6 12 34 56 78", "+33612345678")]
    [InlineData("(555) 123-4567", "5551234567")]
    [InlineData("+1-800-555-0123", "+18005550123")]
    public void Phone_normalizes_and_validates(string input, string expected)
    {
        var phone = Phone.Create(input);

        Assert.Equal(expected, phone.Number);
        Assert.Equal(expected, (string)phone);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("")]
    public void Phone_rejects_invalid(string value)
    {
        Assert.Throws<ArgumentException>(() => Phone.Create(value));
    }

    [Fact]
    public void Money_normalizes_currency_and_supports_tuple_conversion()
    {
        Money money = (10.5m, "eur");

        Assert.Equal(10.5m, money.Amount);
        Assert.Equal("EUR", money.Currency);
        Assert.Equal("10.50 EUR", money.ToString());
    }

    [Theory]
    [InlineData("US", 5)]
    [InlineData("USDT", 15)]
    [InlineData("")]
    public void Money_rejects_invalid_currency(string currency, decimal amount)
    {
        Assert.Throws<ArgumentException>(() => Money.Create(amount, currency));
    }
}

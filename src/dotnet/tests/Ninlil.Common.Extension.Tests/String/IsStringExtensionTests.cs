using Ninlil.Common.Extension.String;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.String;

/// <summary>
///     This class tests <see cref="IsStringExtension" />.
/// </summary>
public class IsStringExtensionTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("FALSE", true)]
    [InlineData("yes", true)]
    [InlineData("n", true)]
    [InlineData("1", true)]
    [InlineData("0", true)]
    [InlineData("maybe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBooleanRecognizesKnownBooleanWords(string value, bool expected)
    {
        Assert.Equal(expected, value.IsBoolean());
    }

    [Theory]
    [InlineData("2026-01-02", true)]
    [InlineData("not-a-date", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDateRecognizesParsableDateStrings(string value, bool expected)
    {
        Assert.Equal(expected, value.IsDate());
    }

    [Fact]
    public void IsDateTimeDelegatesToIsDate()
    {
        Assert.Equal("2026-01-02".IsDate(), "2026-01-02".IsDateTime());
        Assert.Equal("garbage".IsDate(), "garbage".IsDateTime());
    }

    [Theory]
    [InlineData("3.14", true)]
    [InlineData("$1,234.56", true)]
    [InlineData("not-a-number", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDoubleRecognizesNumericStringsIncludingCurrencyFormatting(
        string value,
        bool expected
    )
    {
        Assert.Equal(expected, value.IsDouble());
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEmailAddressRecognizesValidEmailAddresses(string value, bool expected)
    {
        Assert.Equal(expected, value.IsEmailAddress());
    }

    [Theory]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6", true)]
    [InlineData("not-a-guid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGuidRecognizesParsableGuidStrings(string value, bool expected)
    {
        Assert.Equal(expected, value.IsGuid());
    }

    [Theory]
    [InlineData("42", true)]
    [InlineData("-7", true)]
    [InlineData("3.14", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsIntRecognizesParsableIntegerStrings(string value, bool expected)
    {
        Assert.Equal(expected, value.IsInt());
    }

    [Fact]
    public void IsMoneyDelegatesToIsDouble()
    {
        Assert.Equal("$9.99".IsDouble(), "$9.99".IsMoney());
        Assert.Equal("garbage".IsDouble(), "garbage".IsMoney());
    }
}

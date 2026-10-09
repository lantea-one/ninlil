using System.Globalization;
using System.Text.Json;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Web.Extension;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the typed readers of <see cref="JsonElementMergePatchExtension" /> as
///     pure unit tests (no host): each reader accepts its own JSON kind, rejects every other kind
///     and <c>null</c> with a <see cref="ValidationException" /> that names the field, and keeps
///     its ranges inclusive at both ends.
/// </summary>
public class JsonElementMergePatchExtensionTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Readers_WhenTheFieldIsAbsent_ReturnFalse()
    {
        JsonElement patch = Parse("{}");

        Assert.False(patch.TryGetMergePatchBoolean("x", out _));
        Assert.False(patch.TryGetMergePatchInt32("x", out _, 0, 1));
        Assert.False(patch.TryGetMergePatchDouble("x", out _, 0, 1));
        Assert.False(patch.TryGetMergePatchString("x", out _, false));
        Assert.False(patch.TryGetMergePatchDuration("x", out _, TimeSpan.Zero, TimeSpan.MaxValue));
        Assert.False(patch.TryGetMergePatchStringList("x", out _, false));
        Assert.False(patch.TryGetMergePatchEnum("x", out DayOfWeek _));
    }

    [Fact]
    public void Readers_WhenTheBodyIsNotAnObject_ReturnFalse()
    {
        Assert.False(Parse("[]").TryGetMergePatchBoolean("x", out _));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Boolean_WithABoolean_ReturnsIt(string json, bool expected)
    {
        Assert.True(Parse($"{{\"x\":{json}}}").TryGetMergePatchBoolean("x", out bool value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Boolean_WithAnyOtherKind_ThrowsNamingTheField(string json)
    {
        ValidationException ex = Assert.Throws<ValidationException>(() =>
            Parse($"{{\"x\":{json}}}").TryGetMergePatchBoolean("x", out _)
        );
        Assert.Contains("'x'", ex.Message);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("10")]
    [InlineData("5")]
    public void Int32_InsideTheRangeInclusive_ReturnsIt(string json)
    {
        Assert.True(Parse($"{{\"x\":{json}}}").TryGetMergePatchInt32("x", out int value, 1, 10));
        Assert.Equal(int.Parse(json), value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    [InlineData("\"5\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Int32_OutsideTheRangeOrOfAnotherKind_ThrowsNamingTheFieldAndRange(string json)
    {
        ValidationException ex = Assert.Throws<ValidationException>(() =>
            Parse($"{{\"x\":{json}}}").TryGetMergePatchInt32("x", out _, 1, 10)
        );
        Assert.Equal("'x' must be a whole number between 1 and 10.", ex.Message);
    }

    [Fact]
    public void Int32_WithAFieldPath_NamesThePathInTheMessage()
    {
        ValidationException ex = Assert.Throws<ValidationException>(() =>
            Parse("{\"x\":\"a\"}").TryGetMergePatchInt32("x", out _, 1, 10, "outer.x")
        );
        Assert.Contains("'outer.x'", ex.Message);
    }

    [Theory]
    [InlineData("0.5", 0.5)]
    [InlineData("0", 0.0)]
    [InlineData("2", 2.0)]
    public void Double_InsideTheRangeInclusive_ReturnsIt(string json, double expected)
    {
        Assert.True(Parse($"{{\"x\":{json}}}").TryGetMergePatchDouble("x", out double value, 0, 2));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("2.1")]
    [InlineData("1e999")]
    [InlineData("\"1\"")]
    [InlineData("false")]
    [InlineData("null")]
    public void Double_OutsideTheRangeOrNotFinite_ThrowsNamingTheField(string json)
    {
        ValidationException ex = Assert.Throws<ValidationException>(() =>
            Parse($"{{\"x\":{json}}}").TryGetMergePatchDouble("x", out _, 0, 2)
        );
        Assert.Contains("'x'", ex.Message);
    }

    [Fact]
    public void String_WithAString_ReturnsIt()
    {
        Assert.True(Parse("{\"x\":\"abc\"}").TryGetMergePatchString("x", out string value, false));
        Assert.Equal("abc", value);
    }

    [Fact]
    public void String_WithNullWhereNullIsAllowed_ReturnsTrueAndNull()
    {
        Assert.True(Parse("{\"x\":null}").TryGetMergePatchString("x", out string value, true));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("1", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("[]", false)]
    public void String_WithAnythingElse_ThrowsNamingTheField(string json, bool allowNull)
    {
        ValidationException ex = Assert.Throws<ValidationException>(() =>
            Parse($"{{\"x\":{json}}}").TryGetMergePatchString("x", out _, allowNull)
        );
        Assert.Contains("'x'", ex.Message);
    }

    [Theory]
    [InlineData("00:00:30", 30)]
    [InlineData("00:00:01", 1)]
    [InlineData("01:00:00", 3600)]
    public void Duration_InsideTheRangeInclusive_ReturnsIt(string text, int seconds)
    {
        Assert.True(
            Parse($"{{\"x\":\"{text}\"}}")
                .TryGetMergePatchDuration(
                    "x",
                    out TimeSpan value,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromHours(1)
                )
        );
        Assert.Equal(TimeSpan.FromSeconds(seconds), value);
    }

    [Theory]
    [InlineData("\"00:00:00\"")]
    [InlineData("\"-00:00:05\"")]
    [InlineData("\"01:00:01\"")]
    [InlineData("\"not-a-timespan\"")]
    [InlineData("\"\"")]
    [InlineData("30")]
    [InlineData("null")]
    [InlineData("true")]
    public void Duration_OutsideTheRangeOrUnparseableOrOfAnotherKind_ThrowsNamingTheField(
        string json
    )
    {
        ValidationException ex = Assert.Throws<ValidationException>(() =>
            Parse($"{{\"x\":{json}}}")
                .TryGetMergePatchDuration(
                    "x",
                    out _,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromHours(1)
                )
        );
        Assert.Contains("'x'", ex.Message);
        Assert.Contains("hh:mm:ss", ex.Message);
    }

    [Fact]
    public void Duration_ParsesWithTheInvariantCultureWhateverTheCurrentCultureIs()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            // A decimal comma is invalid for the invariant culture and must not be accepted just
            // because the server runs under de-DE.
            Assert.Throws<ValidationException>(() =>
                Parse("{\"x\":\"00:00:01,5\"}")
                    .TryGetMergePatchDuration("x", out _, TimeSpan.Zero, TimeSpan.FromHours(1))
            );
            Assert.True(
                Parse("{\"x\":\"00:00:01.5\"}")
                    .TryGetMergePatchDuration(
                        "x",
                        out TimeSpan value,
                        TimeSpan.Zero,
                        TimeSpan.FromHours(1)
                    )
            );
            Assert.Equal(TimeSpan.FromSeconds(1.5), value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Duration_AtTheTimerMaximum_IsAcceptedAndOneMillisecondMoreIsRejected()
    {
        TimeSpan max = JsonElementMergePatchExtension.TimerMaximum;
        Assert.Equal(TimeSpan.FromMilliseconds(uint.MaxValue - 1), max);

        string atMax = max.ToString("c", CultureInfo.InvariantCulture);
        string overMax = (max + TimeSpan.FromMilliseconds(1)).ToString(
            "c",
            CultureInfo.InvariantCulture
        );

        Assert.True(
            Parse($"{{\"x\":\"{atMax}\"}}")
                .TryGetMergePatchDuration("x", out TimeSpan value, TimeSpan.Zero, max)
        );
        Assert.Equal(max, value);
        Assert.Throws<ValidationException>(() =>
            Parse($"{{\"x\":\"{overMax}\"}}")
                .TryGetMergePatchDuration("x", out _, TimeSpan.Zero, max)
        );
        Assert.Throws<ValidationException>(() =>
            Parse("{\"x\":\"10675199.00:00:00\"}")
                .TryGetMergePatchDuration("x", out _, TimeSpan.Zero, max)
        );
    }

    [Fact]
    public void HttpTimeoutMaximum_IsInt32MaxMilliseconds()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(int.MaxValue),
            JsonElementMergePatchExtension.HttpTimeoutMaximum
        );
    }

    [Fact]
    public void StringList_WithAnArrayOfStrings_ReturnsTheElements()
    {
        Assert.True(
            Parse("{\"x\":[\"a\",\"b\"]}")
                .TryGetMergePatchStringList("x", out List<string> v, false)
        );
        Assert.Equal(["a", "b"], v);
    }

    [Fact]
    public void StringList_WithAnEmptyArray_ReturnsAnEmptyList()
    {
        Assert.True(Parse("{\"x\":[]}").TryGetMergePatchStringList("x", out List<string> v, false));
        Assert.Empty(v);
    }

    [Fact]
    public void StringList_WithNullWhereNullIsAllowed_ReturnsTrueAndNull()
    {
        Assert.True(
            Parse("{\"x\":null}").TryGetMergePatchStringList("x", out List<string> v, true)
        );
        Assert.Null(v);
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("\"a\"", true)]
    [InlineData("1", false)]
    [InlineData("{}", false)]
    [InlineData("[1]", false)]
    [InlineData("[\"a\",null]", true)]
    [InlineData("[[\"a\"]]", false)]
    public void StringList_WithAnythingElse_ThrowsNamingTheField(string json, bool allowNull)
    {
        ValidationException ex = Assert.Throws<ValidationException>(() =>
            Parse($"{{\"x\":{json}}}").TryGetMergePatchStringList("x", out _, allowNull)
        );
        Assert.Contains("'x'", ex.Message);
    }

    [Theory]
    [InlineData("\"Monday\"", DayOfWeek.Monday)]
    [InlineData("\"friday\"", DayOfWeek.Friday)]
    [InlineData("\"SUNDAY\"", DayOfWeek.Sunday)]
    public void Enum_WithADefinedNameInAnyCase_ReturnsIt(string json, DayOfWeek expected)
    {
        Assert.True(Parse($"{{\"x\":{json}}}").TryGetMergePatchEnum("x", out DayOfWeek value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("\"Funday\"")]
    [InlineData("\"1\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[]")]
    public void Enum_WithAnUndefinedNameOrAnotherKind_ThrowsNamingTheField(string json)
    {
        ValidationException ex = Assert.Throws<ValidationException>(() =>
            Parse($"{{\"x\":{json}}}").TryGetMergePatchEnum("x", out DayOfWeek _)
        );
        Assert.Contains("'x'", ex.Message);
        Assert.Contains("Monday", ex.Message);
    }
}

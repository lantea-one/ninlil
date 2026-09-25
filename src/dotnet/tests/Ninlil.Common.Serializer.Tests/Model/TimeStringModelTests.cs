using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     These tests cover <see cref="TimeStringModel" />: parsing, component rollover, conversions
///     and <see cref="TimeStringModel.IsValid" />. A populated model must be valid and an empty one
///     invalid, because <see cref="StructuredStringModel.IsNull" /> is <c>!IsValid()</c> (see
///     <see cref="IStructuredStringModel.IsValid" />).
/// </summary>
public class TimeStringModelTests
{
    [Fact]
    public void Empty_IsInvalid_AndIsNull()
    {
        TimeStringModel empty = TimeStringModel.Empty;

        Assert.False(empty.IsValid());
        Assert.True(empty.IsNull());
    }

    [Fact]
    public void Populated_IsValid_AndIsNotNull()
    {
        TimeStringModel populated = TimeStringModel.FromString("1d2h3m4s5ms");

        Assert.True(populated.IsValid());
        Assert.False(populated.IsNull());
    }

    [Theory]
    [InlineData("1d", 1, 0, 0, 0, 0)]
    [InlineData("2h", 0, 2, 0, 0, 0)]
    [InlineData("3m", 0, 0, 3, 0, 0)]
    [InlineData("4s", 0, 0, 0, 4, 0)]
    [InlineData("5ms", 0, 0, 0, 0, 5)]
    [InlineData("1d2h3m4s5ms", 1, 2, 3, 4, 5)]
    public void Parses_EachComponent(
        string input,
        int days,
        int hours,
        int minutes,
        int seconds,
        int milliseconds
    )
    {
        TimeStringModel model = TimeStringModel.FromString(input);

        Assert.Equal(days, model.Days);
        Assert.Equal(hours, model.Hours);
        Assert.Equal(minutes, model.Minutes);
        Assert.Equal(seconds, model.Seconds);
        Assert.Equal(milliseconds, model.Milliseconds);
    }

    [Fact]
    public void Normalize_CarriesOverflowingComponentsUpward()
    {
        // Each component's regex group allows up to 3 digits, so milliseconds tops out at 999
        // here; the overflow still exercises every rollover: seconds -> minutes -> hours -> days.
        // 90s -> +1m 30s; 91m -> +1h 31m; 26h -> +1d 2h.
        TimeStringModel model = TimeStringModel.FromString("25h90m90s999ms");

        Assert.Equal(1, model.Days);
        Assert.Equal(2, model.Hours);
        Assert.Equal(31, model.Minutes);
        Assert.Equal(30, model.Seconds);
        Assert.Equal(999, model.Milliseconds);
    }

    [Fact]
    public void ToString_OmitsZeroComponents()
    {
        TimeStringModel model = TimeStringModel.FromString("1d3s");

        Assert.Equal("1d3s", model.ToString());
    }

    [Fact]
    public void ToTimeSpan_ReflectsParsedComponents()
    {
        TimeStringModel model = TimeStringModel.FromString("1d2h3m4s5ms");

        TimeSpan span = model.ToTimeSpan();

        Assert.Equal(new TimeSpan(1, 2, 3, 4, 5), span);
    }

    [Fact]
    public void FromTimeSpan_And_ImplicitConversions_RoundTrip()
    {
        TimeSpan span = new(1, 2, 3, 4, 5);
        TimeStringModel model = TimeStringModel.FromTimeSpan(span);

        Assert.Equal(span, (TimeSpan)model);

        // Implicit string conversion.
        string asString = model;
        Assert.Equal(model.ToString(), asString);

        // Implicit conversion back from a string.
        TimeStringModel roundTripped = asString;
        Assert.Equal(span, roundTripped.ToTimeSpan());
    }

    [Fact]
    public void ImplicitConversion_FromNullableTimeSpan_Null_ReturnsNull()
    {
        TimeSpan? nullSpan = null;
        TimeStringModel model = nullSpan;

        Assert.Null(model);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        bool success = TimeStringModel.TryParse("not-a-timespan", out TimeStringModel model);

        Assert.False(success);
        Assert.Null(model);
    }

    [Fact]
    public void RoundTrips_ThroughJson()
    {
        string json = JsonSerializer.Serialize(TimeStringModel.FromString("2h30m"));
        TimeStringModel result = JsonSerializer.Deserialize<TimeStringModel>(json);

        Assert.Equal("2h30m", result.ToString());
    }

    [Fact]
    public void Since_ComputesElapsedTime()
    {
        DateTime tenMinutesAgo = DateTime.Now.AddMinutes(-10);

        TimeStringModel elapsed = TimeStringModel.Since(tenMinutesAgo);

        // Allow generous tolerance since this test itself takes non-zero time to run.
        Assert.InRange(elapsed.ToTimeSpan().TotalMinutes, 9.5, 10.5);
    }
}

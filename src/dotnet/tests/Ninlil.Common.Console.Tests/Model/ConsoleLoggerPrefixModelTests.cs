using Ninlil.Common.Console.Model;

// Define our namespace for the console-logger-prefix-model tests.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     This test class exercises <see cref="ConsoleLoggerPrefixModel" /> - part sanitization, index
///     lookup, formatted/padded rendering, the merge operation (including a real bug fixed in this
///     project), and the With*/WithoutPart mutators.
/// </summary>
public class ConsoleLoggerPrefixModelTests
{
    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.WithPart" /> sanitizes and adds a valid part.</summary>
    [Fact]
    public void WithPart_AddsSanitizedPart()
    {
        var model = new ConsoleLoggerPrefixModel().WithPart("  [my-part]  ");

        Assert.Single(model);
        Assert.Equal("my-part", model[0]);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.WithPart" /> ignores a null or empty part.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithPart_IgnoresNullOrEmptyPart(string part)
    {
        var model = new ConsoleLoggerPrefixModel().WithPart(part);

        Assert.Empty(model);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.WithParts" /> adds every valid part in order.</summary>
    [Fact]
    public void WithParts_AddsEveryPartInOrder()
    {
        var model = new ConsoleLoggerPrefixModel().WithParts("first", "second");

        Assert.Equal(["first", "second"], model);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.GetPartIndex" /> returns the index of an existing part.</summary>
    [Fact]
    public void GetPartIndex_ReturnsIndexOfExistingPart()
    {
        var model = new ConsoleLoggerPrefixModel().WithParts("first", "second");

        Assert.Equal(1, model.GetPartIndex("second"));
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.GetPartIndex" /> returns -1 for a missing part.</summary>
    [Fact]
    public void GetPartIndex_ReturnsNegativeOneForMissingPart()
    {
        var model = new ConsoleLoggerPrefixModel().WithPart("first");

        Assert.Equal(-1, model.GetPartIndex("missing"));
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.GetParts" /> without formatting returns the sanitized raw parts.</summary>
    [Fact]
    public void GetParts_Unformatted_ReturnsRawParts()
    {
        var model = new ConsoleLoggerPrefixModel().WithParts("first", "second");

        Assert.Equal(["first", "second"], model.GetParts());
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.GetParts" /> with formatting wraps each part in square brackets.</summary>
    [Fact]
    public void GetParts_Formatted_WrapsEachPartInBrackets()
    {
        var model = new ConsoleLoggerPrefixModel().WithPart("info");

        List<string> result = model.GetParts(true);

        Assert.Equal("[info]", result[0]);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleLoggerPrefixModel.Merge" /> adds a part from the other model
    ///     that doesn't already exist in this one.
    /// </summary>
    [Fact]
    public void Merge_AddsNewPartFromOther()
    {
        var model = new ConsoleLoggerPrefixModel().WithPart("first");
        var other = new ConsoleLoggerPrefixModel().WithPart("second");

        model.Merge(other);

        Assert.Equal(["first", "second"], model);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleLoggerPrefixModel.Merge" /> does not duplicate a part that
    ///     already exists in this model, even when that part sits at a different index in the other
    ///     model. This is a real bug found and fixed in this project: the original condition compared
    ///     the part's index in each list to each other (<c>GetPartIndex(o) != other.GetPartIndex(o)</c>)
    ///     rather than checking whether the part already existed in this model at all, so merging any
    ///     two prefix models that shared a part at different positions produced a duplicate.
    /// </summary>
    [Fact]
    public void Merge_DoesNotDuplicateExistingPartAtDifferentIndex()
    {
        var model = new ConsoleLoggerPrefixModel().WithParts("A", "B");
        var other = new ConsoleLoggerPrefixModel().WithParts("C", "A");

        model.Merge(other);

        Assert.Equal(["A", "B", "C"], model);
        Assert.Single(model, p => p == "A");
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.Merge" /> with a null other model is a no-op.</summary>
    [Fact]
    public void Merge_WithNullOther_IsNoOp()
    {
        var model = new ConsoleLoggerPrefixModel().WithPart("first");

        model.Merge(null);

        Assert.Single(model);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleLoggerPrefixModel.ToString()" /> joins the formatted (bracket-wrapped)
    ///     parts with spaces - the parameterless overload always renders formatted, via
    ///     <see cref="ConsoleLoggerPrefixModel.ToString(int)" /> with zero padding.
    /// </summary>
    [Fact]
    public void ToString_JoinsFormattedPartsWithSpaces()
    {
        var model = new ConsoleLoggerPrefixModel().WithParts("first", "second");

        Assert.Equal("[first] [second]", model.ToString());
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.WithoutPart(object)" /> removes a part by its string value.</summary>
    [Fact]
    public void WithoutPart_ByString_RemovesMatchingPart()
    {
        var model = new ConsoleLoggerPrefixModel().WithParts("first", "second");

        model.WithoutPart("first");

        Assert.DoesNotContain("first", model);
        Assert.Contains("second", model);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.WithoutPart(object)" /> removes a part by its positive index.</summary>
    [Fact]
    public void WithoutPart_ByPositiveIndex_RemovesAtIndex()
    {
        var model = new ConsoleLoggerPrefixModel().WithParts("first", "second");

        model.WithoutPart(0);

        Assert.Equal(["second"], model);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerPrefixModel.WithoutPart(object)" /> removes a part by its negative index.</summary>
    [Fact]
    public void WithoutPart_ByNegativeIndex_RemovesFromEnd()
    {
        var model = new ConsoleLoggerPrefixModel().WithParts("first", "second");

        model.WithoutPart(-1);

        Assert.Equal(["first"], model);
    }

    /// <summary>Verifies the implicit conversion from a string array sanitizes and populates a new instance.</summary>
    [Fact]
    public void ImplicitOperator_FromStringArray_SanitizesAndPopulates()
    {
        ConsoleLoggerPrefixModel model = new[] { "  first  ", "", null, "second" };

        Assert.Equal(["first", "second"], model);
    }

    /// <summary>Verifies the implicit conversion from a null array returns an empty instance.</summary>
    [Fact]
    public void ImplicitOperator_FromNullArray_ReturnsEmptyInstance()
    {
        ConsoleLoggerPrefixModel model = (string[])null;

        Assert.Empty(model);
    }
}

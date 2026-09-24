using Ninlil.Common.Console.Model;

// Define our namespace for the console-command-validation-model tests.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     This test class exercises <see cref="ConsoleCommandValidationModel" /> - the fluent
///     With*Argument builders, <see cref="ConsoleCommandValidationModel.IsValid" />, and the
///     human-readable string projections.
/// </summary>
public class ConsoleCommandValidationModelTests
{
    /// <summary>Verifies that a freshly constructed model with no arguments added reports valid.</summary>
    [Fact]
    public void IsValid_TrueWhenEmpty()
    {
        ConsoleCommandValidationModel model = new();

        Assert.True(model.IsValid());
    }

    /// <summary>Verifies that adding a missing argument makes the model invalid.</summary>
    [Fact]
    public void IsValid_FalseAfterAddingMissingArgument()
    {
        ConsoleCommandValidationModel model =
            new ConsoleCommandValidationModel().WithMissingArgument("--name");

        Assert.False(model.IsValid());
    }

    /// <summary>Verifies that adding an invalid argument makes the model invalid.</summary>
    [Fact]
    public void IsValid_FalseAfterAddingInvalidArgument()
    {
        ConsoleCommandValidationModel model =
            new ConsoleCommandValidationModel().WithInvalidArgument("--age", "Must be a number.");

        Assert.False(model.IsValid());
    }

    /// <summary>Verifies that adding an unknown argument makes the model invalid.</summary>
    [Fact]
    public void IsValid_FalseAfterAddingUnknownArgument()
    {
        ConsoleCommandValidationModel model =
            new ConsoleCommandValidationModel().WithUnknownArgument("--bogus");

        Assert.False(model.IsValid());
    }

    /// <summary>Verifies that <see cref="ConsoleCommandValidationModel.WithMissingArgument" /> trims and stores the argument name.</summary>
    [Fact]
    public void WithMissingArgument_TrimsAndStores()
    {
        ConsoleCommandValidationModel model =
            new ConsoleCommandValidationModel().WithMissingArgument("  --name  ");

        Assert.Contains("--name", model.MissingArguments);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandValidationModel.WithMissingArgument" /> throws for a null/empty argument.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithMissingArgument_ThrowsForNullOrEmpty(string argument)
    {
        ConsoleCommandValidationModel model = new();

        Assert.Throws<ArgumentNullException>(() => model.WithMissingArgument(argument));
    }

    /// <summary>Verifies that <see cref="ConsoleCommandValidationModel.WithInvalidArgument" /> stores both the argument and its message.</summary>
    [Fact]
    public void WithInvalidArgument_StoresArgumentAndMessage()
    {
        ConsoleCommandValidationModel model =
            new ConsoleCommandValidationModel().WithInvalidArgument("--age", "Must be a number.");

        Assert.Contains(
            model.InvalidArguments,
            kv => kv.Key == "--age" && kv.Value == "Must be a number."
        );
    }

    /// <summary>Verifies that <see cref="ConsoleCommandValidationModel.WithInvalidArgument" /> throws for a null/empty argument or message.</summary>
    [Fact]
    public void WithInvalidArgument_ThrowsForNullArgumentOrMessage()
    {
        ConsoleCommandValidationModel model = new();

        Assert.Throws<ArgumentNullException>(() => model.WithInvalidArgument(null, "message"));
        Assert.Throws<ArgumentNullException>(() => model.WithInvalidArgument("--age", null));
    }

    /// <summary>Verifies that <see cref="ConsoleCommandValidationModel.WithUnknownArgument" /> stores the argument.</summary>
    [Fact]
    public void WithUnknownArgument_Stores()
    {
        ConsoleCommandValidationModel model =
            new ConsoleCommandValidationModel().WithUnknownArgument("--bogus");

        Assert.Contains("--bogus", model.UnknownArguments);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandValidationModel.ToMissingString" /> includes every missing argument.</summary>
    [Fact]
    public void ToMissingString_IncludesEveryMissingArgument()
    {
        ConsoleCommandValidationModel model = new ConsoleCommandValidationModel()
            .WithMissingArgument("--name")
            .WithMissingArgument("--age");

        string result = model.ToMissingString();

        Assert.Contains("--name", result);
        Assert.Contains("--age", result);
        Assert.Contains("Missing Arguments:", result);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandValidationModel.ToInvalidString" /> includes the argument and its message.</summary>
    [Fact]
    public void ToInvalidString_IncludesArgumentAndMessage()
    {
        ConsoleCommandValidationModel model =
            new ConsoleCommandValidationModel().WithInvalidArgument("--age", "Must be a number.");

        string result = model.ToInvalidString();

        Assert.Contains("--age", result);
        Assert.Contains("Must be a number.", result);
        Assert.Contains("Invalid Arguments:", result);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandValidationModel.ToUnknownString" /> includes every unknown argument.</summary>
    [Fact]
    public void ToUnknownString_IncludesEveryUnknownArgument()
    {
        ConsoleCommandValidationModel model =
            new ConsoleCommandValidationModel().WithUnknownArgument("--bogus");

        string result = model.ToUnknownString();

        Assert.Contains("--bogus", result);
        Assert.Contains("Unknown Arguments:", result);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandValidationModel.ToString" /> combines every populated section.</summary>
    [Fact]
    public void ToString_CombinesEveryPopulatedSection()
    {
        ConsoleCommandValidationModel model = new ConsoleCommandValidationModel()
            .WithMissingArgument("--name")
            .WithInvalidArgument("--age", "Must be a number.")
            .WithUnknownArgument("--bogus");

        string result = model.ToString();

        Assert.Contains("Missing Arguments:", result);
        Assert.Contains("Invalid Arguments:", result);
        Assert.Contains("Unknown Arguments:", result);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandValidationModel.ToString" /> omits sections with nothing to report.</summary>
    [Fact]
    public void ToString_OmitsEmptySections()
    {
        ConsoleCommandValidationModel model =
            new ConsoleCommandValidationModel().WithMissingArgument("--name");

        string result = model.ToString();

        Assert.Contains("Missing Arguments:", result);
        Assert.DoesNotContain("Invalid Arguments:", result);
        Assert.DoesNotContain("Unknown Arguments:", result);
    }
}

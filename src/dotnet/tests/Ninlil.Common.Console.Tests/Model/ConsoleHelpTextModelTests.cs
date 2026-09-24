using Ninlil.Common.Console.Model;

// Define our namespace for the console-help-text-model tests.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     This test class exercises <see cref="ConsoleHelpTextModel" /> - name/description formatting
///     for verbs, positional arguments, and named arguments, along with the required-argument
///     bracket convention and padding behavior.
/// </summary>
public class ConsoleHelpTextModelTests
{
    /// <summary>Verifies that a verb's help name is simply its trimmed name.</summary>
    [Fact]
    public void GetHelpName_ForVerb_ReturnsName()
    {
        ConsoleHelpTextModel model = new() { Verb = true, Name = "add" };

        Assert.Equal("add", model.GetHelpName().Trim());
    }

    /// <summary>Verifies that a required positional argument's help name is "&lt;position&gt;".</summary>
    [Fact]
    public void GetHelpName_ForRequiredPositional_ReturnsAngleBracketPosition()
    {
        ConsoleHelpTextModel model = new() { Position = 0, Required = true };

        Assert.Equal("<0>", model.GetHelpName());
    }

    /// <summary>Verifies that an optional positional argument's help name is wrapped in square brackets.</summary>
    [Fact]
    public void GetHelpName_ForOptionalPositional_WrapsInSquareBrackets()
    {
        ConsoleHelpTextModel model = new() { Position = 1, Required = false };

        Assert.Equal("[<1>]", model.GetHelpName());
    }

    /// <summary>Verifies that a required named argument without a short name is formatted "--name".</summary>
    [Fact]
    public void GetHelpName_ForRequiredNamedWithoutShortName_ReturnsDashDashName()
    {
        ConsoleHelpTextModel model = new()
        {
            Position = int.MinValue,
            Name = "name",
            Required = true,
        };

        Assert.Equal("--name", model.GetHelpName());
    }

    /// <summary>Verifies that a required named argument with a short name is formatted "-x, --name".</summary>
    [Fact]
    public void GetHelpName_ForRequiredNamedWithShortName_ReturnsBothForms()
    {
        ConsoleHelpTextModel model = new()
        {
            Position = int.MinValue,
            Name = "age",
            ShortName = 'a',
            Required = true,
        };

        Assert.Equal("-a, --age", model.GetHelpName());
    }

    /// <summary>Verifies that an optional named argument is wrapped in square brackets.</summary>
    [Fact]
    public void GetHelpName_ForOptionalNamed_WrapsInSquareBrackets()
    {
        ConsoleHelpTextModel model = new()
        {
            Position = int.MinValue,
            Name = "verbose",
            Required = false,
        };

        Assert.Equal("[--verbose]", model.GetHelpName());
    }

    /// <summary>Verifies that a default verb's description is prefixed with "[default]".</summary>
    [Fact]
    public void GetHelpDescription_ForDefaultVerb_PrefixesDefaultMarker()
    {
        ConsoleHelpTextModel model = new()
        {
            Verb = true,
            IsDefault = true,
            Description = "The default command.",
        };

        Assert.Equal("[default] The default command.", model.GetHelpDescription());
    }

    /// <summary>Verifies that a non-default verb's description is returned as-is.</summary>
    [Fact]
    public void GetHelpDescription_ForNonDefaultVerb_ReturnsDescriptionAsIs()
    {
        ConsoleHelpTextModel model = new()
        {
            Verb = true,
            IsDefault = false,
            Description = "Adds a new item.",
        };

        Assert.Equal("Adds a new item.", model.GetHelpDescription());
    }

    /// <summary>Verifies that a non-verb entry's description includes the "Required:" prefix.</summary>
    [Fact]
    public void GetHelpDescription_ForNonVerb_IncludesRequiredPrefix()
    {
        ConsoleHelpTextModel model = new()
        {
            Verb = false,
            Required = true,
            Description = "The name.",
        };

        string result = model.GetHelpDescription();

        Assert.Contains("Required: true", result);
        Assert.Contains("The name.", result);
    }

    /// <summary>Verifies that a non-verb entry's description includes a "Default:" prefix when a default value is present.</summary>
    [Fact]
    public void GetHelpDescription_WithDefaultValue_IncludesDefaultPrefix()
    {
        ConsoleHelpTextModel model = new()
        {
            Verb = false,
            Required = false,
            Default = 42,
            Description = "The age.",
        };

        Assert.Contains("Default: 42", model.GetHelpDescription());
    }

    /// <summary>Verifies that a non-verb entry's description includes a "Group:" prefix when a group is present.</summary>
    [Fact]
    public void GetHelpDescription_WithGroup_IncludesGroupPrefix()
    {
        ConsoleHelpTextModel model = new()
        {
            Verb = false,
            Required = false,
            Group = "connection",
            Description = "The host.",
        };

        Assert.Contains("Group: connection", model.GetHelpDescription());
    }

    /// <summary>Verifies that <see cref="ConsoleHelpTextModel.GetPadding" /> returns a fixed value of 4.</summary>
    [Fact]
    public void GetPadding_ReturnsFour()
    {
        ConsoleHelpTextModel model = new();

        Assert.Equal(4, model.GetPadding());
    }

    /// <summary>Verifies that <see cref="ConsoleHelpTextModel.ToString" /> combines the name and description.</summary>
    [Fact]
    public void ToString_CombinesNameAndDescription()
    {
        ConsoleHelpTextModel model = new()
        {
            Position = int.MinValue,
            Name = "name",
            Required = true,
            Description = "The name.",
        };

        string result = model.ToString();

        Assert.Contains("--name", result);
        Assert.Contains("The name.", result);
    }

    /// <summary>Verifies that <see cref="ConsoleHelpTextModel.ToHelpStringPair" /> returns the name and description as a tuple.</summary>
    [Fact]
    public void ToHelpStringPair_ReturnsNameAndDescriptionTuple()
    {
        ConsoleHelpTextModel model = new()
        {
            Position = int.MinValue,
            Name = "name",
            Required = true,
            Description = "The name.",
        };

        Tuple<string, string> pair = model.ToHelpStringPair();

        Assert.Equal("--name", pair.Item1);
        Assert.Contains("The name.", pair.Item2);
    }

    /// <summary>Verifies that name/description/group values are trimmed on assignment.</summary>
    [Fact]
    public void Properties_AreTrimmedOnAssignment()
    {
        ConsoleHelpTextModel model = new()
        {
            Name = "  name  ",
            Description = "  desc  ",
            Group = "  grp  ",
        };

        Assert.Equal("name", model.Name);
        Assert.Equal("desc", model.Description);
        Assert.Equal("grp", model.Group);
    }
}

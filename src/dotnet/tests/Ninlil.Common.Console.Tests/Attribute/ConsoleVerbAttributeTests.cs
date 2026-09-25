using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Attribute;

/// <summary>
///     These tests cover <see cref="ConsoleVerbAttribute" /> and its
///     <see cref="NestedConsoleVerbAttribute" /> subclass by constructing them directly: name
///     sanitizing, defaults, null-name rejection and the help-text projection.
/// </summary>
public class ConsoleVerbAttributeTests
{
    /// <summary>
    ///     Verifies that the constructor stores the name, description, and default flag.
    /// </summary>
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        ConsoleVerbAttribute attribute = new("--deploy", "Deploys the application", true);

        Assert.Equal("deploy", attribute.Name);
        Assert.Equal("Deploys the application", attribute.Description);
        Assert.True(attribute.Default);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleVerbAttribute.Default" /> defaults to false when not specified.
    /// </summary>
    [Fact]
    public void Constructor_DefaultsIsDefaultToFalse()
    {
        ConsoleVerbAttribute attribute = new("build");

        Assert.False(attribute.Default);
    }

    /// <summary>
    ///     Verifies that a null name throws, matching <see cref="NamedConsoleArgumentAttribute.SanitizeArgumentName" />'s contract.
    /// </summary>
    [Fact]
    public void Constructor_WithNullName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ConsoleVerbAttribute(null));
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleVerbAttribute.ToHelpTextModel" /> marks the model as a verb.
    /// </summary>
    [Fact]
    public void ToHelpTextModel_MarksModelAsVerb()
    {
        ConsoleVerbAttribute attribute = new("start", "Starts the service", true);

        ConsoleHelpTextModel model = attribute.ToHelpTextModel();

        Assert.True(model.Verb);
        Assert.Equal("start", model.Name);
        Assert.Equal("Starts the service", model.Description);
        Assert.True(model.IsDefault);
    }

    /// <summary>
    ///     Verifies that <see cref="NestedConsoleVerbAttribute" /> inherits <see cref="ConsoleVerbAttribute" />'s
    ///     name/description behavior.
    /// </summary>
    [Fact]
    public void NestedConsoleVerbAttribute_InheritsNameAndDescription()
    {
        NestedConsoleVerbAttribute attribute = new("child", "A nested command");

        Assert.Equal("child", attribute.Name);
        Assert.Equal("A nested command", attribute.Description);
        Assert.False(attribute.Default);
    }

    /// <summary>
    ///     Verifies that <see cref="NestedConsoleVerbAttribute" /> is itself a <see cref="ConsoleVerbAttribute" />.
    /// </summary>
    [Fact]
    public void NestedConsoleVerbAttribute_IsConsoleVerbAttribute()
    {
        NestedConsoleVerbAttribute attribute = new("child");

        Assert.IsAssignableFrom<ConsoleVerbAttribute>(attribute);
    }
}

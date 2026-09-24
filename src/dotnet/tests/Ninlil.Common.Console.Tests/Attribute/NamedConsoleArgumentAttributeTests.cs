using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Model;

// Define our namespace for the named-console-argument attribute tests.
namespace Ninlil.Common.Console.Tests.Attribute;

/// <summary>
///     This test class exercises <see cref="NamedConsoleArgumentAttribute" /> - name sanitization,
///     both constructor overloads, and the help-text model projection.
/// </summary>
public class NamedConsoleArgumentAttributeTests
{
    /// <summary>
    ///     Verifies that <see cref="NamedConsoleArgumentAttribute.SanitizeArgumentName" /> strips a
    ///     leading double-dash.
    /// </summary>
    [Fact]
    public void SanitizeArgumentName_StripsLeadingDoubleDash()
    {
        Assert.Equal("name", NamedConsoleArgumentAttribute.SanitizeArgumentName("--name"));
    }

    /// <summary>
    ///     Verifies that <see cref="NamedConsoleArgumentAttribute.SanitizeArgumentName" /> strips a
    ///     single leading dash.
    /// </summary>
    [Fact]
    public void SanitizeArgumentName_StripsLeadingSingleDash()
    {
        Assert.Equal("n", NamedConsoleArgumentAttribute.SanitizeArgumentName("-n"));
    }

    /// <summary>
    ///     Verifies that a name without any leading dash is trimmed but otherwise passed through.
    /// </summary>
    [Fact]
    public void SanitizeArgumentName_TrimsPlainName()
    {
        Assert.Equal("name", NamedConsoleArgumentAttribute.SanitizeArgumentName("  name  "));
    }

    /// <summary>
    ///     Verifies that a null or empty name throws <see cref="ArgumentNullException" />.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeArgumentName_ThrowsForNullOrEmpty(string name)
    {
        Assert.Throws<ArgumentNullException>(() =>
            NamedConsoleArgumentAttribute.SanitizeArgumentName(name)
        );
    }

    /// <summary>
    ///     Verifies the single-name constructor sets every property, including the required default.
    /// </summary>
    [Fact]
    public void Constructor_WithNameOnly_SetsDefaults()
    {
        NamedConsoleArgumentAttribute attribute = new(
            "--my-flag",
            "A description",
            false,
            "value",
            "group1"
        );

        Assert.Equal("my-flag", attribute.Name);
        Assert.Equal("A description", attribute.Description);
        Assert.False(attribute.Required);
        Assert.Equal("value", attribute.Default);
        Assert.Equal("group1", attribute.Group);
        Assert.Null(attribute.ShortName);
    }

    /// <summary>
    ///     Verifies the short-name constructor populates <see cref="NamedConsoleArgumentAttribute.ShortName" />
    ///     along with the rest of the shared state.
    /// </summary>
    [Fact]
    public void Constructor_WithShortName_SetsShortName()
    {
        NamedConsoleArgumentAttribute attribute = new('n', "name", "The name", true, "default");

        Assert.Equal('n', attribute.ShortName);
        Assert.Equal("name", attribute.Name);
        Assert.True(attribute.Required);
        Assert.Equal("default", attribute.Default);
    }

    /// <summary>
    ///     Verifies that omitting a name throws, since a console argument must always have a name.
    /// </summary>
    [Fact]
    public void Constructor_WithNullName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NamedConsoleArgumentAttribute(null));
    }

    /// <summary>
    ///     Verifies that <see cref="NamedConsoleArgumentAttribute.ToHelpTextModel" /> projects every
    ///     property across correctly.
    /// </summary>
    [Fact]
    public void ToHelpTextModel_ProjectsAllProperties()
    {
        NamedConsoleArgumentAttribute attribute = new(
            'x',
            "example",
            "An example",
            false,
            42,
            "grp"
        );

        ConsoleHelpTextModel model = attribute.ToHelpTextModel();

        Assert.Equal("example", model.Name);
        Assert.Equal("An example", model.Description);
        Assert.False(model.Required);
        Assert.Equal(42, model.Default);
        Assert.Equal("grp", model.Group);
        Assert.Equal('x', model.ShortName);
    }

    /// <summary>
    ///     Verifies the generic <see cref="NamedConsoleArgumentAttribute{TValue}" /> variant carries a
    ///     strongly-typed default value through to the base <see cref="NamedConsoleArgumentAttribute.Default" />.
    /// </summary>
    [Fact]
    public void GenericVariant_CarriesTypedDefault()
    {
        NamedConsoleArgumentAttribute<int> attribute = new("count", "A count", false, 7);

        Assert.Equal(7, attribute.Default);
    }

    /// <summary>
    ///     Verifies the generic short-name constructor also carries the strongly-typed default through.
    /// </summary>
    [Fact]
    public void GenericVariant_WithShortName_CarriesTypedDefault()
    {
        NamedConsoleArgumentAttribute<bool> attribute = new('f', "flag", "A flag", false, true);

        Assert.Equal('f', attribute.ShortName);
        Assert.Equal(true, attribute.Default);
    }

    /// <summary>
    ///     Verifies that <see cref="NamedConsoleArgumentAttribute" /> derives from
    ///     <see cref="ConsoleArgumentAttribute" />, the shared ancestor for every console argument attribute.
    /// </summary>
    [Fact]
    public void DerivesFrom_ConsoleArgumentAttribute()
    {
        NamedConsoleArgumentAttribute attribute = new("name");

        Assert.IsAssignableFrom<ConsoleArgumentAttribute>(attribute);
    }
}

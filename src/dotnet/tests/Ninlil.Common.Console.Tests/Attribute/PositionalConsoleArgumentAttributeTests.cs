using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Attribute;

/// <summary>
///     These tests cover <see cref="PositionalConsoleArgumentAttribute" /> by constructing it
///     directly: position validation, stored values and the help-text model projection.
/// </summary>
public class PositionalConsoleArgumentAttributeTests
{
    /// <summary>
    ///     Verifies that the constructor stores every provided value.
    /// </summary>
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        PositionalConsoleArgumentAttribute attribute = new(
            2,
            "The third argument",
            false,
            "fallback",
            "group-a"
        );

        Assert.Equal(2, attribute.Position);
        Assert.Equal("The third argument", attribute.Description);
        Assert.False(attribute.Required);
        Assert.Equal("fallback", attribute.Default);
        Assert.Equal("group-a", attribute.Group);
    }

    /// <summary>
    ///     Verifies that a negative position throws <see cref="ArgumentOutOfRangeException" />, since
    ///     position must always be non-negative.
    /// </summary>
    [Fact]
    public void Constructor_WithNegativePosition_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PositionalConsoleArgumentAttribute(-1)
        );
    }

    /// <summary>
    ///     Verifies that a zero position is accepted, since zero is the first non-negative value.
    /// </summary>
    [Fact]
    public void Constructor_WithZeroPosition_Succeeds()
    {
        PositionalConsoleArgumentAttribute attribute = new(0);

        Assert.Equal(0, attribute.Position);
    }

    /// <summary>
    ///     Verifies that <see cref="PositionalConsoleArgumentAttribute.ToHelpTextModel" /> projects
    ///     every property across correctly.
    /// </summary>
    [Fact]
    public void ToHelpTextModel_ProjectsAllProperties()
    {
        PositionalConsoleArgumentAttribute attribute = new(1, "desc", true, 5, "grp");

        ConsoleHelpTextModel model = attribute.ToHelpTextModel();

        Assert.Equal(1, model.Position);
        Assert.Equal("desc", model.Description);
        Assert.True(model.Required);
        Assert.Equal(5, model.Default);
        Assert.Equal("grp", model.Group);
    }

    /// <summary>
    ///     Verifies that the generic <see cref="PositionalConsoleArgumentAttribute{TValue}" /> variant
    ///     behaves identically to its non-generic base for position validation.
    /// </summary>
    [Fact]
    public void GenericVariant_ValidatesPositionLikeBase()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PositionalConsoleArgumentAttribute<int>(-5)
        );
    }

    /// <summary>
    ///     Verifies that <see cref="PositionalConsoleArgumentAttribute" /> derives from
    ///     <see cref="ConsoleArgumentAttribute" />.
    /// </summary>
    [Fact]
    public void DerivesFrom_ConsoleArgumentAttribute()
    {
        PositionalConsoleArgumentAttribute attribute = new(0);

        Assert.IsAssignableFrom<ConsoleArgumentAttribute>(attribute);
    }
}

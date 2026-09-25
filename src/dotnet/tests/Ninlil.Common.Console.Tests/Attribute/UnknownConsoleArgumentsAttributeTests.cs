using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Attribute;

/// <summary>
///     These tests check that <see cref="UnknownConsoleArgumentsAttribute" /> is found by
///     reflection on the <see cref="UnknownArgsModel" /> property it decorates and absent from the
///     others. Capturing unmatched arguments during parsing is covered in
///     <c>ConsoleCommandArgumentCollectionTests</c>.
/// </summary>
public class UnknownConsoleArgumentsAttributeTests
{
    /// <summary>
    ///     Verifies that the attribute can be discovered via reflection on the property it decorates.
    /// </summary>
    [Fact]
    public void Attribute_IsDiscoverableOnDecoratedProperty()
    {
        var property = typeof(UnknownArgsModel).GetProperty(nameof(UnknownArgsModel.Remaining));

        var attribute = property?.GetCustomAttributes(
            typeof(UnknownConsoleArgumentsAttribute),
            true
        );

        Assert.NotNull(attribute);
        Assert.Single(attribute);
    }

    /// <summary>
    ///     Verifies that a property not decorated with the attribute is not discovered.
    /// </summary>
    [Fact]
    public void Attribute_IsNotPresentOnUndecoratedProperty()
    {
        var property = typeof(UnknownArgsModel).GetProperty(nameof(UnknownArgsModel.Known));

        var attribute = property?.GetCustomAttributes(
            typeof(UnknownConsoleArgumentsAttribute),
            true
        );

        Assert.Empty(attribute!);
    }
}

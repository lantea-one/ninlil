using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     This class tests <see cref="TupleTypeExtension" />.
/// </summary>
public class TupleTypeExtensionTests
{
    [Fact]
    public void GetTupleElementTypesReturnsTheSingleGenericArgumentForASingleElementTuple()
    {
        System.Type[] result = typeof(Tuple<int>).GetTupleElementTypes();

        Assert.Equal([typeof(int)], result);
    }

    /// <summary>
    ///     Since <c>IsTypeExtension.IsTuple</c> recognizes every real tuple arity (not just a
    ///     single-element one), <c>GetTupleElementTypes</c> returns the actual element types for a
    ///     multi-element tuple too.
    /// </summary>
    [Fact]
    public void GetTupleElementTypesReturnsEachElementTypeForAMultiElementTuple()
    {
        System.Type[] result = typeof(Tuple<int, string>).GetTupleElementTypes();

        Assert.Equal([typeof(int), typeof(string)], result);
    }

    [Fact]
    public void GetTupleElementTypesReturnsEmptyArrayForNonTupleType()
    {
        Assert.Empty(typeof(List<int>).GetTupleElementTypes());
    }
}

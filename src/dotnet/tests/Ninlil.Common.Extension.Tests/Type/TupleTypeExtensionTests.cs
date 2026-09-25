using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     These tests call <see cref="TupleTypeExtension.GetTupleElementTypes" /> on one- and
///     two-element tuples and on a non-tuple type.
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
    ///     <c>IsTypeExtension.IsTuple</c> recognizes every tuple arity, so
    ///     <c>GetTupleElementTypes</c> returns the element types of a multi-element tuple too.
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

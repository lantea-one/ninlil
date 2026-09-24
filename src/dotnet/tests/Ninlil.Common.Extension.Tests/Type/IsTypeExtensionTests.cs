using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     This class tests <see cref="IsTypeExtension" />.
/// </summary>
public class IsTypeExtensionTests
{
    [Fact]
    public void IsListReturnsFalseForObject()
    {
        Assert.False(typeof(object).IsList());
    }

    [Fact]
    public void IsDictionaryReturnsFalseForObject()
    {
        Assert.False(typeof(object).IsDictionary());
    }

    [Fact]
    public void IsEnumerableTypeReturnsFalseForObject()
    {
        Assert.False(typeof(object).IsEnumerableType());
    }

    [Fact]
    public void IsArrayReturnsTrueForAnyArray()
    {
        Assert.True(typeof(int[]).IsArray());
    }

    [Fact]
    public void IsArrayReturnsFalseForNonArray()
    {
        Assert.False(typeof(List<int>).IsArray());
    }

    [Fact]
    public void IsArrayWithElementTypeMatchesOnlyThatElementType()
    {
        Assert.True(typeof(int[]).IsArray(typeof(int)));
        Assert.False(typeof(int[]).IsArray(typeof(string)));
    }

    [Fact]
    public void IsArrayGenericMatchesElementTypeParameter()
    {
        Assert.True(typeof(string[]).IsArray<string>());
        Assert.False(typeof(string[]).IsArray<int>());
    }

    [Fact]
    public void IsArrayGenericKeyValuePairMatchesArrayOfThatKeyValuePair()
    {
        Assert.True(typeof(KeyValuePair<string, int>[]).IsArray<string, int>());
    }

    [Fact]
    public void IsBooleanMatchesOnlyBoolType()
    {
        Assert.True(typeof(bool).IsBoolean());
        Assert.False(typeof(int).IsBoolean());
    }

    [Fact]
    public void IsDictionaryReturnsTrueForDictionaryType()
    {
        Assert.True(typeof(Dictionary<string, int>).IsDictionary());
    }

    [Fact]
    public void IsDictionaryReturnsTrueForIDictionaryInterface()
    {
        Assert.True(typeof(IDictionary<string, int>).IsDictionary());
    }

    [Fact]
    public void IsDictionaryReturnsFalseForList()
    {
        Assert.False(typeof(List<int>).IsDictionary());
    }

    [Fact]
    public void IsDictionaryWithKeyAndValueTypeMatchesExactGenericArguments()
    {
        Assert.True(typeof(Dictionary<string, int>).IsDictionary(typeof(string), typeof(int)));
        Assert.False(typeof(Dictionary<string, int>).IsDictionary(typeof(int), typeof(string)));
    }

    [Fact]
    public void IsDictionaryGenericMatchesKeyAndValueTypeParameters()
    {
        Assert.True(typeof(Dictionary<string, int>).IsDictionary<string, int>());
        Assert.False(typeof(Dictionary<string, int>).IsDictionary<int, string>());
    }

    [Fact]
    public void IsEnumerableReturnsTrueForIEnumerableOfT()
    {
        Assert.True(typeof(IEnumerable<int>).IsEnumerable());
    }

    [Fact]
    public void IsEnumerableWithValueTypeMatchesExactElementType()
    {
        Assert.True(typeof(IEnumerable<int>).IsEnumerable(typeof(int)));
        Assert.False(typeof(IEnumerable<int>).IsEnumerable(typeof(string)));
    }

    [Fact]
    public void IsEnumerableGenericMatchesElementTypeParameter()
    {
        Assert.True(typeof(IEnumerable<string>).IsEnumerable<string>());
    }

    [Fact]
    public void IsEnumerableGenericKeyValuePairMatchesEnumerableOfThatPair()
    {
        Assert.True(typeof(IEnumerable<KeyValuePair<string, int>>).IsEnumerable<string, int>());
    }

    [Fact]
    public void IsEnumerableTypeReturnsTrueForArrayListDictionaryCollectionAndEnumerable()
    {
        Assert.True(typeof(int[]).IsEnumerableType());
        Assert.True(typeof(List<int>).IsEnumerableType());
        Assert.True(typeof(Dictionary<string, int>).IsEnumerableType());
        Assert.True(typeof(IEnumerable<int>).IsEnumerableType());
    }

    [Fact]
    public void IsEnumerableTypeReturnsFalseForADictionaryWhenExcludingDictionaries()
    {
        Assert.False(typeof(Dictionary<string, int>).IsEnumerableType(false));
    }

    [Fact]
    public void IsEnumerableTypeReturnsFalseForAPlainScalarType()
    {
        Assert.False(typeof(int).IsEnumerableType());
        Assert.False(typeof(string).IsEnumerableType());
    }

    [Fact]
    public void IsKeyValuePairReturnsTrueForKeyValuePairType()
    {
        Assert.True(typeof(KeyValuePair<string, int>).IsKeyValuePair());
    }

    [Fact]
    public void IsKeyValuePairReturnsFalseForNonKeyValuePairType()
    {
        Assert.False(typeof(Dictionary<string, int>).IsKeyValuePair());
    }

    [Fact]
    public void IsKeyValuePairWithKeyAndValueTypeMatchesExactGenericArguments()
    {
        Assert.True(typeof(KeyValuePair<string, int>).IsKeyValuePair(typeof(string), typeof(int)));
        Assert.False(typeof(KeyValuePair<string, int>).IsKeyValuePair(typeof(int), typeof(string)));
    }

    [Fact]
    public void IsKeyValuePairGenericMatchesKeyAndValueTypeParameters()
    {
        Assert.True(typeof(KeyValuePair<string, int>).IsKeyValuePair<string, int>());
    }

    [Fact]
    public void IsListReturnsTrueForListAndIListTypes()
    {
        Assert.True(typeof(List<int>).IsList());
        Assert.True(typeof(IList<int>).IsList());
    }

    [Fact]
    public void IsListReturnsFalseForDictionary()
    {
        Assert.False(typeof(Dictionary<string, int>).IsList());
    }

    [Fact]
    public void IsListWithValueTypeMatchesExactElementType()
    {
        Assert.True(typeof(List<int>).IsList(typeof(int)));
        Assert.False(typeof(List<int>).IsList(typeof(string)));
    }

    [Fact]
    public void IsListGenericMatchesElementTypeParameter()
    {
        Assert.True(typeof(List<string>).IsList<string>());
    }

    [Fact]
    public void IsListGenericKeyValuePairMatchesListOfThatPair()
    {
        Assert.True(typeof(List<KeyValuePair<string, int>>).IsList<string, int>());
    }

    [Fact]
    public void IsNullableReturnsTrueForReferenceType()
    {
        Assert.True(typeof(string).IsNullable());
    }

    [Fact]
    public void IsNullableReturnsTrueForNullableValueType()
    {
        Assert.True(typeof(int?).IsNullable());
    }

    [Fact]
    public void IsNullableReturnsFalseForNonNullableValueType()
    {
        Assert.False(typeof(int).IsNullable());
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(string))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(TimeSpan))]
    [InlineData(typeof(Guid))]
    public void IsPrimitiveReturnsTrueForKnownPrimitiveLikeTypes(System.Type type)
    {
        Assert.True(type.IsPrimitive());
    }

    [Fact]
    public void IsPrimitiveReturnsFalseForAPlainClass()
    {
        Assert.False(typeof(List<int>).IsPrimitive());
    }

    /// <summary>
    ///     <c>IsTuple</c> compares <c>GetGenericTypeDefinition()</c> against every 1-through-8-element
    ///     <c>Tuple&lt;...&gt;</c> open generic type definition, so it recognizes every real tuple arity,
    ///     not just a single-element one.
    /// </summary>
    [Fact]
    public void IsTupleReturnsTrueForEveryTupleArity()
    {
        Assert.True(typeof(Tuple<int>).IsTuple());
        Assert.True(typeof(Tuple<int, string>).IsTuple());
        Assert.True(typeof(Tuple<int, string, bool>).IsTuple());
        Assert.True(typeof(Tuple<int, int, int, int, int, int, int, Tuple<int>>).IsTuple());
    }

    [Fact]
    public void IsTupleReturnsTrueForEveryValueTupleArity()
    {
        Assert.True(typeof(ValueTuple<int>).IsTuple());
        Assert.True(typeof(ValueTuple<int, string>).IsTuple());
        Assert.True(typeof(ValueTuple<int, string, bool>).IsTuple());
    }

    [Fact]
    public void IsTupleReturnsFalseForANonTupleType()
    {
        Assert.False(typeof(List<int>).IsTuple());
    }
}

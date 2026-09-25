using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     These tests cover <see cref="FluentDistinctListModel{TType, TMatcher}" /> with a small record
///     type. Adding through <c>WithItem</c>/<c>WithItems</c> after construction must replace an
///     item with the same distinct value, not only the constructor's up-front
///     <see cref="FluentDistinctListModel{TType, TMatcher}.Distinct" />. Removal by distinct value
///     goes through the base <c>WithoutItems(Func{TType,bool})</c>, which must not modify the list
///     while enumerating it (see <see cref="FluentListModelTests.WithoutItems_ByPredicate_RemovesAllMatches" />).
/// </summary>
public class FluentDistinctListModelTests
{
    private record Item(string Key, string Payload);

    private static FluentDistinctListModel<Item, string> NewList() => new(i => i.Key);

    [Fact]
    public void WithItem_ReplacesExistingItem_WithSameDistinctPropertyValue()
    {
        FluentDistinctListModel<Item, string> list = NewList()
            .WithItem(new Item("a", "first"))
            .WithItem(new Item("a", "second"));

        // Both items have the distinct key "a" but are not equal records (different Payload),
        // so equality-based deduplication would keep both. Only the most recently added may
        // remain.
        Item single = Assert.Single(list);
        Assert.Equal("second", single.Payload);
    }

    [Fact]
    public void WithItem_DistinctKeys_KeepsBothItems()
    {
        FluentDistinctListModel<Item, string> list = NewList()
            .WithItem(new Item("a", "1"))
            .WithItem(new Item("b", "2"));

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Constructor_FromCollection_DedupsUpFront()
    {
        var list = new FluentDistinctListModel<Item, string>(
            i => i.Key,
            [new Item("a", "1"), new Item("a", "2"), new Item("b", "3")]
        );

        Assert.Equal(2, list.Count);
        Assert.Contains(list, i => i.Key == "b");

        // Distinct() keeps the first occurrence for a given key.
        Assert.Equal("1", list.Single(i => i.Key == "a").Payload);
    }

    [Fact]
    public void WithoutItem_ByMatchValue_RemovesMatchingItem()
    {
        FluentDistinctListModel<Item, string> list = NewList()
            .WithItem(new Item("a", "1"))
            .WithItem(new Item("b", "2"));

        list.WithoutItem("a");

        Item remaining = Assert.Single(list);
        Assert.Equal("b", remaining.Key);
    }

    [Fact]
    public void WithoutItems_ByMatchValues_RemovesEachMatch_WithoutThrowing()
    {
        // With more than one match, removing while lazily enumerating the list would throw
        // InvalidOperationException here.
        FluentDistinctListModel<Item, string> list = NewList()
            .WithItem(new Item("a", "1"))
            .WithItem(new Item("b", "2"))
            .WithItem(new Item("c", "3"));

        list.WithoutItems(["a", "c"]);

        Item remaining = Assert.Single(list);
        Assert.Equal("b", remaining.Key);
    }

    [Fact]
    public void WithoutItems_ByParamsMatchValues_RemovesEachMatch()
    {
        FluentDistinctListModel<Item, string> list = NewList()
            .WithItem(new Item("a", "1"))
            .WithItem(new Item("b", "2"));

        list.WithoutItems("a", "b");

        Assert.Empty(list);
    }

    [Fact]
    public void Distinct_StaticHelper_KeepsFirstOccurrencePerKey()
    {
        IEnumerable<Item> result = FluentDistinctListModel<Item, string>.Distinct(
            [new Item("a", "1"), new Item("a", "2"), new Item("b", "3")],
            i => i.Key
        );

        Assert.Equal(["1", "3"], result.Select(i => i.Payload));
    }
}

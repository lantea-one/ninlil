using Ninlil.Common.Serializer.Model;

namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     This class contains unit tests for <see cref="FluentDistinctListModel{TType, TMatcher}" />.
///     These tests specifically lock in a fix for a bug where adding an item via
///     <see cref="FluentListModel{TType}.WithItem(TType)" /> (inherited, unoverridden) never
///     enforced distinctness by the configured property selector -- only the constructor's own
///     up-front <see cref="FluentDistinctListModel{TType, TMatcher}.Distinct" /> call did, so
///     calling <c>WithItem</c>/<c>WithItems</c> after construction could leave two items sharing
///     the same distinct property value sitting side by side in the list, defeating the whole
///     point of the class. The fix makes the base class's <c>WithItem</c> virtual and overrides it
///     here to remove any existing item with a matching distinct property value first.
///     These tests also exercise <see cref="FluentDistinctListModel{TType, TMatcher}.WithoutItem(TMatcher)" />
///     and <see cref="FluentDistinctListModel{TType, TMatcher}.WithoutItems(IEnumerable{TMatcher})" />,
///     both of which route through the same <c>WithoutItems(Func{TType,bool})</c> path whose
///     lazy-enumeration-during-mutation bug is fixed and covered by
///     <see cref="FluentListModelTests.WithoutItems_ByPredicate_RemovesAllMatches" />.
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

        // Two items were added with the same distinct key ("a") but are NOT equal by the record's
        // own value-equality (different Payload) -- the base class's default equality-based
        // WithItem would never have deduplicated these. Only one must remain, and it must be the
        // most recently added one.
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
        // This specifically exercises the same predicate-based removal path whose lazy-enumeration
        // bug was fixed on the base class -- with more than one match, the old implementation would
        // throw InvalidOperationException here.
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

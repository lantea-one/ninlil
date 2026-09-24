using Ninlil.Common.Serializer.Model;

namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     This class contains unit tests for <see cref="FluentListModel{TType}" />.
/// </summary>
public class FluentListModelTests
{
    [Fact]
    public void WithItem_AddsItem_AndReturnsSelfForChaining()
    {
        var list = new FluentListModel<int>();

        FluentListModel<int> result = list.WithItem(1).WithItem(2).WithItem(3);

        Assert.Same(list, result);
        Assert.Equal([1, 2, 3], list);
    }

    [Fact]
    public void WithItem_RemovesAnExistingEqualItem_BeforeAdding()
    {
        // Base-class WithItem dedups by ordinary equality, not by any distinct property -- it
        // just removes an exactly-equal item first, so re-adding "1" moves it to the end rather
        // than producing a duplicate.
        var list = new FluentListModel<int>().WithItems(1, 2, 3);

        list.WithItem(1);

        Assert.Equal([2, 3, 1], list);
    }

    [Fact]
    public void WithItems_AddsMultipleItems()
    {
        var list = new FluentListModel<string>().WithItems(["a", "b", "c"]);

        Assert.Equal(["a", "b", "c"], list);
    }

    [Fact]
    public void WithoutItem_RemovesMatchingItem()
    {
        var list = new FluentListModel<int>().WithItems(1, 2, 3);

        list.WithoutItem(2);

        Assert.Equal([1, 3], list);
    }

    [Fact]
    public void WithoutItem_NullItem_IsNoOp()
    {
        var list = new FluentListModel<string>().WithItems(["a", "b"]);

        FluentListModel<string> result = list.WithoutItem((string)null);

        Assert.Same(list, result);
        Assert.Equal(["a", "b"], list);
    }

    [Fact]
    public void WithoutItem_ByPredicate_RemovesFirstMatch()
    {
        var list = new FluentListModel<int>().WithItems(1, 2, 3, 4);

        list.WithoutItem(i => i % 2 == 0);

        Assert.Equal([1, 3, 4], list);
    }

    [Fact]
    public void WithoutItems_ByPredicate_RemovesAllMatches()
    {
        var list = new FluentListModel<int>().WithItems(1, 2, 3, 4, 5);

        list.WithoutItems(i => i % 2 == 0);

        Assert.Equal([1, 3, 5], list);
    }

    [Fact]
    public void WithoutItems_ByCollection_RemovesEach()
    {
        var list = new FluentListModel<int>().WithItems(1, 2, 3, 4);

        list.WithoutItems([2, 4]);

        Assert.Equal([1, 3], list);
    }

    [Fact]
    public void ClearItems_RemovesEverything()
    {
        var list = new FluentListModel<int>().WithItems(1, 2, 3);

        list.ClearItems();

        Assert.Empty(list);
    }

    [Fact]
    public void Constructor_FromCapacity_StartsEmpty()
    {
        var list = new FluentListModel<int>(10);

        Assert.Empty(list);
    }

    [Fact]
    public void Constructor_FromCollection_CopiesItems()
    {
        var list = new FluentListModel<int>((IEnumerable<int>)[1, 2, 3]);

        Assert.Equal([1, 2, 3], list);
    }
}

using System.Text.RegularExpressions;
using Ninlil.Common.Extension.Object;
using Ninlil.Common.Extension.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Object;

/// <summary>
///     These tests render objects, dictionaries and lists of dictionaries with
///     <see cref="ToHtmlTableObjectExtension" /> and check the HTML for the expected elements and
///     values, plus the exceptions for null and non-class inputs and cancellation.
/// </summary>
public class ToHtmlTableObjectExtensionTests
{
    /// <summary>
    ///     A class with no public properties, used together with <see cref="WrapperWithNestedEmptyClass" />
    ///     to exercise the nested-class recursion branch without any primitive-leaf values involved.
    /// </summary>
    private class EmptyClass;

    /// <summary>
    ///     A class whose only property is itself a class with no properties of its own, so recursing into
    ///     it bottoms out safely with nothing left to iterate.
    /// </summary>
    private class WrapperWithNestedEmptyClass
    {
        public EmptyClass Nested { get; set; } = new();
    }

    /// <summary>
    ///     The nested-class check in the property loop tests each property value's type, not the
    ///     enclosing instance's type (which always passes), so a plain class with primitive properties
    ///     renders instead of recursing into a primitive and throwing.
    /// </summary>
    [Fact]
    public async Task ToHtmlTableAsyncOnPlainClassWithAPrimitivePropertyRendersSuccessfully()
    {
        SampleModel instance = new() { Name = "widget", Number = 7 };

        string html = await instance.ToHtmlTableAsync();

        Assert.Contains("<table", html);
        Assert.Contains("<thead", html);
        Assert.Contains("widget", html);
        Assert.Contains("7", html);
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnClassWithOnlyNestedClassPropertiesRendersSuccessfully()
    {
        WrapperWithNestedEmptyClass instance = new();

        string html = await instance.ToHtmlTableAsync();

        Assert.Contains("<table", html);
        Assert.Contains("<thead", html);
        Assert.Contains("Nested", html);
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnDictionaryEmitsATableFromKeysAndValues()
    {
        Dictionary<string, object> instance = new() { ["Key"] = "Value" };

        string html = await instance.ToHtmlTableAsync();

        Assert.Contains("Key", html);
        Assert.Contains("Value", html);
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnEnumerableOfDictionariesEmitsARowPerDictionary()
    {
        List<Dictionary<string, object>> instance = [new() { ["A"] = "1" }, new() { ["A"] = "2" }];

        string html = await instance.ToHtmlTableAsync();

        Assert.Contains("A", html);
        Assert.Contains("1", html);
        Assert.Contains("2", html);
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnDictionaryWithRowOnlyOmitsTableAndHeaderElements()
    {
        Dictionary<string, object> instance = new() { ["Key"] = "row-only-value" };

        string html = await instance.ToHtmlTableAsync(rowOnly: true);

        Assert.DoesNotContain("<table", html);
        Assert.DoesNotContain("<thead", html);
        Assert.Contains("row-only-value", html);
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnDictionaryIncludesProvidedTableAttributes()
    {
        Dictionary<string, object> instance = new() { ["Key"] = "value" };

        string html = await instance.ToHtmlTableAsync(
            new Dictionary<string, object> { ["class"] = "my-table" }
        );

        Assert.Contains("my-table", html);
    }

    [Fact]
    public async Task ToHtmlTableAsyncThrowsForNullInstance()
    {
        object instance = null;

        await Assert.ThrowsAsync<ArgumentNullException>(() => instance.ToHtmlTableAsync());
    }

    [Fact]
    public async Task ToHtmlTableAsyncThrowsForAPrimitiveInstance()
    {
        object instance = 42;

        await Assert.ThrowsAsync<ArgumentException>(() => instance.ToHtmlTableAsync());
    }

    [Fact]
    public async Task ToHtmlTableAsyncThrowsForAStringInstance()
    {
        object instance = "just a string";

        await Assert.ThrowsAsync<ArgumentException>(() => instance.ToHtmlTableAsync());
    }

    [Fact]
    public async Task ToHtmlTableAsyncThrowsForAnEnumInstance()
    {
        object instance = DayOfWeek.Monday;

        await Assert.ThrowsAsync<ArgumentException>(() => instance.ToHtmlTableAsync());
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnDictionaryCanBeCancelled()
    {
        Dictionary<string, object> instance = new() { ["Key"] = "value" };
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            instance.ToHtmlTableAsync(stoppingToken: cts.Token)
        );
    }

    /// <summary>
    ///     A class that holds a reference to itself, used to prove that a cycle ends in a type name
    ///     instead of unbounded recursion.
    /// </summary>
    private class SelfReferencingNode
    {
        public string Label { get; set; } = "node";

        public SelfReferencingNode Self { get; set; }
    }

    /// <summary>
    ///     A class with an indexer, which has no value without an index argument and must be skipped.
    /// </summary>
    private class ClassWithIndexer
    {
        public string Name { get; set; } = "indexed";

        public string this[int index] => index.ToString();
    }

    /// <summary>
    ///     Counts the occurrences of an element's opening tag by plain string matching, so the tests
    ///     need no HTML library.
    /// </summary>
    /// <param name="html">The HTML to search.</param>
    /// <param name="tag">The element name, such as <c>tr</c>.</param>
    private static int Count(string html, string tag) =>
        Regex.Matches(html, "<" + tag + "[ >]").Count;

    [Fact]
    public async Task ToHtmlTableAsyncOnDictionaryWritesOneBodyRowWithOneCellPerKey()
    {
        Dictionary<string, object> instance = new() { ["A"] = 1, ["B"] = 2 };

        string html = await instance.ToHtmlTableAsync();

        // One header row plus one body row, two header cells and two body cells.
        Assert.Equal(2, Count(html, "tr"));
        Assert.Equal(2, Count(html, "th"));
        Assert.Equal(2, Count(html, "td"));
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnDictionaryWithRowOnlyWritesExactlyOneRow()
    {
        Dictionary<string, object> instance = new() { ["A"] = 1, ["B"] = 2 };

        string html = await instance.ToHtmlTableAsync(rowOnly: true);

        Assert.Equal(1, Count(html, "tr"));
        Assert.Equal(2, Count(html, "td"));
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnAClassWritesOneBodyRowWithOneCellPerProperty()
    {
        SampleModel instance = new() { Name = "widget", Number = 7 };

        string html = await instance.ToHtmlTableAsync();

        // Name, Number and FixedValue: one header row, one body row.
        Assert.Equal(2, Count(html, "tr"));
        Assert.Equal(3, Count(html, "th"));
        Assert.Equal(3, Count(html, "td"));
    }

    [Fact]
    public async Task ToHtmlTableAsyncWithANestedDictionaryWritesItOnceAsATable()
    {
        Dictionary<string, object> instance = new()
        {
            ["Outer"] = new Dictionary<string, object> { ["Inner"] = "deep" },
        };

        string html = await instance.ToHtmlTableAsync();

        Assert.Equal(2, Count(html, "table"));
        Assert.Single(Regex.Matches(html, "deep"));
        Assert.DoesNotContain("System.Collections.Generic.Dictionary", html);
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnAListTakesTheUnionOfKeysAndLeavesMissingCellsEmpty()
    {
        List<Dictionary<string, object>> instance =
        [
            new() { ["A"] = "1" },
            new() { ["A"] = "2", ["B"] = "3" },
        ];

        string html = await instance.ToHtmlTableAsync();

        // Header row plus two body rows; headers A and B; every body row has both cells.
        Assert.Equal(3, Count(html, "tr"));
        Assert.Equal(2, Count(html, "th"));
        Assert.Contains("<td></td>", html);
        Assert.Contains("<td></td>", html.Replace(" ", string.Empty).Replace("\n", string.Empty));
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnASelfReferencingClassFinishesWithTheTypeName()
    {
        SelfReferencingNode instance = new();
        instance.Self = instance;

        string html = await instance.ToHtmlTableAsync();

        Assert.Contains(nameof(SelfReferencingNode), html);
        Assert.Equal(1, Count(html, "table"));
    }

    [Fact]
    public async Task ToHtmlTableAsyncOnAClassWithAnIndexerSkipsTheIndexer()
    {
        ClassWithIndexer instance = new();

        string html = await instance.ToHtmlTableAsync();

        Assert.Contains("indexed", html);
        Assert.Equal(1, Count(html, "th"));
    }
}

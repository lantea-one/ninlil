using Ninlil.Common.Extension.Object;
using Ninlil.Common.Extension.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Object;

/// <summary>
///     This class tests <see cref="ToHtmlTableObjectExtension" />.
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
    ///     The per-property rendering loop's nested-class recursion guard now inspects the current
    ///     property's own <c>value.GetType()</c> instead of the enclosing <c>instance.GetType()</c> (which
    ///     is constant for the whole loop and always a class, since that same guard already passed once
    ///     earlier in the method to get this far) -- so a plain POCO with primitive-typed properties
    ///     renders successfully instead of always recursing into every non-null property value and
    ///     throwing on the first primitive one.
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
}

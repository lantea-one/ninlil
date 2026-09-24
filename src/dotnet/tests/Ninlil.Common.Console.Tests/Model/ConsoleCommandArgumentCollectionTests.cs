using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace for the console-command-argument-collection tests.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     This test class exercises <see cref="ConsoleCommandArgumentCollection" /> - raw CLI tokenization
///     (positional/named/switch/embedded-value forms), the implicit conversions and factory methods,
///     the reflection-driven Parse*Arguments helpers, and the top-level Parse/TryParse orchestration.
///     Every scenario below deliberately supplies every required argument (and never starts with a
///     help-request token) so that the real, unmockable <see cref="Environment.Exit" /> calls inside
///     <see cref="ConsoleCommandArgumentCollection.Parse(Type)" /> for a missing-required-argument or a
///     help request are never reached - those two paths cannot be exercised from an in-process test at
///     all since they would terminate the test host, so they're deliberately left untested here.
/// </summary>
public class ConsoleCommandArgumentCollectionTests
{
    /// <summary>Verifies that a plain token (no leading dash) is parsed as a positional argument.</summary>
    [Fact]
    public void Constructor_ParsesPositionalArgument()
    {
        ConsoleCommandArgumentCollection collection = new(["value1"]);

        Assert.Single(collection);
        Assert.Equal(0, collection[0].Position);
        Assert.Equal("value1", collection[0].Value);
        Assert.Equal(ConsoleCommandArgumentType.Positional, collection[0].Type);
        Assert.Equal(char.MinValue, collection[0].Character);
        Assert.Null(collection[0].Name);
    }

    /// <summary>Verifies that a long-form named argument with a space-separated value is parsed correctly.</summary>
    [Fact]
    public void Constructor_ParsesLongNamedArgumentWithSeparateValue()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "John"]);

        Assert.Single(collection);
        Assert.Equal("name", collection[0].Name);
        Assert.Equal("John", collection[0].Value);
        Assert.Equal(char.MinValue, collection[0].Character);
        Assert.Equal(ConsoleCommandArgumentType.Named, collection[0].Type);
    }

    /// <summary>Verifies that a short-form named argument with a space-separated value is parsed correctly.</summary>
    [Fact]
    public void Constructor_ParsesShortNamedArgumentWithSeparateValue()
    {
        ConsoleCommandArgumentCollection collection = new(["-a", "30"]);

        Assert.Single(collection);
        Assert.Equal('a', collection[0].Character);
        Assert.Equal("30", collection[0].Value);
        Assert.Null(collection[0].Name);
    }

    /// <summary>
    ///     Verifies that a long-form argument with an embedded "=value" is split into the correct name
    ///     and value - this is the exact shape of a real bug found and fixed in this project: the
    ///     original "inclusive value" detection condition could never be true (it compared the raw,
    ///     dash-prefixed token against a prefix built from values that were already derived from that
    ///     same token in a way that could never match), so "--name=value" fell through to being
    ///     misparsed as a boolean switch instead.
    /// </summary>
    [Fact]
    public void Constructor_ParsesLongNamedArgumentWithEmbeddedValue()
    {
        ConsoleCommandArgumentCollection collection = new(["--name=John"]);

        Assert.Single(collection);
        Assert.Equal("name", collection[0].Name);
        Assert.Equal("John", collection[0].Value);
        Assert.Equal(char.MinValue, collection[0].Character);
    }

    /// <summary>
    ///     Verifies that a short-form argument with an embedded "=value" is split into the correct
    ///     character and value (the short-form counterpart of the embedded-value bug above).
    /// </summary>
    [Fact]
    public void Constructor_ParsesShortNamedArgumentWithEmbeddedValue()
    {
        ConsoleCommandArgumentCollection collection = new(["-a=5"]);

        Assert.Single(collection);
        Assert.Equal('a', collection[0].Character);
        Assert.Equal("5", collection[0].Value);
        Assert.Null(collection[0].Name);
    }

    /// <summary>
    ///     Verifies that a trailing named argument with no following value is treated as a boolean switch.
    /// </summary>
    [Fact]
    public void Constructor_TreatsTrailingNamedArgumentAsSwitch()
    {
        ConsoleCommandArgumentCollection collection = new(["--verbose"]);

        Assert.Single(collection);
        Assert.Equal("verbose", collection[0].Name);
        Assert.Equal("true", collection[0].Value);
    }

    /// <summary>
    ///     Verifies that a named argument immediately followed by another named argument (rather than a
    ///     value) is treated as a boolean switch.
    /// </summary>
    [Fact]
    public void Constructor_TreatsNamedArgumentFollowedByAnotherFlagAsSwitch()
    {
        ConsoleCommandArgumentCollection collection = new(["--verbose", "--name", "John"]);

        Assert.Equal(2, collection.Count);
        Assert.Equal("verbose", collection[0].Name);
        Assert.Equal("true", collection[0].Value);
        Assert.Equal("name", collection[1].Name);
        Assert.Equal("John", collection[1].Value);
    }

    /// <summary>Verifies that a mix of positional and named arguments preserves each argument's index.</summary>
    [Fact]
    public void Constructor_PreservesIndexAcrossMixedArguments()
    {
        ConsoleCommandArgumentCollection collection = new(["source.txt", "--verbose"]);

        Assert.Equal(0, collection[0].Index);
        Assert.Equal(1, collection[1].Index);
    }

    /// <summary>Verifies the implicit conversion from a string array.</summary>
    [Fact]
    public void ImplicitOperator_FromStringArray_ParsesArguments()
    {
        ConsoleCommandArgumentCollection collection = new[] { "--name", "John" };

        Assert.Single(collection);
        Assert.Equal("name", collection[0].Name);
    }

    /// <summary>Verifies the implicit conversion from a <see cref="List{String}" />.</summary>
    [Fact]
    public void ImplicitOperator_FromStringList_ParsesArguments()
    {
        ConsoleCommandArgumentCollection collection = new List<string> { "--name", "John" };

        Assert.Single(collection);
        Assert.Equal("name", collection[0].Name);
    }

    /// <summary>Verifies the implicit conversion from a single space-delimited string.</summary>
    [Fact]
    public void ImplicitOperator_FromString_SplitsAndParsesArguments()
    {
        ConsoleCommandArgumentCollection collection = "--name John";

        Assert.Single(collection);
        Assert.Equal("name", collection[0].Name);
        Assert.Equal("John", collection[0].Value);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandArgumentCollection.FromCli(IEnumerable{string})" /> parses correctly.</summary>
    [Fact]
    public void FromCli_WithEnumerable_ParsesArguments()
    {
        ConsoleCommandArgumentCollection collection = ConsoleCommandArgumentCollection.FromCli([
            "source.txt",
        ]);

        Assert.Single(collection);
        Assert.Equal(ConsoleCommandArgumentType.Positional, collection[0].Type);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandArgumentCollection.FromCli(string)" /> parses correctly.</summary>
    [Fact]
    public void FromCli_WithString_ParsesArguments()
    {
        ConsoleCommandArgumentCollection collection = ConsoleCommandArgumentCollection.FromCli(
            "source.txt"
        );

        Assert.Single(collection);
        Assert.Equal(ConsoleCommandArgumentType.Positional, collection[0].Type);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleCommandArgumentCollection.ParseNamedArguments" /> matches CLI
    ///     tokens to their corresponding declared property via either the short or long name. Note that
    ///     the base <see cref="ConsoleCommandInputModel" /> class itself declares two named arguments
    ///     (Format and Silent), so the returned list always includes those in addition to whatever the
    ///     concrete model itself declares.
    /// </summary>
    [Fact]
    public void ParseNamedArguments_MatchesShortAndLongNames()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "John", "-a", "30"]);

        List<ParsedConsoleArgument> parsed = collection.ParseNamedArguments(
            typeof(SimpleNamedArgsModel)
        );

        ParsedConsoleArgument nameArgument = parsed.Single(p =>
            p.Property.Name == nameof(SimpleNamedArgsModel.Name)
        );
        Assert.True(nameArgument.Found);
        ParsedConsoleArgument ageArgument = parsed.Single(p =>
            p.Property.Name == nameof(SimpleNamedArgsModel.Age)
        );
        Assert.True(ageArgument.Found);
        ParsedConsoleArgument verboseArgument = parsed.Single(p =>
            p.Property.Name == nameof(SimpleNamedArgsModel.Verbose)
        );
        Assert.False(verboseArgument.Found);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleCommandArgumentCollection.ParsePositionalArguments" /> matches
    ///     CLI tokens to their corresponding declared property via position index.
    /// </summary>
    [Fact]
    public void ParsePositionalArguments_MatchesByPosition()
    {
        ConsoleCommandArgumentCollection collection = new(["in.txt", "out.txt"]);

        List<ParsedConsoleArgument> parsed = collection.ParsePositionalArguments(
            typeof(PositionalArgsModel)
        );

        Assert.Equal(2, parsed.Count);
        Assert.All(parsed, p => Assert.True(p.Found));
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleCommandArgumentCollection.ParseNestedVerbs" /> matches a CLI
    ///     token whose value equals the nested verb's own name.
    /// </summary>
    [Fact]
    public void ParseNestedVerbs_MatchesByVerbName()
    {
        ConsoleCommandArgumentCollection collection = new(["child"]);

        List<ParsedConsoleArgument> parsed = collection.ParseNestedVerbs(
            typeof(ParentWithNestedVerbModel)
        );

        Assert.Single(parsed);
        Assert.True(parsed[0].Found);
    }

    /// <summary>
    ///     Verifies a full round-trip: parsing a model with both named and positional arguments, all
    ///     required arguments satisfied, produces a correctly populated instance.
    /// </summary>
    [Fact]
    public void Parse_WithNamedArguments_PopulatesInstance()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "John", "-a", "30"]);

        object result = collection.Parse(typeof(SimpleNamedArgsModel));

        SimpleNamedArgsModel model = Assert.IsType<SimpleNamedArgsModel>(result);
        Assert.Equal("John", model.Name);
        Assert.Equal(30, model.Age);
    }

    /// <summary>Verifies that positional arguments populate an instance by position.</summary>
    [Fact]
    public void Parse_WithPositionalArguments_PopulatesInstance()
    {
        ConsoleCommandArgumentCollection collection = new(["in.txt", "out.txt"]);

        object result = collection.Parse(typeof(PositionalArgsModel));

        PositionalArgsModel model = Assert.IsType<PositionalArgsModel>(result);
        Assert.Equal("in.txt", model.Source);
        Assert.Equal("out.txt", model.Destination);
    }

    /// <summary>
    ///     Verifies that a positional argument's default value is applied when only the required
    ///     leading positional argument is supplied.
    /// </summary>
    [Fact]
    public void Parse_WithMissingOptionalPositionalArgument_AppliesDefault()
    {
        ConsoleCommandArgumentCollection collection = new(["in.txt"]);

        object result = collection.Parse(typeof(PositionalArgsModel));

        PositionalArgsModel model = Assert.IsType<PositionalArgsModel>(result);
        Assert.Equal("in.txt", model.Source);
    }

    /// <summary>Verifies that a matching verb as the first argument is consumed and the model parses successfully.</summary>
    [Fact]
    public void Parse_WithMatchingVerb_Succeeds()
    {
        ConsoleCommandArgumentCollection collection = new(["add", "--name", "Widget"]);

        object result = collection.Parse(typeof(VerbModel));

        VerbModel model = Assert.IsType<VerbModel>(result);
        Assert.Equal("Widget", model.Name);
    }

    /// <summary>Verifies that a mismatched verb as the first argument throws.</summary>
    [Fact]
    public void Parse_WithMismatchedVerb_Throws()
    {
        ConsoleCommandArgumentCollection collection = new(["remove", "--name", "Widget"]);

        Assert.Throws<InvalidOperationException>(() => collection.Parse(typeof(VerbModel)));
    }

    /// <summary>
    ///     Verifies that a default verb (<see cref="Ninlil.Common.Console.Attribute.ConsoleVerbAttribute.Default" />)
    ///     does not require its own name to appear as the first CLI argument.
    /// </summary>
    [Fact]
    public void Parse_WithDefaultVerb_DoesNotRequireVerbNameAsFirstArgument()
    {
        ConsoleCommandArgumentCollection collection = new(["--target", "server1"]);

        object result = collection.Parse(typeof(DefaultVerbModel));

        DefaultVerbModel model = Assert.IsType<DefaultVerbModel>(result);
        Assert.Equal("server1", model.Target);
    }

    /// <summary>Verifies that a nested verb property is populated with a parsed instance of its own type.</summary>
    [Fact]
    public void Parse_WithNestedVerb_PopulatesNestedInstance()
    {
        ConsoleCommandArgumentCollection collection = new(["child", "--value", "hello"]);

        object result = collection.Parse(typeof(ParentWithNestedVerbModel));

        ParentWithNestedVerbModel model = Assert.IsType<ParentWithNestedVerbModel>(result);
        Assert.NotNull(model.Child);
        Assert.Equal("hello", model.Child.Value);
    }

    /// <summary>Verifies that CLI tokens not matching any declared argument are captured by the unknown-arguments property.</summary>
    [Fact]
    public void Parse_WithUnknownArguments_CapturesRemaining()
    {
        ConsoleCommandArgumentCollection collection = new(["--known", "value", "extra1", "extra2"]);

        object result = collection.Parse(typeof(UnknownArgsModel));

        UnknownArgsModel model = Assert.IsType<UnknownArgsModel>(result);
        Assert.Equal("value", model.Known);
        Assert.NotNull(model.Remaining);
        Assert.Contains(model.Remaining, a => a.Value == "extra1");
        Assert.Contains(model.Remaining, a => a.Value == "extra2");
    }

    /// <summary>
    ///     Verifies that parsing zero arguments against a model with no required arguments and no verb
    ///     returns an instance populated with its declared defaults.
    /// </summary>
    [Fact]
    public void Parse_WithZeroArgumentsAndNoRequiredArguments_AppliesDefaults()
    {
        ConsoleCommandArgumentCollection collection = new(Array.Empty<string>());

        object result = collection.Parse(typeof(AllOptionalModel));

        AllOptionalModel model = Assert.IsType<AllOptionalModel>(result);
        Assert.Equal("default-label", model.Label);
    }

    /// <summary>Verifies the generic <see cref="ConsoleCommandArgumentCollection.Parse{TInputModel}" /> overload.</summary>
    [Fact]
    public void Parse_Generic_ReturnsStronglyTypedInstance()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "John"]);

        IConsoleCommandInputModel result = collection.Parse<SimpleNamedArgsModel>();

        SimpleNamedArgsModel model = Assert.IsType<SimpleNamedArgsModel>(result);
        Assert.Equal("John", model.Name);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandArgumentCollection.Parse(Type)" /> throws for a type that isn't a valid input model.</summary>
    [Fact]
    public void Parse_WithInvalidType_Throws()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "John"]);

        Assert.Throws<InvalidOperationException>(() => collection.Parse(typeof(string)));
    }

    /// <summary>Verifies that <see cref="ConsoleCommandArgumentCollection.TryParse(Type, out object)" /> returns true and a model on success.</summary>
    [Fact]
    public void TryParse_WithValidArguments_ReturnsTrue()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "John"]);

        bool success = collection.TryParse(typeof(SimpleNamedArgsModel), out object model);

        Assert.True(success);
        Assert.IsType<SimpleNamedArgsModel>(model);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandArgumentCollection.TryParse(Type, out object)" /> returns false and a null model on an invalid type.</summary>
    [Fact]
    public void TryParse_WithInvalidType_ReturnsFalse()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "John"]);

        bool success = collection.TryParse(typeof(string), out object model);

        Assert.False(success);
        Assert.Null(model);
    }

    /// <summary>Verifies the generic <see cref="ConsoleCommandArgumentCollection.TryParse{TInputModel}" /> overload on success.</summary>
    [Fact]
    public void TryParseGeneric_WithValidArguments_ReturnsTrueAndModel()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "John"]);

        bool success = collection.TryParse(out SimpleNamedArgsModel model);

        Assert.True(success);
        Assert.Equal("John", model.Name);
    }

    /// <summary>
    ///     Verifies that a single CLI token carrying a brace-delimited dictionary literal
    ///     ("{key:value,...}") is parsed into a genuinely typed <see cref="Dictionary{TKey,TValue}" />,
    ///     exercising the collection-typed value-parsing path that depends on the corrected
    ///     type-inspection/conversion helpers in the sibling Extension project.
    /// </summary>
    [Fact]
    public void Parse_WithDictionaryTypedNamedArgument_PopulatesDictionary()
    {
        ConsoleCommandArgumentCollection collection = new(["--counts", "{a:1,b:2}"]);

        object result = collection.Parse(typeof(TypedArgsModel));

        TypedArgsModel model = Assert.IsType<TypedArgsModel>(result);
        Assert.NotNull(model.Counts);
        Assert.Equal(1, model.Counts["a"]);
        Assert.Equal(2, model.Counts["b"]);
    }

    /// <summary>
    ///     Verifies that a single CLI token carrying a parenthesized tuple literal ("(a,b)") is parsed
    ///     into a genuinely typed <see cref="Tuple{T1,T2}" />.
    /// </summary>
    [Fact]
    public void Parse_WithTupleTypedNamedArgument_PopulatesTuple()
    {
        ConsoleCommandArgumentCollection collection = new(["--pair", "(key,42)"]);

        object result = collection.Parse(typeof(TypedArgsModel));

        TypedArgsModel model = Assert.IsType<TypedArgsModel>(result);
        Assert.NotNull(model.Pair);
        Assert.Equal("key", model.Pair.Item1);
        Assert.Equal(42, model.Pair.Item2);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandArgumentCollection.ToString" /> produces a space-joined representation of its valid arguments.</summary>
    [Fact]
    public void ToString_JoinsValidArgumentsWithSpaces()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "John"]);

        string result = collection.ToString();

        Assert.Equal("--name John", result);
    }
}

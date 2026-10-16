using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     These tests cover <see cref="ConsoleCommandArgumentCollection" />: tokenizing raw CLI
///     arguments (positional, named, switch and embedded-value forms), the implicit conversions and
///     factory methods, the <c>Parse*Arguments</c> matchers, and GetParseResult/Parse/TryParse
///     against the models in <c>TestSupport</c> and the test-private models below. The parser never
///     prints or exits, so help requests, missing and unconvertible arguments, verb-only command
///     lines and throwing setters are asserted on the returned <see cref="ConsoleParseResult" /> or
///     the thrown <see cref="ConsoleParseException" />, in process.
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
    ///     Verifies that a long-form argument with an embedded "=value" is split into its name and
    ///     value, rather than being read as a boolean switch named "name=John".
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
    ///     character and value (the short-form counterpart of the test above).
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
    ///     tokens to their declared property by either the short or the long name. The base
    ///     <see cref="ConsoleCommandInputModel" /> declares two named arguments (Format and Silent), so
    ///     the returned list always includes those as well as the model's own.
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
    ///     token whose value equals the nested verb's name.
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
    ///     Verifies that parsing long and short named arguments, with every required argument given,
    ///     produces a populated instance.
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

    /// <summary>
    ///     Verifies that a mismatched verb as the first argument throws an exception that callers
    ///     catching <see cref="InvalidOperationException" /> still see.
    /// </summary>
    [Fact]
    public void Parse_WithMismatchedVerb_Throws()
    {
        ConsoleCommandArgumentCollection collection = new(["remove", "--name", "Widget"]);

        Assert.ThrowsAny<InvalidOperationException>(() => collection.Parse(typeof(VerbModel)));
    }

    /// <summary>
    ///     Verifies that a default verb (<see cref="Ninlil.Common.Console.Attribute.ConsoleVerbAttribute.Default" />)
    ///     does not require its name to appear as the first CLI argument.
    /// </summary>
    [Fact]
    public void Parse_WithDefaultVerb_DoesNotRequireVerbNameAsFirstArgument()
    {
        ConsoleCommandArgumentCollection collection = new(["--target", "server1"]);

        object result = collection.Parse(typeof(DefaultVerbModel));

        DefaultVerbModel model = Assert.IsType<DefaultVerbModel>(result);
        Assert.Equal("server1", model.Target);
    }

    /// <summary>Verifies that a nested verb property is populated with a parsed instance of its type.</summary>
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
    ///     ("{key:value,...}") is parsed into a typed <see cref="Dictionary{TKey,TValue}" />, through
    ///     the type-inspection and conversion helpers in Ninlil.Common.Extension.
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
    ///     into a typed <see cref="Tuple{T1,T2}" />.
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

    /// <summary>
    ///     This model has a non-default verb and only an optional switch, so the verb alone is a
    ///     complete command line.
    /// </summary>
    [ConsoleVerb("list", "Lists the items.")]
    public sealed class ListVerbModel : ConsoleCommandInputModel
    {
        /// <summary>The optional switch that shows every column.</summary>
        [NamedConsoleArgument('l', "long", "Whether to show every column.", false, false)]
        public bool Long { get; set; }
    }

    /// <summary>
    ///     This model's named property setter always throws, to check that an exception while
    ///     filling the model is reported as an invalid result. The input-model tests use it too.
    /// </summary>
    public sealed class ThrowingSetterModel : ConsoleCommandInputModel
    {
        /// <summary>The optional value whose setter rejects every assignment.</summary>
        [NamedConsoleArgument("value", "A value the setter rejects.", false)]
        public string Value
        {
            get => null;
            set => throw new ArgumentException($"The value '{value}' is rejected.");
        }
    }

    /// <summary>Verifies that a help switch is reported as a help request with the model's help text.</summary>
    [Fact]
    public void GetParseResult_WithHelpSwitch_ReportsHelpRequested()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection(["--help"]).GetParseResult(
            typeof(SimpleNamedArgsModel)
        );

        Assert.Equal(ConsoleParseOutcomeEnum.HelpRequested, result.Outcome);
        Assert.Contains("--name", result.HelpText);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Model);
    }

    /// <summary>Verifies that Parse reports a help switch by throwing, carrying the help result.</summary>
    [Fact]
    public void Parse_WithHelpSwitch_ThrowsWithHelpRequested()
    {
        ConsoleParseException exception = Assert.Throws<ConsoleParseException>(() =>
            new ConsoleCommandArgumentCollection(["--help"]).Parse(typeof(SimpleNamedArgsModel))
        );

        Assert.Equal(ConsoleParseOutcomeEnum.HelpRequested, exception.Result.Outcome);
    }

    /// <summary>Verifies that a help switch right after the verb is reported as a help request.</summary>
    [Fact]
    public void GetParseResult_WithHelpSwitchAfterVerb_ReportsHelpRequested()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "add",
            "--help",
        ]).GetParseResult(typeof(VerbModel));

        Assert.Equal(ConsoleParseOutcomeEnum.HelpRequested, result.Outcome);
    }

    /// <summary>Verifies that Parse reports a help switch after the verb by throwing.</summary>
    [Fact]
    public void Parse_WithHelpSwitchAfterVerb_ThrowsWithHelpRequested()
    {
        ConsoleParseException exception = Assert.Throws<ConsoleParseException>(() =>
            new ConsoleCommandArgumentCollection(["add", "--help"]).Parse(typeof(VerbModel))
        );

        Assert.Equal(ConsoleParseOutcomeEnum.HelpRequested, exception.Result.Outcome);
    }

    /// <summary>Verifies that no arguments for a model with a required argument is invalid and names it.</summary>
    [Fact]
    public void GetParseResult_WithNoArgumentsAndARequiredArgument_ReportsInvalid()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection(
            Array.Empty<string>()
        ).GetParseResult(typeof(SimpleNamedArgsModel));

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, result.Outcome);
        Assert.Contains("--name", Assert.Single(result.Errors));
        Assert.Equal(1, result.ExitCode);
    }

    /// <summary>Verifies that Parse reports no arguments for a model with a required argument as a parse exception.</summary>
    [Fact]
    public void Parse_WithNoArgumentsAndARequiredArgument_ThrowsWithInvalid()
    {
        ConsoleParseException exception = Assert.Throws<ConsoleParseException>(() =>
            new ConsoleCommandArgumentCollection(Array.Empty<string>()).Parse(
                typeof(SimpleNamedArgsModel)
            )
        );

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, exception.Result.Outcome);
    }

    /// <summary>Verifies that a missing required argument is invalid, with the error leading the help text.</summary>
    [Fact]
    public void GetParseResult_WithMissingRequiredArgument_ReportsInvalid()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "--age",
            "3",
        ]).GetParseResult(typeof(SimpleNamedArgsModel));

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, result.Outcome);
        string error = Assert.Single(result.Errors);
        Assert.Contains("--name", error);
        Assert.StartsWith(error, result.HelpText);
        Assert.Null(result.Exception);
    }

    /// <summary>Verifies that Parse reports a missing required argument as a parse exception.</summary>
    [Fact]
    public void Parse_WithMissingRequiredArgument_ThrowsWithInvalid()
    {
        ConsoleParseException exception = Assert.Throws<ConsoleParseException>(() =>
            new ConsoleCommandArgumentCollection(["--age", "3"]).Parse(typeof(SimpleNamedArgsModel))
        );

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, exception.Result.Outcome);
        Assert.Contains("--name", exception.Message);
    }

    /// <summary>Verifies that a verb alone parses a model whose arguments are all optional.</summary>
    [Fact]
    public void GetParseResult_WithVerbOnlyAndOptionalArguments_ReportsParsedWithDefaults()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection(["list"]).GetParseResult(
            typeof(ListVerbModel)
        );

        Assert.Equal(ConsoleParseOutcomeEnum.Parsed, result.Outcome);
        Assert.False(Assert.IsType<ListVerbModel>(result.Model).Long);
    }

    /// <summary>Verifies that TryParse succeeds for a verb alone when every argument is optional.</summary>
    [Fact]
    public void TryParse_WithVerbOnlyAndOptionalArguments_ReturnsTrue()
    {
        bool success = new ConsoleCommandArgumentCollection(["list"]).TryParse(
            out ListVerbModel model
        );

        Assert.True(success);
        Assert.NotNull(model);
    }

    /// <summary>Verifies that a verb alone for a model with a required argument is invalid and names it.</summary>
    [Fact]
    public void GetParseResult_WithVerbOnlyAndARequiredArgument_ReportsInvalid()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection(["add"]).GetParseResult(
            typeof(VerbModel)
        );

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, result.Outcome);
        Assert.Contains("--name", Assert.Single(result.Errors));
    }

    /// <summary>Verifies that Parse reports a verb alone with a missing required argument as a parse exception.</summary>
    [Fact]
    public void Parse_WithVerbOnlyAndARequiredArgument_ThrowsWithInvalid()
    {
        ConsoleParseException exception = Assert.Throws<ConsoleParseException>(() =>
            new ConsoleCommandArgumentCollection(["add"]).Parse(typeof(VerbModel))
        );

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, exception.Result.Outcome);
    }

    /// <summary>Verifies that another model's verb is reported as not matched.</summary>
    [Fact]
    public void GetParseResult_WithMismatchedVerb_ReportsNotMatched()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "remove",
            "--name",
            "x",
        ]).GetParseResult(typeof(VerbModel));

        Assert.Equal(ConsoleParseOutcomeEnum.NotMatched, result.Outcome);
        Assert.Equal(1, result.ExitCode);
    }

    /// <summary>Verifies that a setter that throws is an invalid result carrying the setter's exception.</summary>
    [Fact]
    public void GetParseResult_WithThrowingSetter_ReportsInvalidWithException()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "--value",
            "x",
        ]).GetParseResult(typeof(ThrowingSetterModel));

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, result.Outcome);
        Assert.IsType<ArgumentException>(result.Exception);
        Assert.Contains("rejected", Assert.Single(result.Errors));
    }

    /// <summary>Verifies that TryParse is false when a setter throws.</summary>
    [Fact]
    public void TryParse_WithThrowingSetter_ReturnsFalse()
    {
        bool success = new ConsoleCommandArgumentCollection(["--value", "x"]).TryParse(
            typeof(ThrowingSetterModel),
            out object model
        );

        Assert.False(success);
        Assert.Null(model);
    }

    /// <summary>Verifies that a value that does not convert to an integer is invalid, naming the argument and the value.</summary>
    [Fact]
    public void GetParseResult_WithUnconvertibleInteger_ReportsInvalid()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "--name",
            "x",
            "--age",
            "abc",
        ]).GetParseResult(typeof(SimpleNamedArgsModel));

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, result.Outcome);
        string error = Assert.Single(result.Errors);
        Assert.Contains("--age", error);
        Assert.Contains("'abc'", error);
    }

    /// <summary>Verifies that TryParse is false for a value that does not convert to an integer.</summary>
    [Fact]
    public void TryParse_WithUnconvertibleInteger_ReturnsFalse()
    {
        bool success = new ConsoleCommandArgumentCollection([
            "--name",
            "x",
            "--age",
            "abc",
        ]).TryParse(out SimpleNamedArgsModel model);

        Assert.False(success);
        Assert.Null(model);
    }

    /// <summary>Verifies that a value that is not a boolean word is invalid for a switch.</summary>
    [Fact]
    public void GetParseResult_WithUnconvertibleBoolean_ReportsInvalid()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "--name",
            "x",
            "--verbose",
            "maybe",
        ]).GetParseResult(typeof(SimpleNamedArgsModel));

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, result.Outcome);
        Assert.Contains("maybe", Assert.Single(result.Errors));
    }

    /// <summary>Verifies that TryParse is false for a value that is not a boolean word.</summary>
    [Fact]
    public void TryParse_WithUnconvertibleBoolean_ReturnsFalse()
    {
        bool success = new ConsoleCommandArgumentCollection([
            "--name",
            "x",
            "--verbose",
            "maybe",
        ]).TryParse(out SimpleNamedArgsModel _);

        Assert.False(success);
    }

    /// <summary>Verifies that parsing leaves the collection unchanged, so a second parse also succeeds.</summary>
    [Fact]
    public void GetParseResult_CalledTwiceOnOneCollection_ParsesBothTimes()
    {
        ConsoleCommandArgumentCollection collection = new(["add", "--name", "w"]);

        ConsoleParseResult first = collection.GetParseResult(typeof(VerbModel));
        ConsoleParseResult second = collection.GetParseResult(typeof(VerbModel));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal("add", collection[0].Value);
    }

    /// <summary>Verifies that Parse on the same collection twice returns a model both times.</summary>
    [Fact]
    public void Parse_CalledTwiceOnOneCollection_ReturnsAModelBothTimes()
    {
        ConsoleCommandArgumentCollection collection = new(["add", "--name", "w"]);

        object first = collection.Parse(typeof(VerbModel));
        object second = collection.Parse(typeof(VerbModel));

        Assert.Equal("w", Assert.IsType<VerbModel>(first).Name);
        Assert.Equal("w", Assert.IsType<VerbModel>(second).Name);
    }

    /// <summary>Verifies that a lone dash, the usual stdin marker, is a positional value.</summary>
    [Fact]
    public void Constructor_WithLoneDash_ParsesPositionalArgument()
    {
        ConsoleCommandArgumentCollection collection = new(["-"]);

        ConsoleCommandArgument argument = Assert.Single(collection);
        Assert.Equal(ConsoleCommandArgumentType.Positional, argument.Type);
        Assert.Equal("-", argument.Value);
    }

    /// <summary>Verifies that a lone dash after a named argument is that argument's value.</summary>
    [Fact]
    public void Constructor_WithLoneDashAfterNamedArgument_UsesItAsTheValue()
    {
        ConsoleCommandArgumentCollection collection = new(["--input", "-"]);

        ConsoleCommandArgument argument = Assert.Single(collection);
        Assert.Equal("input", argument.Name);
        Assert.Equal("-", argument.Value);
    }

    /// <summary>Verifies that a help switch inside a nested verb reports the nested verb's help.</summary>
    [Fact]
    public void GetParseResult_WithHelpSwitchInsideNestedVerb_ReportsNestedHelp()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "child",
            "--help",
        ]).GetParseResult(typeof(ParentWithNestedVerbModel));

        Assert.Equal(ConsoleParseOutcomeEnum.HelpRequested, result.Outcome);
        Assert.Contains("--value", result.HelpText);
    }

    /// <summary>Verifies that Parse reports a help switch inside a nested verb by throwing.</summary>
    [Fact]
    public void Parse_WithHelpSwitchInsideNestedVerb_ThrowsWithHelpRequested()
    {
        ConsoleParseException exception = Assert.Throws<ConsoleParseException>(() =>
            new ConsoleCommandArgumentCollection(["child", "--help"]).Parse(
                typeof(ParentWithNestedVerbModel)
            )
        );

        Assert.Equal(ConsoleParseOutcomeEnum.HelpRequested, exception.Result.Outcome);
    }

    /// <summary>Verifies that a nested verb with valid arguments parses into the nested model.</summary>
    [Fact]
    public void GetParseResult_WithNestedVerbArguments_ReportsParsed()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "child",
            "--value",
            "hello",
        ]).GetParseResult(typeof(ParentWithNestedVerbModel));

        Assert.True(result.Succeeded);
        Assert.Equal("hello", Assert.IsType<ParentWithNestedVerbModel>(result.Model).Child.Value);
    }

    /// <summary>
    ///     This model has a non-default verb and two positional arguments, to check that positions
    ///     count from the first token after the verb.
    /// </summary>
    [ConsoleVerb("copy", "Copies a file.")]
    public sealed class CopyVerbModel : ConsoleCommandInputModel
    {
        /// <summary>The required source file.</summary>
        [PositionalConsoleArgument(0, "The source file.")]
        public string Source { get; set; }

        /// <summary>The optional destination file.</summary>
        [PositionalConsoleArgument(1, "The destination file.", false, "out.txt")]
        public string Destination { get; set; }
    }

    /// <summary>
    ///     This model has an optional named argument and a nested verb, to check that a named
    ///     argument's value is never taken for the verb.
    /// </summary>
    public sealed class NamedAndNestedVerbModel : ConsoleCommandInputModel
    {
        /// <summary>The optional name.</summary>
        [NamedConsoleArgument("name", "The name.", false)]
        public string Name { get; set; }

        /// <summary>The nested child command.</summary>
        [NestedConsoleVerb("child", "The nested child command.")]
        public ChildInputModel Child { get; set; }
    }

    /// <summary>Verifies that a positional token's position ignores named tokens and their values.</summary>
    [Fact]
    public void Constructor_WithNamedArgumentBefore_NumbersPositionalTokensAmongThemselves()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "x", "in.txt", "out.txt"]);

        ConsoleCommandArgument first = collection[1];
        ConsoleCommandArgument second = collection[2];
        Assert.Equal(0, first.Position);
        Assert.Equal(2, first.Index);
        Assert.Equal(1, second.Position);
        Assert.Equal(3, second.Index);
        Assert.Equal(-1, collection[0].Position);
    }

    /// <summary>Verifies that a lone double dash is not a token and everything after it is positional.</summary>
    [Fact]
    public void Constructor_WithDoubleDash_EndsOptionParsing()
    {
        ConsoleCommandArgumentCollection collection = new(["--", "--not-an-option", "-x", "--"]);

        Assert.Equal(3, collection.Count);
        Assert.All(collection, a => Assert.Equal(ConsoleCommandArgumentType.Positional, a.Type));
        Assert.Equal(["--not-an-option", "-x", "--"], collection.Select(a => a.Value));
        Assert.Equal([0, 1, 2], collection.Select(a => a.Position));
        Assert.Equal([1, 2, 3], collection.Select(a => a.Index));
    }

    /// <summary>Verifies that named arguments before the double dash are still named.</summary>
    [Fact]
    public void Constructor_WithNamedArgumentBeforeDoubleDash_KeepsItNamed()
    {
        ConsoleCommandArgumentCollection collection = new(["--verbose", "--", "file"]);

        Assert.Equal(2, collection.Count);
        Assert.Equal(ConsoleCommandArgumentType.Named, collection[0].Type);
        Assert.Equal("true", collection[0].Value);
        Assert.Equal(0, collection[1].Position);
    }

    /// <summary>Verifies that positional arguments are found after a named argument and its value.</summary>
    [Fact]
    public void GetParseResult_WithNamedArgumentBeforePositionals_MatchesPositionals()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "--output-format",
            "json",
            "in.txt",
            "out.txt",
        ]).GetParseResult(typeof(PositionalArgsModel));

        Assert.True(result.Succeeded);
        PositionalArgsModel model = Assert.IsType<PositionalArgsModel>(result.Model);
        Assert.Equal("in.txt", model.Source);
        Assert.Equal("out.txt", model.Destination);
    }

    /// <summary>Verifies that the twin through Parse matches positionals after a named argument.</summary>
    [Fact]
    public void Parse_WithNamedArgumentBeforePositionals_PopulatesPositionals()
    {
        PositionalArgsModel model = Assert.IsType<PositionalArgsModel>(
            new ConsoleCommandArgumentCollection(["--output-format", "json", "in.txt"]).Parse(
                typeof(PositionalArgsModel)
            )
        );

        Assert.Equal("in.txt", model.Source);
        Assert.Equal("out.txt", model.Destination);
    }

    /// <summary>Verifies that positions count from the first token after the verb.</summary>
    [Fact]
    public void GetParseResult_WithVerbAndPositionals_CountsFromTheVerb()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "copy",
            "a",
            "b",
        ]).GetParseResult(typeof(CopyVerbModel));

        Assert.True(result.Succeeded);
        CopyVerbModel model = Assert.IsType<CopyVerbModel>(result.Model);
        Assert.Equal("a", model.Source);
        Assert.Equal("b", model.Destination);
    }

    /// <summary>Verifies that the verb alone leaves the required positional argument missing.</summary>
    [Fact]
    public void GetParseResult_WithVerbAndNoPositionals_IsInvalid()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection(["copy"]).GetParseResult(
            typeof(CopyVerbModel)
        );

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, result.Outcome);
    }

    /// <summary>Verifies that a token after the double dash fills a positional argument.</summary>
    [Fact]
    public void GetParseResult_WithOptionLikeValueAfterDoubleDash_FillsThePositionalArgument()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "--",
            "--not-an-option",
        ]).GetParseResult(typeof(PositionalArgsModel));

        Assert.True(result.Succeeded);
        Assert.Equal("--not-an-option", Assert.IsType<PositionalArgsModel>(result.Model).Source);
    }

    /// <summary>Verifies that without the double dash the same token is a named argument.</summary>
    [Fact]
    public void GetParseResult_WithOptionLikeValueAndNoDoubleDash_MissesThePositionalArgument()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "--not-an-option",
        ]).GetParseResult(typeof(PositionalArgsModel));

        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, result.Outcome);
    }

    /// <summary>Verifies that a named argument's value equal to the verb name is not the nested verb.</summary>
    [Fact]
    public void GetParseResult_WithNamedValueEqualToNestedVerb_DoesNotSelectTheNestedVerb()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "--name",
            "child",
            "--value",
            "x",
        ]).GetParseResult(typeof(NamedAndNestedVerbModel));

        Assert.True(result.Succeeded);
        NamedAndNestedVerbModel model = Assert.IsType<NamedAndNestedVerbModel>(result.Model);
        Assert.Equal("child", model.Name);
        // The verb was not given, so the child is a plain new instance: its attribute defaults are
        // not applied and nothing from "--value x" reaches it.
        Assert.NotNull(model.Child);
        Assert.Null(model.Child.Value);
    }

    /// <summary>Verifies that ParseNestedVerbs ignores a named argument's value.</summary>
    [Fact]
    public void ParseNestedVerbs_WithNamedValueEqualToVerb_IsNotFound()
    {
        ConsoleCommandArgumentCollection collection = new(["--name", "child"]);

        ParsedConsoleArgument parsed = Assert.Single(
            collection.ParseNestedVerbs(typeof(ParentWithNestedVerbModel))
        );

        Assert.False(parsed.Found);
    }

    /// <summary>Verifies that a switch after the nested verb belongs to the nested model only.</summary>
    [Fact]
    public void GetParseResult_WithSwitchAfterNestedVerb_GivesItToTheNestedModel()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "child",
            "--value",
            "x",
            "-s",
        ]).GetParseResult(typeof(ParentWithNestedVerbModel));

        Assert.True(result.Succeeded);
        ParentWithNestedVerbModel model = Assert.IsType<ParentWithNestedVerbModel>(result.Model);
        Assert.False(model.Silent);
        Assert.True(model.Child.Silent);
        Assert.Equal("x", model.Child.Value);
    }

    /// <summary>Verifies that a named argument before the nested verb stays with the parent.</summary>
    [Fact]
    public void GetParseResult_WithNamedArgumentBeforeNestedVerb_KeepsItOnTheParent()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "--name",
            "n",
            "child",
            "--value",
            "x",
        ]).GetParseResult(typeof(NamedAndNestedVerbModel));

        Assert.True(result.Succeeded);
        NamedAndNestedVerbModel model = Assert.IsType<NamedAndNestedVerbModel>(result.Model);
        Assert.Equal("n", model.Name);
        Assert.Equal("x", model.Child.Value);
    }

    /// <summary>Verifies that tokens after the nested verb do not reach the parent's unknown arguments.</summary>
    [Fact]
    public void GetParseResult_WithTokensAfterNestedVerb_LeavesParentUnknownArgumentsEmpty()
    {
        ConsoleParseResult result = new ConsoleCommandArgumentCollection([
            "--extra",
            "1",
            "child",
            "--value",
            "x",
        ]).GetParseResult(typeof(UnknownAndNestedVerbModel));

        Assert.True(result.Succeeded);
        UnknownAndNestedVerbModel model = Assert.IsType<UnknownAndNestedVerbModel>(result.Model);
        ConsoleCommandArgument unknown = Assert.Single(model.Unknown);
        Assert.Equal("extra", unknown.Name);
        Assert.Equal("x", model.Child.Value);
    }

    /// <summary>
    ///     This model has an unknown-arguments capture and a nested verb, to check which tokens the
    ///     capture sees when a nested verb splits the command line.
    /// </summary>
    public sealed class UnknownAndNestedVerbModel : ConsoleCommandInputModel
    {
        /// <summary>The tokens no argument claimed.</summary>
        [UnknownConsoleArguments]
        public ConsoleCommandArgumentCollection Unknown { get; set; }

        /// <summary>The nested child command.</summary>
        [NestedConsoleVerb("child", "The nested child command.")]
        public ChildInputModel Child { get; set; }
    }
}

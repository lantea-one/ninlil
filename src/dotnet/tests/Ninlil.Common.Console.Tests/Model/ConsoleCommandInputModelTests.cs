using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     These tests cover <see cref="ConsoleCommandInputModel" /> using the models in
///     <c>TestSupport</c>: attribute discovery (named, positional, nested verb, verb), the
///     argument-name and predicate helpers, <see cref="ConsoleCommandInputModel.SetDefaults" />,
///     and the static Parse/TryParse/help-text entry points. As in
///     <see cref="ConsoleCommandArgumentCollectionTests" />, every parse supplies all required
///     arguments so the parser never reaches its process-exit paths.
/// </summary>
public class ConsoleCommandInputModelTests
{
    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetNamedArguments" /> discovers every named-argument-decorated property, including those inherited from the base class.</summary>
    [Fact]
    public void GetNamedArguments_DiscoversDeclaredAndInheritedProperties()
    {
        var named = ConsoleCommandInputModel
            .GetNamedArguments(typeof(SimpleNamedArgsModel))
            .ToList();

        Assert.Contains(named, n => n.Item1.Name == nameof(SimpleNamedArgsModel.Name));
        Assert.Contains(named, n => n.Item1.Name == nameof(SimpleNamedArgsModel.Age));
        Assert.Contains(named, n => n.Item1.Name == nameof(SimpleNamedArgsModel.Verbose));

        // Inherited from ConsoleCommandInputModel itself.
        Assert.Contains(named, n => n.Item1.Name == nameof(ConsoleCommandInputModel.Format));
        Assert.Contains(named, n => n.Item1.Name == nameof(ConsoleCommandInputModel.Silent));
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetPositionalArguments" /> discovers positional properties ordered by their declared position.</summary>
    [Fact]
    public void GetPositionalArguments_OrdersByPosition()
    {
        var positional = ConsoleCommandInputModel
            .GetPositionalArguments(typeof(PositionalArgsModel))
            .ToList();

        Assert.Equal(2, positional.Count);
        Assert.Equal(0, positional[0].Item2.Position);
        Assert.Equal(1, positional[1].Item2.Position);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetNestedVerbs" /> discovers nested-verb-decorated properties.</summary>
    [Fact]
    public void GetNestedVerbs_DiscoversDecoratedProperty()
    {
        var nested = ConsoleCommandInputModel
            .GetNestedVerbs(typeof(ParentWithNestedVerbModel))
            .ToList();

        Assert.Single(nested);
        Assert.Equal(nameof(ParentWithNestedVerbModel.Child), nested[0].Item1.Name);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetVerb" /> returns the verb attribute declared on the type.</summary>
    [Fact]
    public void GetVerb_ReturnsDeclaredVerbAttribute()
    {
        ConsoleVerbAttribute verb = ConsoleCommandInputModel.GetVerb(typeof(VerbModel));

        Assert.NotNull(verb);
        Assert.Equal("add", verb.Name);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetVerb" /> returns null for a type without a verb attribute.</summary>
    [Fact]
    public void GetVerb_ReturnsNullForModelWithoutVerb()
    {
        Assert.Null(ConsoleCommandInputModel.GetVerb(typeof(SimpleNamedArgsModel)));
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.HasArguments" /> is true for a model with named/positional arguments.</summary>
    [Fact]
    public void HasArguments_TrueForModelWithArguments()
    {
        SimpleNamedArgsModel model = new();

        Assert.True(model.HasArguments());
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.HasRequiredArguments" /> reflects whether at least one required argument is declared.</summary>
    [Fact]
    public void HasRequiredArguments_TrueWhenARequiredArgumentExists()
    {
        SimpleNamedArgsModel model = new();

        Assert.True(model.HasRequiredArguments());
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.HasRequiredArguments" /> is false when every argument is optional.</summary>
    [Fact]
    public void HasRequiredArguments_FalseWhenEveryArgumentIsOptional()
    {
        AllOptionalModel model = new();

        Assert.False(model.HasRequiredArguments());
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.HasVerb" /> reflects the presence of a verb attribute.</summary>
    [Fact]
    public void HasVerb_TrueForVerbDecoratedModel()
    {
        VerbModel model = new();

        Assert.True(model.HasVerb());
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.HasVerb" /> is false for a model without a verb.</summary>
    [Fact]
    public void HasVerb_FalseForModelWithoutVerb()
    {
        SimpleNamedArgsModel model = new();

        Assert.False(model.HasVerb());
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.IsDefault" /> reflects the verb attribute's Default flag.</summary>
    [Fact]
    public void IsDefault_TrueForDefaultVerbModel()
    {
        DefaultVerbModel model = new();

        Assert.True(model.IsDefault());
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.IsDefault" /> is false for a non-default verb model.</summary>
    [Fact]
    public void IsDefault_FalseForNonDefaultVerbModel()
    {
        VerbModel model = new();

        Assert.False(model.IsDefault());
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.IsNamedArgument" /> correctly identifies a named-argument property.</summary>
    [Fact]
    public void IsNamedArgument_TrueForNamedProperty()
    {
        SimpleNamedArgsModel model = new();

        Assert.True(model.IsNamedArgument(nameof(SimpleNamedArgsModel.Name)));
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.IsPositionalArgument" /> correctly identifies a positional-argument property.</summary>
    [Fact]
    public void IsPositionalArgument_TrueForPositionalProperty()
    {
        PositionalArgsModel model = new();

        Assert.True(model.IsPositionalArgument(nameof(PositionalArgsModel.Source)));
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.IsNestedVerb" /> correctly identifies a nested-verb property.</summary>
    [Fact]
    public void IsNestedVerb_TrueForNestedVerbProperty()
    {
        ParentWithNestedVerbModel model = new();

        Assert.True(model.IsNestedVerb(nameof(ParentWithNestedVerbModel.Child)));
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetArgumentNameOrPosition" /> formats a short/long named argument.</summary>
    [Fact]
    public void GetArgumentNameOrPosition_FormatsNamedArgumentWithShortName()
    {
        SimpleNamedArgsModel model = new();

        string result = model.GetArgumentNameOrPosition(nameof(SimpleNamedArgsModel.Age));

        Assert.Equal("-a|--age", result);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetArgumentNameOrPosition" /> formats a positional argument.</summary>
    [Fact]
    public void GetArgumentNameOrPosition_FormatsPositionalArgument()
    {
        PositionalArgsModel model = new();

        string result = model.GetArgumentNameOrPosition(nameof(PositionalArgsModel.Source));

        Assert.Equal("<0>", result);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetArgumentNameOrPosition" /> throws for an unknown property name.</summary>
    [Fact]
    public void GetArgumentNameOrPosition_ThrowsForUnknownProperty()
    {
        SimpleNamedArgsModel model = new();

        Assert.Throws<ArgumentException>(() => model.GetArgumentNameOrPosition("NotAProperty"));
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.SetDefaults" /> applies declared defaults to untouched properties.</summary>
    [Fact]
    public void SetDefaults_AppliesDeclaredDefaultsToUntouchedProperties()
    {
        SimpleNamedArgsModel model = new();

        model.SetDefaults();

        Assert.Equal(0, model.Age);
        Assert.False(model.Verbose);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.SetDefaults" /> does not overwrite an already-set property value.</summary>
    [Fact]
    public void SetDefaults_DoesNotOverwriteExistingValue()
    {
        SimpleNamedArgsModel model = new() { Age = 99 };

        model.SetDefaults();

        Assert.Equal(99, model.Age);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.SetDefaults" /> instantiates a nested verb property recursively.</summary>
    [Fact]
    public void SetDefaults_InstantiatesNestedVerbProperty()
    {
        ParentWithNestedVerbModel model = new();

        model.SetDefaults();

        Assert.NotNull(model.Child);
        Assert.Equal("default-child-value", model.Child.Value);
    }

    /// <summary>Verifies that the default, unoverridden <see cref="ConsoleCommandInputModel.ValidateAsync" /> completes successfully.</summary>
    [Fact]
    public async Task ValidateAsync_DefaultImplementation_CompletesSuccessfully()
    {
        SimpleNamedArgsModel model = new();

        await model.ValidateAsync();
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.Parse{TInputModel}(string[])" /> parses CLI arguments into a strongly-typed model.</summary>
    [Fact]
    public void Parse_Generic_ParsesArgumentsIntoStronglyTypedModel()
    {
        SimpleNamedArgsModel model = ConsoleCommandInputModel.Parse<SimpleNamedArgsModel>(
            "--name",
            "John"
        );

        Assert.Equal("John", model.Name);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.TryParse{TInputModel}(out TInputModel, string[])" /> returns true and a populated model on success.</summary>
    [Fact]
    public void TryParse_Generic_ReturnsTrueOnSuccess()
    {
        bool success = ConsoleCommandInputModel.TryParse(
            out SimpleNamedArgsModel model,
            "--name",
            "John"
        );

        Assert.True(success);
        Assert.Equal("John", model.Name);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.TryParse{TInputModel}(out TInputModel, string[])" /> returns false and a null model when the underlying parse throws.</summary>
    [Fact]
    public void TryParse_Generic_ReturnsFalseWhenVerbMismatches()
    {
        bool success = ConsoleCommandInputModel.TryParse(
            out VerbModel model,
            "remove",
            "--name",
            "Widget"
        );

        Assert.False(success);
        Assert.Null(model);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetHelpText{TInputModel}(string, ConsoleHelpTextCollectionModel)" /> produces non-empty help text mentioning the model's arguments.</summary>
    [Fact]
    public void GetHelpText_Generic_ProducesNonEmptyHelpText()
    {
        string helpText = ConsoleCommandInputModel.GetHelpText<SimpleNamedArgsModel>();

        Assert.False(string.IsNullOrWhiteSpace(helpText));
        Assert.Contains("name", helpText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetHelpText(Type, string, ConsoleHelpTextCollectionModel)" /> prepends a supplied preface.</summary>
    [Fact]
    public void GetHelpText_WithPreface_PrependsPreface()
    {
        string helpText = ConsoleCommandInputModel.GetHelpText(
            typeof(SimpleNamedArgsModel),
            "Missing required argument"
        );

        Assert.StartsWith("Missing required argument", helpText);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetHelpText(Type, string, ConsoleHelpTextCollectionModel)" /> throws for a null type.</summary>
    [Fact]
    public void GetHelpText_WithNullType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConsoleCommandInputModel.GetHelpText(null));
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetHelpText(Type, string, ConsoleHelpTextCollectionModel)" /> throws for a type that isn't a valid input model.</summary>
    [Fact]
    public void GetHelpText_WithInvalidType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ConsoleCommandInputModel.GetHelpText(typeof(string))
        );
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetHelpTextCollection(Type, ConsoleHelpTextCollectionModel)" /> includes an entry for every declared argument.</summary>
    [Fact]
    public void GetHelpTextCollection_IncludesEveryDeclaredArgument()
    {
        ConsoleHelpTextCollectionModel collection = ConsoleCommandInputModel.GetHelpTextCollection(
            typeof(SimpleNamedArgsModel)
        );

        Assert.Contains(collection, e => e.Name == "name");
        Assert.Contains(collection, e => e.Name == "age");
        Assert.Contains(collection, e => e.Name == "verbose");
    }

    /// <summary>Verifies that <see cref="ConsoleCommandInputModel.GetHelpTextCollection(Type, ConsoleHelpTextCollectionModel)" /> includes the verb entry for a verb-decorated model.</summary>
    [Fact]
    public void GetHelpTextCollection_IncludesVerbEntry()
    {
        ConsoleHelpTextCollectionModel collection = ConsoleCommandInputModel.GetHelpTextCollection(
            typeof(VerbModel)
        );

        Assert.Contains(collection, e => e.Verb && e.Name == "add");
    }
}

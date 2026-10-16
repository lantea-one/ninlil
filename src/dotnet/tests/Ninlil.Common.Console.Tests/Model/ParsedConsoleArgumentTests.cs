using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     These tests cover <see cref="ParsedConsoleArgument" /> built by hand from attributes and
///     tokens: <see cref="ParsedConsoleArgument.Found" /> and <see cref="ParsedConsoleArgument.Required" />,
///     typed-value conversion for named (including list-typed) and positional arguments, defaults,
///     the readable <see cref="ParsedConsoleArgument.AttributeString" /> and
///     <see cref="ParsedConsoleArgument.PropertyString" />, and <see cref="ParsedConsoleArgument.SetValue" />.
/// </summary>
public class ParsedConsoleArgumentTests
{
    /// <summary>Verifies that <see cref="ParsedConsoleArgument.Found" /> reflects whether any CLI arguments matched.</summary>
    [Fact]
    public void Found_TrueWhenArgumentsPresent()
    {
        ParsedConsoleArgument parsed = new()
        {
            Arguments = [new ConsoleCommandArgument { Value = "x" }],
        };

        Assert.True(parsed.Found);
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.Found" /> is false for an empty arguments collection.</summary>
    [Fact]
    public void Found_FalseWhenArgumentsEmpty()
    {
        ParsedConsoleArgument parsed = new() { Arguments = [] };

        Assert.False(parsed.Found);
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.Required" /> reflects a required named-argument attribute.</summary>
    [Fact]
    public void Required_TrueForRequiredNamedAttribute()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("name"),
        };

        Assert.True(parsed.Required);
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.Required" /> is false for an optional named-argument attribute.</summary>
    [Fact]
    public void Required_FalseForOptionalNamedAttribute()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("name", required: false),
        };

        Assert.False(parsed.Required);
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.GetAttribute{TAttribute}" /> casts to the requested attribute type.</summary>
    [Fact]
    public void GetAttribute_CastsToRequestedType()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("name"),
        };

        NamedConsoleArgumentAttribute attribute =
            parsed.GetAttribute<NamedConsoleArgumentAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("name", attribute.Name);
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.GetAttribute{TAttribute}" /> returns null for a mismatched attribute type.</summary>
    [Fact]
    public void GetAttribute_ReturnsNullForMismatchedType()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("name"),
        };

        Assert.Null(parsed.GetAttribute<PositionalConsoleArgumentAttribute>());
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.AttributeString" /> formats a short/long named attribute.</summary>
    [Fact]
    public void AttributeString_FormatsNamedAttributeWithShortName()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute('a', "age"),
        };

        Assert.Equal("NamedConsoleArgumentAttribute[-a, --age]", parsed.AttributeString);
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.AttributeString" /> is null when no attribute is set.</summary>
    [Fact]
    public void AttributeString_NullWhenNoAttribute()
    {
        ParsedConsoleArgument parsed = new();

        Assert.Null(parsed.AttributeString);
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.PropertyString" /> formats as "DeclaringType.PropertyName".</summary>
    [Fact]
    public void PropertyString_FormatsDeclaringTypeAndPropertyName()
    {
        ParsedConsoleArgument parsed = new()
        {
            Property = typeof(SimpleNamedArgsModel).GetProperty(nameof(SimpleNamedArgsModel.Name)),
        };

        Assert.Equal(
            $"{nameof(SimpleNamedArgsModel)}.{nameof(SimpleNamedArgsModel.Name)}",
            parsed.PropertyString
        );
    }

    /// <summary>Verifies that a single-valued named argument is converted to the declared property's type.</summary>
    [Fact]
    public void GetParsedValue_ForSingleValuedNamedArgument_ConvertsToPropertyType()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("age", required: false, defaultValue: 0),
            Property = typeof(SimpleNamedArgsModel).GetProperty(nameof(SimpleNamedArgsModel.Age)),
            Arguments = [new ConsoleCommandArgument { Value = "30" }],
        };

        object result = parsed.GetParsedValue();

        Assert.Equal(30, result);
    }

    /// <summary>Verifies that an unfound, optional named argument resolves to its declared default value.</summary>
    [Fact]
    public void GetParsedValue_ForUnfoundOptionalNamedArgument_ReturnsDefault()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("age", required: false, defaultValue: 42),
            Property = typeof(SimpleNamedArgsModel).GetProperty(nameof(SimpleNamedArgsModel.Age)),
            Arguments = [],
        };

        object result = parsed.GetParsedValue();

        Assert.Equal(42, result);
    }

    /// <summary>Verifies that a list-typed named argument with multiple matched CLI tokens is parsed as a list.</summary>
    [Fact]
    public void GetParsedValue_ForListTypedArgument_ParsesCommaJoinedValues()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("tags", required: false),
            Property = typeof(TypedArgsModel).GetProperty(nameof(TypedArgsModel.Tags)),
            Arguments =
            [
                new ConsoleCommandArgument { Value = "alpha" },
                new ConsoleCommandArgument { Value = "beta" },
            ],
        };

        object result = parsed.GetParsedValue();

        List<string> tags = Assert.IsType<List<string>>(result);
        Assert.Equal(["alpha", "beta"], tags);
    }

    /// <summary>Verifies that a positional argument's value is converted to the declared property's type.</summary>
    [Fact]
    public void GetParsedValue_ForPositionalArgument_ConvertsToPropertyType()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new PositionalConsoleArgumentAttribute(0),
            Property = typeof(PositionalArgsModel).GetProperty(nameof(PositionalArgsModel.Source)),
            Arguments = [new ConsoleCommandArgument { Value = "in.txt" }],
        };

        object result = parsed.GetParsedValue();

        Assert.Equal("in.txt", result);
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.GetParsedValue" /> returns null for an attribute type it doesn't recognize.</summary>
    [Fact]
    public void GetParsedValue_ForUnrecognizedAttributeType_ReturnsNull()
    {
        ParsedConsoleArgument parsed = new() { Attribute = null };

        Assert.Null(parsed.GetParsedValue());
    }

    /// <summary>Verifies the generic <see cref="ParsedConsoleArgument.GetParsedValue{TValue}" /> overload casts the result.</summary>
    [Fact]
    public void GetParsedValueGeneric_CastsResult()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("age", required: false, defaultValue: 0),
            Property = typeof(SimpleNamedArgsModel).GetProperty(nameof(SimpleNamedArgsModel.Age)),
            Arguments = [new ConsoleCommandArgument { Value = "30" }],
        };

        int result = parsed.GetParsedValue<int>();

        Assert.Equal(30, result);
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.SetValue" /> sets the resolved value onto the input model instance.</summary>
    [Fact]
    public void SetValue_SetsResolvedValueOnInputModel()
    {
        SimpleNamedArgsModel model = new();
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("name"),
            Property = typeof(SimpleNamedArgsModel).GetProperty(nameof(SimpleNamedArgsModel.Name)),
            Arguments = [new ConsoleCommandArgument { Value = "John" }],
        };

        parsed.SetValue(model);

        Assert.Equal("John", model.Name);
    }

    /// <summary>Verifies that <see cref="ParsedConsoleArgument.SetValue" /> with a null property is a safe no-op.</summary>
    [Fact]
    public void SetValue_WithNullProperty_IsNoOp()
    {
        SimpleNamedArgsModel model = new();
        ParsedConsoleArgument parsed = new()
        {
            Attribute = null,
            Property = null,
            Arguments = [],
        };

        parsed.SetValue(model);

        Assert.Null(model.Name);
    }

    /// <summary>Verifies that an unfound optional argument without an attribute default falls back to the property initializer of the model.</summary>
    [Fact]
    public void GetParsedValue_ForUnfoundArgumentWithoutAttributeDefault_ReturnsPropertyInitializer()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("port", "The port.", false),
            Property = typeof(ConsoleCommandArgumentCollectionTests.InitializerModel).GetProperty(
                nameof(ConsoleCommandArgumentCollectionTests.InitializerModel.Port)
            ),
            Arguments = [],
        };

        Assert.Equal(53, parsed.GetParsedValue());
    }

    /// <summary>Verifies that an unfound positional argument without an attribute default also falls back to the property initializer.</summary>
    [Fact]
    public void GetParsedValue_ForUnfoundPositionalWithoutAttributeDefault_ReturnsPropertyInitializer()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new PositionalConsoleArgumentAttribute(0, "The mode.", false),
            Property = typeof(ConsoleCommandArgumentCollectionTests.InitializerModel).GetProperty(
                nameof(ConsoleCommandArgumentCollectionTests.InitializerModel.Mode)
            ),
            Arguments = [],
        };

        Assert.Equal("fast", parsed.GetParsedValue());
    }

    /// <summary>Verifies that a string attribute default on a non-string property is converted to the property type.</summary>
    [Fact]
    public void GetParsedValue_ForStringDefaultOnIntProperty_ConvertsTheDefault()
    {
        ParsedConsoleArgument parsed = new()
        {
            Attribute = new NamedConsoleArgumentAttribute("age", "The age.", false, "7"),
            Property = typeof(SimpleNamedArgsModel).GetProperty(nameof(SimpleNamedArgsModel.Age)),
            Arguments = [],
        };

        Assert.Equal(7, parsed.GetParsedValue());
    }
}

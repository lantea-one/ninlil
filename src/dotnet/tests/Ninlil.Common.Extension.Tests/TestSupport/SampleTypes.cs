using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Ninlil.Common.Extension.Converter;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.TestSupport;

/// <summary>
///     A plain class with a public parameterless constructor, writable properties and a read-only
///     property. Tests across this project use it for the reflection-based extension methods
///     (<c>Copy</c>, <c>SetProperty</c>, <c>TrimProperty</c>, <c>CreateInstance</c>).
/// </summary>
public class SampleModel
{
    /// <summary>
    ///     Gets or sets the sample's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Gets or sets the sample's number.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    ///     Gets a fixed, read-only value with no setter, used to exercise the
    ///     "property is not writable" failure paths.
    /// </summary>
    public string FixedValue { get; } = "fixed";
}

/// <summary>
///     A plain class with only a parameterized constructor (no public parameterless
///     constructor), used to exercise the "cannot create a default instance" fallback paths.
/// </summary>
public class SampleModelWithRequiredConstructorArgument
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SampleModelWithRequiredConstructorArgument" /> class.
    /// </summary>
    /// <param name="seed">The seed value to store on <see cref="Seed" />.</param>
    public SampleModelWithRequiredConstructorArgument(int seed) => Seed = seed;

    /// <summary>
    ///     Gets the seed value passed to the constructor.
    /// </summary>
    public int Seed { get; }
}

/// <summary>
///     An abstract class, used to exercise the "type cannot be instantiated" failure paths.
/// </summary>
public abstract class SampleAbstractModel
{
    /// <summary>
    ///     Gets or sets a sample value.
    /// </summary>
    public string Value { get; set; }
}

/// <summary>
///     A class exposing both static and instance methods with varying arity, used to exercise
///     <c>CallTypeExtension.CallMethod</c>.
/// </summary>
public class SampleCallableModel
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SampleCallableModel" /> class.
    /// </summary>
    public SampleCallableModel() { }

    /// <summary>
    ///     Returns a fixed static greeting, used to test static-method dispatch.
    /// </summary>
    /// <returns>A fixed greeting string.</returns>
    public static string StaticGreet() => "static-hello";

    /// <summary>
    ///     Returns a fixed instance greeting, used to test instance-method dispatch.
    /// </summary>
    /// <returns>A fixed greeting string.</returns>
    public string InstanceGreet() => "instance-hello";

    /// <summary>
    ///     Echoes the given value back unchanged.
    /// </summary>
    /// <param name="value">The value to echo.</param>
    /// <returns>The <paramref name="value" /> unchanged.</returns>
    public string Echo(string value) => value;

    /// <summary>
    ///     Concatenates two values.
    /// </summary>
    /// <param name="first">The first value.</param>
    /// <param name="second">The second value.</param>
    /// <returns>The concatenation of <paramref name="first" /> and <paramref name="second" />.</returns>
    public string Concatenate(string first, string second) => first + second;

    /// <summary>
    ///     Joins five values with a comma.
    /// </summary>
    public string JoinFive(string a, string b, string c, string d, string e) =>
        string.Join(",", a, b, c, d, e);

    /// <summary>
    ///     Returns the given value unchanged, used to test the generic method-calling overloads.
    /// </summary>
    /// <param name="value">The value to return.</param>
    /// <typeparam name="TValue">The type of <paramref name="value" />.</typeparam>
    /// <returns>The <paramref name="value" /> unchanged.</returns>
    public TValue Identity<TValue>(TValue value) => value;
}

/// <summary>
///     A sample enum whose second member carries an <see cref="EnumMemberAttribute" /> value, so a
///     result of <see cref="Second" /> proves a real match and a result of <see cref="First" />
///     exposes a fall back to the zero member. The enum tests across this project share it.
/// </summary>
public enum SampleSecondValueEnum
{
    /// <summary>
    ///     The zero member, which an unmatched input must never produce.
    /// </summary>
    First,

    /// <summary>
    ///     The member matched by its name or by its description <c>second-value</c>.
    /// </summary>
    [EnumMember(Value = "second-value")]
    Second,
}

/// <summary>
///     A sample enum backed by <see cref="ushort" /> with unassigned codes between members, like
///     the DNS wire enums (record types, classes, response codes). Number strings in range must
///     read back as the number, defined or not.
/// </summary>
public enum SampleWireEnum : ushort
{
    /// <summary>
    ///     A defined code.
    /// </summary>
    Alpha = 1,

    /// <summary>
    ///     A second defined code.
    /// </summary>
    Beta = 28,
}

/// <summary>
///     A sample <see cref="FlagsAttribute" /> enum, used to check that a comma list is accepted
///     only for flags enums.
/// </summary>
[Flags]
public enum SampleFlagsEnum
{
    /// <summary>
    ///     No flag.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The first flag.
    /// </summary>
    One = 1,

    /// <summary>
    ///     The second flag.
    /// </summary>
    Two = 2,
}

/// <summary>
///     A sample enum that carries both the description converter attribute and an
///     <see cref="EnumMemberAttribute" /> value on a member that is not the first. The converter
///     precedence guard test serializes it through the library's JSON serializer.
/// </summary>
[JsonConverter(typeof(EnumDescriptionJsonConverter<SampleConverterAttributeEnum>))]
public enum SampleConverterAttributeEnum
{
    /// <summary>
    ///     The zero member.
    /// </summary>
    First,

    /// <summary>
    ///     A member whose description differs from its name.
    /// </summary>
    [EnumMember(Value = "second-value")]
    Second,
}

/// <summary>
///     A sample holder with a non-nullable and a nullable enum property, used to read JSON
///     <c>null</c> into each through <see cref="EnumDescriptionJsonConverter{TEnum}" />.
/// </summary>
public class SampleEnumHolder
{
    /// <summary>
    ///     Gets or sets the non-nullable enum.
    /// </summary>
    public SampleSecondValueEnum Value { get; set; }

    /// <summary>
    ///     Gets or sets the nullable enum.
    /// </summary>
    public SampleSecondValueEnum? Optional { get; set; }
}

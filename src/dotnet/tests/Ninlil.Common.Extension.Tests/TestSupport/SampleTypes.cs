// Define our namespace.
namespace Ninlil.Common.Extension.Tests.TestSupport;

/// <summary>
///     A plain class with a public parameterless constructor and both a writable and a
///     read-only property, used across this test project to exercise reflection-based
///     extension methods (<c>Copy</c>, <c>SetProperty</c>, <c>TrimProperty</c>, <c>CreateInstance</c>).
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

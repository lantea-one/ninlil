// Define our namespace
namespace Ninlil.Common.Serializer.Attribute;

/// <summary>
///     This class is a strongly-typed wrapper around the built-in
///     <see cref="System.Text.Json.Serialization.JsonConverterAttribute" /> that lets a converter
///     be specified as a generic type argument (e.g. <c>[JsonConverter&lt;MyConverter&gt;]</c>)
///     instead of via <see langword="typeof" />, avoiding the need to repeat the converter's name.
/// </summary>
/// <typeparam name="TConverter">The type of the <see cref="System.Text.Json.Serialization.JsonConverter" /> to apply.</typeparam>
[AttributeUsage(
    AttributeTargets.Class
        | AttributeTargets.Interface
        | AttributeTargets.Struct
        | AttributeTargets.Enum
        | AttributeTargets.Property
        | AttributeTargets.Field
)]
public class JsonConverterAttribute<TConverter>
    : System.Text.Json.Serialization.JsonConverterAttribute
    where TConverter : System.Text.Json.Serialization.JsonConverter
{
    /// <summary>
    ///     Initializes a new instance of
    ///     <see cref="JsonConverterAttribute{JsonConverter}" />
    ///     with the specified converter type.
    /// </summary>
    public JsonConverterAttribute()
        : base(typeof(TConverter)) { }
}

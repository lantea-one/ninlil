// Define our namespace.
namespace Ninlil.Common.Serializer.Attribute;

/// <summary>
///     This attribute marks the property that is each item's unique key in a list handled by
///     <see cref="Converter.ListMapJsonConverter{TItem}" />. The converter reads and writes that
///     property explicitly, so the key round-trips even when the property is hidden from ordinary
///     serialization with <c>[JsonIgnore]</c>. The converter only looks at properties, although
///     the attribute also allows fields.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ListMapKeyAttribute : System.Attribute;

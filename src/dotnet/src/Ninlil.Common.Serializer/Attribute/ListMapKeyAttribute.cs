// Define our namespace for the serializer attributes.
namespace Ninlil.Common.Serializer.Attribute;

/// <summary>
///     This attribute is used to mark a field or property as the key for a list map during serialization and
///     deserialization. It indicates that the marked member should be treated as the unique identifier for
///     items in a collection when converting between a list and a dictionary representation. This is particularly
///     useful when working with JSON or XML serialization, where you want to maintain a mapping between keys and
///     values in a collection. The attribute can be applied to fields or properties of a class, and it helps the
///     serializer understand how to handle the collection during the serialization process. When applied, the
///     serializer will use the marked member as the key for each item in the collection, allowing for efficient
///     lookups and ensuring that the serialized output maintains the correct structure and relationships between items.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ListMapKeyAttribute : System.Attribute;

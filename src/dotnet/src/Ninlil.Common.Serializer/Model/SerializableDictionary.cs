using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This class is a <see cref="Dictionary{TKey, TValue}" /> that <see cref="System.Xml.Serialization.XmlSerializer" />
///     can handle, which a plain dictionary cannot. Each entry becomes an element named after the
///     key's JSON form (XML-encoded with <see cref="XmlConvert.EncodeName" />), holding the value's
///     XML. A key that is blank or cannot be serialized is written as <c>UnknownKey_n</c> and is
///     dropped when read back.
/// </summary>
/// <typeparam name="TKey"> The type of the keys in the dictionary.</typeparam>
/// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
public class SerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IXmlSerializable
{
    /// <summary>
    ///     This constructor initializes a new instance of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" /> class.
    /// </summary>
    public SerializableDictionary() { }

    /// <summary>
    ///     This constructor initializes a new instance of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" />
    ///     class from an existing <paramref name="dictionary" />.
    /// </summary>
    /// <param name="dictionary">
    ///     The existing dictionary from which to initialize the new instance
    ///     of the <see cref="SerializableDictionary{TKey, TValue}" /> class.
    /// </param>
    public SerializableDictionary(IDictionary<TKey, TValue> dictionary)
        : base(dictionary) { }

    /// <summary>
    ///     This constructor initializes a new instance of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" />
    ///     class with a specified <paramref name="comparer" />
    ///     for key comparison.
    /// </summary>
    /// <param name="comparer">The comparer to use for key comparison.</param>
    public SerializableDictionary(IEqualityComparer<TKey> comparer)
        : base(comparer) { }

    /// <summary>
    ///     This constructor initializes a new instance of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" />
    ///     class with a specified initial <paramref name="capacity" />.
    /// </summary>
    /// <param name="capacity">The initial capacity of the dictionary.</param>
    public SerializableDictionary(int capacity)
        : base(capacity) { }

    /// <summary>
    ///     This constructor initializes a new instance of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" />
    ///     class from an existing <paramref name="dictionary" />
    ///     and a specified <paramref name="comparer" />.
    /// </summary>
    /// <param name="dictionary">
    ///     The existing dictionary from which to initialize the new instance
    ///     of the <see cref="SerializableDictionary{TKey, TValue}" /> class.
    /// </param>
    /// <param name="comparer">The comparer to use for key comparison.</param>
    public SerializableDictionary(
        IDictionary<TKey, TValue> dictionary,
        IEqualityComparer<TKey> comparer
    )
        : base(dictionary, comparer) { }

    /// <summary>
    ///     This constructor initializes a new instance of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" />
    ///     class with a specified initial <paramref name="capacity" />
    ///     and a specified <paramref name="comparer" /> for key comparison.
    /// </summary>
    /// <param name="capacity">The initial capacity of the dictionary.</param>
    /// <param name="comparer">The comparer to use for key comparison.</param>
    public SerializableDictionary(int capacity, IEqualityComparer<TKey> comparer)
        : base(capacity, comparer) { }

    /// <summary>
    ///     This constructor initializes a new instance of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" />
    ///     class from an existing <paramref name="collection" />
    ///     of key-value pairs.
    /// </summary>
    /// <param name="collection">The collection of key-value pairs to initialize the dictionary with.</param>
    public SerializableDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        : base(collection) { }

    /// <summary>
    ///     This constructor initializes a new instance of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" />
    ///     class from an existing <paramref name="collection" />
    ///     of key-value pairs and a specified <paramref name="comparer" />
    ///     for key comparison.
    /// </summary>
    /// <param name="collection">The collection of key-value pairs to initialize the dictionary with.</param>
    /// <param name="comparer">The comparer to use for key comparison.</param>
    public SerializableDictionary(
        IEnumerable<KeyValuePair<TKey, TValue>> collection,
        IEqualityComparer<TKey> comparer
    )
        : base(collection, comparer) { }

    /// <summary>
    ///     This method returns <see langword="null" />, as <see cref="IXmlSerializable" /> recommends.
    /// </summary>
    /// <returns>Always <see langword="null" />.</returns>
    public XmlSchema GetSchema() => null;

    /// <summary>
    ///     This method deserializes the XML representation of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" />
    ///     class from the specified <paramref name="reader" />.
    /// </summary>
    /// <param name="reader">
    ///     The <see cref="XmlReader" /> instance from which the XML representation of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" /> class will be deserialized.
    /// </param>
    public void ReadXml(XmlReader reader)
    {
        // Move to the content of the XML document.
        reader.MoveToContent();

        // If the current element is empty, consume it and return.
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        // Read the start element of the dictionary.
        reader.ReadStartElement();

        // Iterate through the XML elements and deserialize them into key-value pairs.
        while (reader.NodeType != XmlNodeType.EndElement)
        {
            // If the current node is an element, process it as a key-value pair.
            if (reader.NodeType == XmlNodeType.Element)
            {
                // Get the encoded name of the current XML element.
                var encodedKey = reader.Name;

                // Decode the element name back to the key's JSON form or the fallback key.
                var key = XmlConvert.DecodeName(encodedKey);

                // Deserialize the key from JSON; a generated UnknownKey_n becomes default and is skipped.
                TKey thisKey = key.ToLower().StartsWith("unknownkey_")
                    ? default
                    : JsonSerializer.Deserialize<TKey>(key);

                // Deserialize the element's inner XML as the value. ReadInnerXml() consumes the element
                // and advances the reader to the next node.
                TValue thisValue = XmlSerializer.Deserialize<TValue>(reader.ReadInnerXml());

                // Add the key-value pair to the dictionary.
                if (thisKey is not null)
                    this[thisKey] = thisValue;
            }
            // Otherwise, advance the reader to the next node in the XML document.
            else
                reader.Read();
        }

        // Consume the end element of the dictionary.
        reader.ReadEndElement();
    }

    /// <summary>
    ///     This method serializes the current instance of the
    ///     <see cref="SerializableDictionary{TKey, TValue}" />
    ///     class to the XML <paramref name="writer" />.
    /// </summary>
    /// <param name="writer">
    ///     The <see cref="XmlWriter" /> instance to which the current instance will be serialized.
    /// </param>
    public void WriteXml(XmlWriter writer)
    {
        // Define our unknown key iterator.
        var unknownKey = 0;

        // Iterate through the instance and add the items to the XML writer as their key-value pairs.
        foreach (var pair in this)
        {
            // Use the key's JSON form, or a generated key when it is blank or cannot be serialized.
            var key =
                pair.Key?.ToString()?.Trim() is null or { Length: 0 }
                || !JsonSerializer.TrySerialize(pair.Key, out var keyJson)
                    ? $"UnknownKey_{++unknownKey}"
                    : keyJson;

            // Encode the key so it is a valid XML element name.
            key = XmlConvert.EncodeName(key);

            // Create an XML element for the key-value pair with the
            // string representation of the key as the element name.
            writer.WriteStartElement(key);

            // Serialize the value of the key-value pair to the XML writer.
            var value = pair.Value;

            // If the value is null, write an empty element.
            if (value is null)
            {
                // Write the end element for the key-value pair.
                writer.WriteEndElement();

                // We're done with this key-value pair, so continue to the next one.
                continue;
            }

            // Write the value as an XML fragment. The default writer settings emit an
            // "<?xml ...?>" declaration, which is only legal at the start of a document; nested here
            // it would make XML that ReadXml cannot parse. Clone the shared default settings (do not
            // modify them) and turn the declaration off for this value.
            XmlWriterSettings valueWriterSettings = XmlSerializer.SerializerOptions.Clone();
            valueWriterSettings.OmitXmlDeclaration = true;
            writer.WriteRaw(XmlSerializer.Serialize(value, valueWriterSettings));

            // Write the end element for the key-value pair.
            writer.WriteEndElement();
        }
    }
}

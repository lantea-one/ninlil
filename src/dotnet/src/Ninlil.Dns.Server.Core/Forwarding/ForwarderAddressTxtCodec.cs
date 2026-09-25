using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Forwarding;

/// <summary>
///     This class encodes one <c>ZoneEntity.ForwarderAddresses</c> entry (an upstream definition
///     string in the grammar <see cref="IUpstreamForwarderDefinitionParser" /> parses) as a TXT
///     record at a <c>ZoneTypeEnum.Forwarder</c> zone's apex, and decodes it again. A
///     <c>ZoneTypeEnum.SecondaryForwarder</c> zone pulls those TXT records by ordinary AXFR/IXFR
///     and rebuilds its <c>ForwarderAddresses</c> list from them.
///     <para>
///         Each string is prefixed with its zero-based position (<c>"{index}:{definition}"</c>)
///         before being split into RFC 1035 §3.3 character-strings. A TXT RRset has no guaranteed
///         order through storage or transfer, but the default failover strategy
///         (<c>ForwarderFailoverStrategyEnum.InOrder</c>) treats list order as priority, so the
///         order must survive. Decoding splits on the first colon only, so colons inside the
///         definition (IPv6 literals, ports, URL schemes) are kept.
///     </para>
/// </summary>
public static class ForwarderAddressTxtCodec
{
    /// <summary>
    ///     This constant contains the longest an RFC 1035 §3.3 character-string may be (the
    ///     internal <c>CharacterStringCodec.MaximumLength</c> in <c>Ninlil.Dns.Common</c>). A longer
    ///     string is split across several <see cref="TxtRecordData.Values" /> entries and joined
    ///     again on decode. The split counts characters, which matches octets only for ASCII.
    /// </summary>
    private const int MaxCharacterStringLength = 255;

    /// <summary>
    ///     This method encodes a single forwarder address entry as a <see cref="TxtRecordData" />.
    /// </summary>
    /// <param name="index">The entry's zero-based position in <c>ForwarderAddresses</c>.</param>
    /// <param name="forwarderAddress">The definition string.</param>
    /// <returns>The encoded TXT record data, ready to write via <see cref="TxtRecordDataCodec" />.</returns>
    public static TxtRecordData Encode(int index, string forwarderAddress)
    {
        string encoded = $"{index}:{forwarderAddress}";
        List<string> chunks = [];

        for (int offset = 0; offset < encoded.Length; offset += MaxCharacterStringLength)
        {
            int length = Math.Min(MaxCharacterStringLength, encoded.Length - offset);
            chunks.Add(encoded.Substring(offset, length));
        }

        // A TXT record needs at least one character-string, even for an empty definition.
        if (chunks.Count == 0)
            chunks.Add(string.Empty);

        return new TxtRecordData(chunks);
    }

    /// <summary>
    ///     This method decodes a <see cref="TxtRecordData" /> previously produced by
    ///     <see cref="Encode" /> back into its index and definition string.
    /// </summary>
    /// <param name="data">The TXT record data to decode.</param>
    /// <param name="index">The entry's zero-based position in <c>ForwarderAddresses</c>.</param>
    /// <param name="forwarderAddress">The decoded definition string.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="data" /> decodes to a well-formed
    ///     <c>"{index}:{definitionString}"</c> pair; <see langword="false" /> otherwise, for example
    ///     a TXT record someone else wrote at the apex.
    /// </returns>
    public static bool TryDecode(TxtRecordData data, out int index, out string forwarderAddress)
    {
        string joined = string.Concat(data?.Values ?? []);
        int firstColon = joined.IndexOf(':');

        if (firstColon < 0 || !int.TryParse(joined[..firstColon], out index))
        {
            index = 0;
            forwarderAddress = null;
            return false;
        }

        forwarderAddress = joined[(firstColon + 1)..];
        return true;
    }
}

using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Forwarding;

/// <summary>
///     This class encodes a single <c>ZoneEntity.ForwarderAddresses</c> entry (one upstream
///     forwarder definition string, in the exact same grammar <see cref="IUpstreamForwarderDefinitionParser" />
///     already parses -- a bare address, a <c>udp://</c>/<c>tcp://</c>/<c>tls://</c>/
///     <c>https://</c>/<c>quic://</c> URL, or an <c>sdns://</c> DNSCrypt stamp) as a single
///     <c>DnsResourceRecordTypeEnum.Txt</c> record at a <c>ZoneTypeEnum.Forwarder</c>
///     zone's own apex, and decodes it back again -- the wire-transferable content a
///     <c>ZoneTypeEnum.SecondaryForwarder</c> zone pulls via ordinary AXFR/IXFR and decodes back
///     into its own <c>ForwarderAddresses</c> list, rather than inventing a second serialization
///     for a string this project's own parser already round-trips.
///     <para>
///         Each definition string is prefixed with its own zero-based position in
///         <c>ForwarderAddresses</c> (<c>"{index}:{definitionString}"</c>) before being split into
///         RFC 1035 §3.3 character-strings -- a TXT RRset carries no ordering guarantee of its
///         own across an arbitrary storage engine or wire transfer, but this project's own
///         forwarder failover strategy (<c>ForwarderFailoverStrategyEnum.InOrder</c>, the default)
///         depends on <c>ForwarderAddresses</c>' own list order to mean "priority" -- so that
///         order must survive the round trip exactly, not merely "some order". The prefix is
///         always split on the *first* colon only, so a definition string that itself contains
///         colons (a bracketed IPv6 literal, an explicit port, a URL scheme) is never misparsed --
///         the index is always exactly the fixed-width digits this class itself prepended.
///     </para>
/// </summary>
public static class ForwarderAddressTxtCodec
{
    /// <summary>
    ///     This constant contains the longest a single RFC 1035 §3.3 character-string may be
    ///     (<c>CharacterStringCodec.MaximumLength</c>, itself internal to
    ///     <c>Ninlil.Dns.Common</c>) -- a definition string longer than this is split across
    ///     several <see cref="TxtRecordData.Values" /> entries, concatenated back together on
    ///     decode.
    /// </summary>
    private const int MaxCharacterStringLength = 255;

    /// <summary>
    ///     This method encodes a single forwarder address entry as a <see cref="TxtRecordData" />.
    /// </summary>
    /// <param name="index">This entry's own zero-based position in <c>ForwarderAddresses</c>.</param>
    /// <param name="forwarderAddress">The raw definition string itself.</param>
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

        // An empty definition string (never produced by this project's own write paths, but
        // defensively handled anyway) still needs at least one character-string on the wire.
        if (chunks.Count == 0)
            chunks.Add(string.Empty);

        return new TxtRecordData(chunks);
    }

    /// <summary>
    ///     This method decodes a <see cref="TxtRecordData" /> previously produced by
    ///     <see cref="Encode" /> back into its own index and definition string.
    /// </summary>
    /// <param name="data">The TXT record data to decode.</param>
    /// <param name="index">This entry's own zero-based position in <c>ForwarderAddresses</c>.</param>
    /// <param name="forwarderAddress">The decoded definition string.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="data" /> decodes to a well-formed
    ///     <c>"{index}:{definitionString}"</c> pair, <see langword="false" /> otherwise (a TXT
    ///     record an operator or another feature wrote at the same owner name by hand, which this
    ///     class must never mistake for one of its own).
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

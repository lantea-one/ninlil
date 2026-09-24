using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientSubnet;

/// <summary>
///     This class implements RFC 7871 EDNS Client Subnet (ECS) support: accepting an ECS option a
///     downstream client already supplied, and building the ECS option this server attaches to
///     its own outbound queries while recursively resolving (<c>Server.Recursive</c>) or
///     forwarding (<c>Server.Forwarding</c>) on that client's behalf. Lives in <c>Server.Core</c>
///     itself, not a separate project, mirroring <see cref="Cookies.DnsCookieService" />'s own
///     reasoning: it needs no storage of its own, and both consuming projects already depend on
///     this one. Registered unconditionally, gated at runtime by
///     <see cref="EdnsClientSubnetOptions.Enable" />.
///     <para>
///         <b>Precedence when building an outbound option</b> (highest first):
///         <see cref="EdnsClientSubnetOptions.OverrideAddress" />, when configured, always wins --
///         an operator who set it explicitly wants a single, fixed, coarse location disclosed
///         upstream regardless of what any individual client sent or which real address queried
///         this server. Otherwise, a client that already attached its own ECS option to its
///         query is trusted and forwarded through unchanged (re-encoded, but with that client's
///         own address and source prefix length, not this server's configured defaults) -- RFC
///         7871 §7.1.2 expects a resolver acting on behalf of another ECS-aware resolver to relay
///         its choice rather than override it. Only when neither applies does this service fall
///         back to synthesizing an option from the real querying client's own source address,
///         truncated to <see cref="EdnsClientSubnetOptions.SourcePrefixLengthIPv4" />/
///         <see cref="EdnsClientSubnetOptions.SourcePrefixLengthIPv6" />.
///     </para>
/// </summary>
/// <param name="options">This service's own configured enablement, prefix lengths, and override address.</param>
public sealed class EdnsClientSubnetService(IOptionsMonitor<EdnsClientSubnetOptions> options)
{
    /// <summary>
    ///     This method looks for an ECS option already attached to a downstream client's own
    ///     query.
    /// </summary>
    /// <param name="request">The downstream client's decoded query.</param>
    /// <param name="data">The decoded ECS contents, if the query carried a well-formed one.</param>
    /// <returns>A <see langword="bool" /> indicating whether a usable ECS option was found.</returns>
    public bool TryExtractFromDownstreamRequest(
        DnsMessage request,
        out EdnsClientSubnetOptionData data
    )
    {
        data = null;
        DnsEdnsOption option = request.Edns?.Options?.FirstOrDefault(candidate =>
            candidate.Code == (ushort)EdnsOptionCodeEnum.ClientSubnet
        );

        return option is not null && EdnsClientSubnetCodec.TryDecode(option, out data);
    }

    /// <summary>
    ///     This method builds the ECS option this server should attach to an outbound query
    ///     issued on a specific downstream client's behalf -- see this class's own doc comment for
    ///     the full precedence this method applies.
    /// </summary>
    /// <param name="downstreamClientAddress">The real querying client's own source address.</param>
    /// <param name="downstreamRequest">
    ///     The downstream client's own decoded query, consulted for a client-supplied ECS option
    ///     to relay through unchanged.
    /// </param>
    /// <returns>
    ///     The ECS option to attach, or <see langword="null" /> when this feature is disabled
    ///     entirely, or when the address to disclose could not be determined (a misconfigured
    ///     <see cref="EdnsClientSubnetOptions.OverrideAddress" />, most commonly).
    /// </returns>
    public DnsEdnsOption BuildOutboundOption(
        IPAddress downstreamClientAddress,
        DnsMessage downstreamRequest
    )
    {
        EdnsClientSubnetOptions currentOptions = options.CurrentValue;

        if (!currentOptions.Enable)
            return null;

        if (!string.IsNullOrWhiteSpace(currentOptions.OverrideAddress))
        {
            if (!IPAddress.TryParse(currentOptions.OverrideAddress, out IPAddress overrideAddress))
                return null;

            return EdnsClientSubnetCodec.Encode(
                overrideAddress,
                DefaultSourcePrefixLength(overrideAddress, currentOptions),
                scopePrefixLength: 0
            );
        }

        if (
            TryExtractFromDownstreamRequest(
                downstreamRequest,
                out EdnsClientSubnetOptionData clientSupplied
            )
        )
            return EdnsClientSubnetCodec.Encode(
                clientSupplied.Address,
                clientSupplied.SourcePrefixLength,
                scopePrefixLength: 0
            );

        if (downstreamClientAddress is null)
            return null;

        return EdnsClientSubnetCodec.Encode(
            downstreamClientAddress,
            DefaultSourcePrefixLength(downstreamClientAddress, currentOptions),
            scopePrefixLength: 0
        );
    }

    /// <summary>
    ///     This method builds a shallow clone of <paramref name="original" /> with
    ///     <paramref name="clientSubnetOption" /> attached to its own EDNS options (replacing any
    ///     ECS option <paramref name="original" /> already carried) -- used by
    ///     <c>ForwarderQueryResolver</c>, which forwards a client's own decoded request onward
    ///     largely as-is and must not mutate the shared <c>DnsQueryContext.Request</c> instance
    ///     every other resolver in the same dispatch also sees.
    /// </summary>
    /// <param name="original">The request to clone.</param>
    /// <param name="clientSubnetOption">The ECS option to attach.</param>
    /// <returns>The cloned, ECS-carrying request.</returns>
    public static DnsMessage AttachToRequest(DnsMessage original, DnsEdnsOption clientSubnetOption)
    {
        DnsMessage clone = new()
        {
            Id = original.Id,
            Opcode = original.Opcode,
            RecursionDesired = original.RecursionDesired,
            CheckingDisabled = original.CheckingDisabled,
            Edns = new DnsEdnsOptions(
                original.Edns?.Version ?? 0,
                original.Edns?.RequestorPayloadSize
                    ?? Resolution.DnsQueryDispatcher.DefaultUdpPayloadSizeOctets,
                original.Edns?.DnssecOk ?? false,
                [
                    .. (original.Edns?.Options ?? []).Where(option =>
                        option.Code != (ushort)EdnsOptionCodeEnum.ClientSubnet
                    ),
                    clientSubnetOption,
                ]
            ),
        };
        clone.Questions.AddRange(original.Questions);

        return clone;
    }

    /// <summary>
    ///     This method picks the configured source prefix length matching an address's own family.
    /// </summary>
    private static byte DefaultSourcePrefixLength(
        IPAddress address,
        EdnsClientSubnetOptions currentOptions
    ) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? currentOptions.SourcePrefixLengthIPv6
            : currentOptions.SourcePrefixLengthIPv4;
}

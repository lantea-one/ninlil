using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientSubnet;

/// <summary>
///     This class implements RFC 7871 EDNS Client Subnet (ECS): it reads an ECS option a client
///     supplied and builds the ECS option for outbound queries made for that client by
///     <c>RecursiveQueryResolver</c> and <c>ForwarderQueryResolver</c>. It needs no storage and
///     both projects reference <c>Server.Core</c>, so it lives here. It is always registered
///     and switched by <see cref="EdnsClientSubnetOptions.Enable" />.
///     <para>
///         <b>Precedence when building an outbound option</b> (highest first):
///         <see cref="EdnsClientSubnetOptions.OverrideAddress" />, when set, always wins.
///         Otherwise an ECS option the client supplied is used when its scope prefix length is 0
///         (RFC 7871 §6: a query carries 0); an option with another scope is treated as absent.
///         The outbound source prefix is the smaller of the client's and the configured one for
///         its family, so a client cannot make this server disclose more than the operator
///         allows. A client prefix of 0 stays 0 (opt-out, RFC 7871 §7.1.2). Otherwise the option
///         is built from the client's source address, cut
///         to <see cref="EdnsClientSubnetOptions.SourcePrefixLengthIPv4" /> or
///         <see cref="EdnsClientSubnetOptions.SourcePrefixLengthIPv6" />.
///     </para>
/// </summary>
/// <param name="options">The ECS settings, read on every call.</param>
public sealed class EdnsClientSubnetService(IOptionsMonitor<EdnsClientSubnetOptions> options)
{
    /// <summary>
    ///     This method looks for an ECS option in a client's query.
    /// </summary>
    /// <param name="request">The client's decoded query.</param>
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
    ///     This method builds the ECS option for an outbound query made for a client, using the
    ///     precedence in the class summary.
    /// </summary>
    /// <param name="downstreamClientAddress">The client's source address.</param>
    /// <param name="downstreamRequest">
    ///     The client's decoded query, checked for an ECS option to relay.
    /// </param>
    /// <returns>
    ///     The ECS option to attach, or <see langword="null" /> when ECS is disabled, or the
    ///     address cannot be determined (usually an unparseable
    ///     <see cref="EdnsClientSubnetOptions.OverrideAddress" />).
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
            && clientSupplied.ScopePrefixLength == 0
        )
            return EdnsClientSubnetCodec.Encode(
                clientSupplied.Address,
                Math.Min(
                    clientSupplied.SourcePrefixLength,
                    DefaultSourcePrefixLength(clientSupplied.Address, currentOptions)
                ),
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
    ///     This method builds a copy of <paramref name="original" /> (header flags, questions and
    ///     EDNS) with <paramref name="clientSubnetOption" /> replacing any ECS option it carried.
    ///     <c>ForwarderQueryResolver</c> uses it because it forwards the client's request largely
    ///     as-is and must not change the shared <c>DnsQueryContext.Request</c> that later
    ///     resolvers also see.
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
    ///     This method picks the configured source prefix length for an address's family.
    /// </summary>
    private static byte DefaultSourcePrefixLength(
        IPAddress address,
        EdnsClientSubnetOptions currentOptions
    ) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? currentOptions.SourcePrefixLengthIPv6
            : currentOptions.SourcePrefixLengthIPv4;
}

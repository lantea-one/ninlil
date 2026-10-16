using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This class implements <see cref="IDnsQueryDispatcher" />. It publishes
///     <see cref="DnsQueryReceivedEvent" />, answers FORMERR for a query with no question,
///     NOTIMP for an opcode other than QUERY, NOTIFY and UPDATE (the kinds of message this server
///     implements) and BADVERS for an EDNS version above 0, then offers the query to each registered
///     <see cref="IDnsQueryResolver" /> in registration order and returns the first answer.
///     <c>Ninlil.Web/Program.cs</c> registers them as NOTIFY, dynamic update, authoritative,
///     forwarding, then recursive, so a hosted zone is answered authoritatively even when the
///     query asks for recursion. A query no resolver claims is answered REFUSED.
/// </summary>
/// <param name="resolvers">
///     Every registered resolver, tried in the order dependency injection provides them.
/// </param>
/// <param name="eventPublisher">
///     The publisher for <see cref="DnsQueryReceivedEvent" />, raised for every query that
///     reaches the dispatcher. <see cref="DnsQueryAnsweredEvent" /> is published by
///     <see cref="Pipeline.DnsQueryProcessingPipeline" /> instead, because its response check
///     can still replace the answer returned here.
/// </param>
public class DnsQueryDispatcher(
    IEnumerable<IDnsQueryResolver> resolvers,
    IDnsServerEventPublisher eventPublisher
) : IDnsQueryDispatcher
{
    /// <summary>
    ///     This constant contains the UDP payload size advertised in an OPT record this server
    ///     builds itself, such as in a BADVERS response. 1232 octets is the DNS Flag Day 2020
    ///     value: large enough for most DNSSEC answers while avoiding IP fragmentation.
    /// </summary>
    public const ushort DefaultUdpPayloadSizeOctets = 1232;

    /// <summary>
    ///     This field contains the registered resolvers, copied once so the injected
    ///     <see cref="IEnumerable{T}" /> is not enumerated on every query.
    /// </summary>
    private readonly IReadOnlyList<IDnsQueryResolver> _resolvers = [.. resolvers];

    /// <inheritdoc />
    public async ValueTask<DnsMessage> DispatchAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    )
    {
        DnsMessage request = context.Request;

        eventPublisher.Publish(
            new DnsQueryReceivedEvent(
                DateTimeOffset.UtcNow,
                context.Protocol,
                context.RemoteAddress,
                request.Questions.Count > 0 ? request.Questions[0].Name.ToString() : null,
                request.Questions.Count > 0 ? request.Questions[0].Type : null
            )
        );

        // A query with no question cannot be answered.
        if (request.Questions.Count == 0)
            return WithResponseCode(request, DnsResponseCodeEnum.FormatError);

        // RFC 1035 §4.1.1: NOTIMP means the server does not support the kind of query. IQUERY
        // (obsolete, RFC 3425), STATUS, DSO (RFC 8490 §5.1) and unassigned opcodes would otherwise
        // be answered as a plain QUERY by the resolvers, which do not look at the opcode.
        if (
            request.Opcode
            is not (DnsOpcodeEnum.Query or DnsOpcodeEnum.Notify or DnsOpcodeEnum.Update)
        )
            return WithResponseCode(request, DnsResponseCodeEnum.NotImplemented);

        // RFC 6891 §6.1.3: an unsupported EDNS version gets BADVERS in an OPT record that
        // advertises version 0.
        if (request.Edns is { Version: > 0 })
        {
            DnsMessage badVersionResponse = WithResponseCode(
                request,
                DnsResponseCodeEnum.BadVersionOrBadSignature
            );
            badVersionResponse.Edns = new DnsEdnsOptions(0, DefaultUdpPayloadSizeOctets, false, []);
            return badVersionResponse;
        }

        foreach (IDnsQueryResolver resolver in _resolvers)
        {
            DnsQueryResolution resolution = await resolver.TryResolveAsync(context, stoppingToken);

            if (resolution.IsHandled)
                return resolution.Response;
        }

        // No resolver claimed the query.
        return WithResponseCode(request, DnsResponseCodeEnum.Refused);
    }

    /// <summary>
    ///     This method builds a minimal response with the given response code: the query's ID,
    ///     opcode, RD and CD flags, and its question section. The question is echoed because most
    ///     clients check that it matches the query they sent.
    /// </summary>
    private static DnsMessage WithResponseCode(
        DnsMessage request,
        DnsResponseCodeEnum responseCode
    ) =>
        new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            RecursionDesired = request.RecursionDesired,
            CheckingDisabled = request.CheckingDisabled,
            ResponseCode = responseCode,
            Questions = [.. request.Questions],
        };
}

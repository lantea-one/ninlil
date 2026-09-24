using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This class implements <see cref="IDnsQueryDispatcher" /> by trying every registered
///     <see cref="IDnsQueryResolver" /> in turn -- in whichever order they were registered with
///     dependency injection -- and returning the first one's answer. The composition root is
///     expected to register the authoritative resolver before the recursive one, so that a name
///     this server is authoritative for is always answered authoritatively even if the query
///     also asked for recursion; a query that no registered resolver claims is answered REFUSED,
///     the same response RFC 1035 expects from a server that is neither authoritative for a name
///     nor willing to recurse for it.
/// </summary>
/// <param name="resolvers">
///     Every registered resolver, tried in the order dependency injection provides them.
/// </param>
/// <param name="eventPublisher">
///     The publisher this dispatcher reports a query's own <em>arrival</em> through
///     (<see cref="DnsQueryReceivedEvent" />). This dispatcher deliberately does <b>not</b>
///     publish <see cref="DnsQueryAnsweredEvent" /> itself, even though it is the component that
///     most often produces a query's final answer -- <see cref="Pipeline.DnsQueryProcessingPipeline" />
///     is the one place a response can no longer be replaced by anything downstream (its own
///     response-IP filter check runs strictly after this dispatcher returns, and can still
///     overwrite an already-produced answer with a denial), so it is the only place that event
///     can safely be published exactly once, for the response actually sent, regardless of
///     whether this dispatcher, a filter/schedule/pause check, or a DNS rewrite ultimately
///     produced it -- see that class's own <c>eventPublisher</c> parameter doc comment for the
///     full reasoning.
/// </param>
public class DnsQueryDispatcher(
    IEnumerable<IDnsQueryResolver> resolvers,
    IDnsServerEventPublisher eventPublisher
) : IDnsQueryDispatcher
{
    /// <summary>
    ///     This constant contains the UDP payload size this server advertises in an OPT record it
    ///     generates itself (rather than one copied from elsewhere), such as an error response to
    ///     an EDNS version it does not support. 1232 octets is the value the DNS operator
    ///     community converged on in 2020 as large enough for a DNSSEC-signed answer while still
    ///     comfortably avoiding IP fragmentation on the modern Internet.
    /// </summary>
    public const ushort DefaultUdpPayloadSizeOctets = 1232;

    /// <summary>
    ///     This property contains every registered resolver, materialized once so that the
    ///     lazy <see cref="IEnumerable{T}" /> dependency injection provides is not re-evaluated
    ///     (and its registration order potentially re-derived) on every single query.
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

        // A query with no question at all cannot be meaningfully answered.
        if (request.Questions.Count == 0)
            return WithResponseCode(request, DnsResponseCodeEnum.FormatError);

        // RFC 6891 §6.1.3: a version of EDNS this server does not implement must be rejected
        // with BADVERS, itself carried in an OPT record this server generates, advertising the
        // version (0) it does support.
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

        // Nobody claimed the query: this server is neither authoritative for the name nor
        // willing (or able) to recurse for it.
        return WithResponseCode(request, DnsResponseCodeEnum.Refused);
    }

    /// <summary>
    ///     This method builds the minimal response header this dispatcher itself is responsible
    ///     for (as opposed to a resolver's own, fuller answer): the query's identifier and
    ///     recursion-desired flag copied across, marked as a response, carrying the given
    ///     response code. The question section is always echoed back too -- a real request was
    ///     already successfully decoded by this point, so there is no reason not to, and a
    ///     validating/cookie-aware client (and, in practice, most ordinary resolvers) expects it
    ///     to match the query it sent.
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

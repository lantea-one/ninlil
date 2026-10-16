using System.Net;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Rewrites;

/// <summary>
///     This interface is DNS rewrites: operator-defined answers for a domain that bypass
///     resolution. <see cref="Pipeline.DnsQueryProcessingPipeline" /> calls it. It is defined
///     here, without storage types, and implemented by <c>Ninlil.Dns.Server.Rewrites</c>, so
///     <c>Server.Core</c> needs no repository reference.
/// </summary>
public interface IDnsRewriteEngine
{
    /// <summary>
    ///     This method checks the request's first question against every enabled rewrite and,
    ///     if one matches, builds the complete response. The pipeline calls it after the cookie,
    ///     schedule and filter checks (a rewrite never overrides a denial) and before the
    ///     dispatcher, so no resolver sees a rewritten query. A CNAME answer to an A or AAAA
    ///     question is built as the CNAME alone; the pipeline then resolves the target and adds
    ///     its records, so an implementation does not chase the target itself.
    /// </summary>
    /// <param name="request">The decoded request.</param>
    /// <param name="remoteAddress">
    ///     The client's source address, used only to match rewrites scoped to a client or client
    ///     group (<c>DnsRewriteEntity.ClientId</c> and <c>ClientGroupId</c>) through
    ///     <c>IClientIdentityResolver</c>. With <see langword="null" />, only global rewrites can
    ///     match.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The complete response, if a rewrite matched the first question and its record kind
    ///     answers the question's type; otherwise <see langword="null" />, and resolution
    ///     continues.
    /// </returns>
    ValueTask<DnsMessage> TryBuildResponseAsync(
        DnsMessage request,
        IPAddress remoteAddress = null,
        CancellationToken stoppingToken = default
    );
}

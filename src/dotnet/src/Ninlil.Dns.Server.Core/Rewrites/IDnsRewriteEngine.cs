using System.Net;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Rewrites;

/// <summary>
///     This interface is <c>Server.Core</c>'s own Data.Abstractions-free contract for
///     DNS rewrites, implemented by <c>Ninlil.Dns.Server.Rewrites</c> (which does
///     depend on <c>Data.Abstractions</c>) and consulted by
///     <see cref="Pipeline.DnsQueryProcessingPipeline" /> -- the same "define the abstraction
///     here, implement it in a dedicated project that may depend on storage" shape
///     <see cref="Filtering.IDnsFilterEngine" /> already establishes for its own
///     block/allow-list engine, kept consistent so <c>Server.Core</c> itself never needs a
///     repository reference.
/// </summary>
public interface IDnsRewriteEngine
{
    /// <summary>
    ///     This method checks the request's own first question against every configured,
    ///     enabled rewrite and, if one matches, builds the complete answer response for it.
    ///     Called after every filter/cookie check has already passed (a rewrite never overrides
    ///     a denial) and before <see cref="Resolution.IDnsQueryDispatcher.DispatchAsync" /> --
    ///     a matched rewrite is answered directly, without either the authoritative, forwarder,
    ///     or recursive resolver ever seeing the query, exactly like a zone/forwarder/recursion
    ///     bypass this project's own "consulted early in the pipeline" design calls for.
    /// </summary>
    /// <param name="request">The already-decoded request.</param>
    /// <param name="remoteAddress">
    ///     The querying client's own source address, used only to resolve a client-/client-group-
    ///     scoped rewrite (see <c>Ninlil.Dns.Data.Abstractions.Entity.DnsRewriteEntity.ClientId</c>/
    ///     <c>ClientGroupId</c>) via the optional <c>IClientIdentityResolver</c> a real
    ///     implementation may have. <see langword="null" /> (the default, so every pre-existing
    ///     caller/test keeps compiling and behaving unchanged) means client-derived scoping is
    ///     entirely skipped -- only an unscoped (global) rewrite can ever match, identical to this
    ///     method's own behavior before client scoping existed.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The complete answer response, if a rewrite matched the request's own first question
    ///     and its own record kind answers the question's own type; otherwise
    ///     <see langword="null" />, meaning normal resolution should proceed unchanged.
    /// </returns>
    ValueTask<DnsMessage> TryBuildResponseAsync(
        DnsMessage request,
        IPAddress remoteAddress = null,
        CancellationToken stoppingToken = default
    );
}

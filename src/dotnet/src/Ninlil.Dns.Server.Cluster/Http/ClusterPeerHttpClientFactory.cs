using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Authentication;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Http;

/// <summary>
///     This class builds the <see cref="HttpClient" /> instances every outbound cluster-channel
///     call (a join-completion callback, a heartbeat probe) uses, and signs the requests sent
///     through them. Each call gets its own freshly built client, pinned to the exact peer
///     certificate thumbprint that call is targeting -- unlike the ordinary outbound HTTP clients
///     <c>Server.Forwarding</c>/<c>Server.Acme</c> use, a cluster-channel client's own trust
///     decision (<see cref="ClusterPeerCertificateValidator" />) is peer-specific, so it cannot be
///     built once and shared via the ordinary <see cref="System.Net.Http.IHttpClientFactory" />
///     named-client pattern without a distinct registration per peer -- and with a genuine
///     homelab-scale peer count (a handful of nodes at most), a plain, per-call
///     <see cref="HttpClient" /> is simpler and avoids that indirection entirely.
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a fresh dependency injection scope for every signing lookup --
///     this class is itself registered as a singleton (see its own registration in
///     <c>ServiceCollectionClusterExtension</c>, since <c>ClusterHeartbeatBackgroundService</c>,
///     also a singleton-lifetime hosted service, depends on it directly), so it cannot hold a
///     constructor-injected <see cref="ITsigKeyRepository" /> itself -- that service is scoped,
///     and a singleton capturing a scoped dependency is a real, ASP.NET Core-detected DI
///     validation error, not merely a style preference. Resolving it fresh, per call, through its
///     own short-lived scope is the same pattern <see cref="Heartbeat.ClusterHeartbeatBackgroundService" />
///     itself already uses for its own scoped dependencies.
/// </param>
/// <param name="secretProtector">The protector used to decrypt a signing key's own shared secret.</param>
public class ClusterPeerHttpClientFactory(
    IServiceScopeFactory scopeFactory,
    TsigSecretProtector secretProtector
)
{
    /// <summary>
    ///     This method builds an <see cref="HttpClient" /> that trusts exactly one certificate --
    ///     see <see cref="ClusterPeerCertificateValidator" />.
    /// </summary>
    /// <param name="pinnedCertificateThumbprintHex">The peer's own known-good certificate thumbprint.</param>
    /// <param name="timeout">How long a request through this client may take before it is abandoned.</param>
    public HttpClient CreateClient(string pinnedCertificateThumbprintHex, TimeSpan timeout)
    {
        HttpClientHandler handler = new()
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null
                && ClusterPeerCertificateValidator.IsTrusted(
                    certificate,
                    pinnedCertificateThumbprintHex
                ),
        };

        return new HttpClient(handler) { Timeout = timeout };
    }

    /// <summary>
    ///     This method signs and sends one cluster-channel request, looking up the named signing
    ///     key's own shared secret from this node's own local TSIG key registry.
    /// </summary>
    /// <param name="client">The client to send through -- see <see cref="CreateClient" />.</param>
    /// <param name="baseUrl">The peer's own base URL, for example <c>"https://192.168.1.20:443"</c>.</param>
    /// <param name="path">The absolute path to request, for example <c>"/api/cluster/channel/heartbeat"</c>.</param>
    /// <param name="tsigKeyName">The name of the locally known key to sign with.</param>
    /// <param name="jsonBody">The request body, already serialized to JSON.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The peer's own response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no key named <paramref name="tsigKeyName" /> exists locally.</exception>
    public async Task<HttpResponseMessage> SendSignedAsync(
        HttpClient client,
        string baseUrl,
        string path,
        string tsigKeyName,
        string jsonBody,
        CancellationToken stoppingToken = default
    )
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ITsigKeyRepository tsigKeyRepository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();

        TsigKeyEntity key =
            await tsigKeyRepository.FindByNameAsync(tsigKeyName, stoppingToken)
            ?? throw new InvalidOperationException(
                $"No locally known TSIG key named '{tsigKeyName}' exists to sign this request with."
            );

        byte[] secret = secretProtector.Unprotect(key.SecretEncrypted);

        return await SendSignedWithSecretAsync(
            client,
            baseUrl,
            path,
            tsigKeyName,
            secret,
            jsonBody,
            stoppingToken
        );
    }

    /// <summary>
    ///     This method signs and sends one cluster-channel request using a raw shared secret
    ///     rather than a locally registered key -- the one case
    ///     <see cref="SendSignedAsync(HttpClient,string,string,string,string,CancellationToken)" />
    ///     itself cannot cover: the very first callback a joining node makes back to its inviter
    ///     (see <c>ClusterJoinService.JoinAsync</c>), before the shared key handed over in the
    ///     invite has been persisted locally at all -- persisting it before this callback even
    ///     succeeds would leave a real, usable TSIG key on record for a join that never actually
    ///     completed.
    /// </summary>
    public async Task<HttpResponseMessage> SendSignedWithSecretAsync(
        HttpClient client,
        string baseUrl,
        string path,
        string tsigKeyName,
        byte[] secret,
        string jsonBody,
        CancellationToken stoppingToken = default
    )
    {
        // What/why/how: every outbound cluster-channel request is signed the same way --
        // timestamp it now, compute an HMAC over method/path/timestamp/body (the receiving
        // node's own ClusterTsigAuthenticationHandler recomputes this exact MAC to verify it),
        // then pack the key name/timestamp/MAC into one header value so the receiver can parse
        // and re-derive everything it needs to check.
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "");
        long unixTimestampSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        byte[] mac = ClusterTsigRequestSigner.ComputeMac(
            secret,
            HttpMethod.Post.Method,
            path,
            unixTimestampSeconds,
            bodyBytes
        );

        // How: the signature travels in its own dedicated header rather than folded into the
        // body, so the JSON payload itself never needs to be re-parsed or mutated just to attach
        // authentication -- the body is instead handed to HttpContent completely unchanged.
        using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}{path}");
        request.Headers.Add(
            ClusterTsigAuthenticationDefaults.SignatureHeaderName,
            ClusterTsigRequestSigner.FormatHeaderValue(tsigKeyName, unixTimestampSeconds, mac)
        );
        request.Content = new ByteArrayContent(bodyBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return await client.SendAsync(request, stoppingToken);
    }
}

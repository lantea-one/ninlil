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
///     This class builds the <see cref="HttpClient" /> for every outbound cluster-channel call
///     (join callback, heartbeat, config sync, username check, statistics) and signs the
///     requests. Each call gets a new client and handler pinned to the target peer's certificate
///     thumbprint (<see cref="ClusterPeerCertificateValidator" />). Because the trust decision is
///     per peer, a shared <see cref="System.Net.Http.IHttpClientFactory" /> named client does not
///     fit, and with a handful of peers a client per call is simpler.
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a scope for each key lookup. This class is a singleton (used
///     by singleton services such as <see cref="Heartbeat.ClusterHeartbeatBackgroundService" />),
///     so it cannot take the scoped <see cref="ITsigKeyRepository" /> in its constructor.
/// </param>
/// <param name="secretProtector">The protector used to decrypt a signing key's secret.</param>
public class ClusterPeerHttpClientFactory(
    IServiceScopeFactory scopeFactory,
    TsigSecretProtector secretProtector
)
{
    /// <summary>
    ///     This method builds an <see cref="HttpClient" /> that trusts only the certificate with the
    ///     given thumbprint (see <see cref="ClusterPeerCertificateValidator" />). A request that
    ///     exceeds <paramref name="timeout" /> throws <see cref="TaskCanceledException" />.
    /// </summary>
    /// <param name="pinnedCertificateThumbprintHex">The peer's pinned certificate thumbprint.</param>
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
    ///     This method signs and sends one cluster-channel POST request, reading the named key's
    ///     secret from the local TSIG key store.
    /// </summary>
    /// <param name="client">The client to send through (see <see cref="CreateClient" />).</param>
    /// <param name="baseUrl">The peer's base URL, for example <c>"https://192.168.1.20:443"</c>.</param>
    /// <param name="path">The absolute path to request, for example <c>"/api/cluster/channel/heartbeat"</c>.</param>
    /// <param name="tsigKeyName">The name of the locally known key to sign with.</param>
    /// <param name="jsonBody">The request body, already serialized to JSON.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The peer's response.</returns>
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
    ///     This method signs and sends one cluster-channel POST request with a given secret
    ///     instead of a stored key. <c>ClusterJoinService.JoinAsync</c> needs it for the join
    ///     callback, which is sent before the invite's key is stored, so a failed join leaves no
    ///     usable key behind.
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
        // Timestamp now, MAC over method, path, timestamp and body (which
        // ClusterTsigAuthenticationHandler recomputes), and key name, timestamp and MAC in one
        // header.
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "");
        long unixTimestampSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        byte[] mac = ClusterTsigRequestSigner.ComputeMac(
            secret,
            HttpMethod.Post.Method,
            path,
            unixTimestampSeconds,
            bodyBytes
        );

        // The signature goes in a header, so the body is sent exactly as signed.
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

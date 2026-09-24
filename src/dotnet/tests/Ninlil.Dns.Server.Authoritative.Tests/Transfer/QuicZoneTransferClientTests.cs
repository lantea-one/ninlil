using System.Net;
using System.Net.Quic;
using System.Runtime.Versioning;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests <see cref="QuicZoneTransferClient" />. Its success path shares the exact
///     same, already-thoroughly-covered <c>ZoneTransferPullOverStream</c> logic
///     <see cref="ZoneTransferPullCodecTests" />/<see cref="TlsZoneTransferClientTests" /> already
///     prove, differing only in how the connection itself is established -- and this environment,
///     like every other QUIC-dependent test in this project (see <c>DnsOverQuicListenerTests</c>'s
///     own identical caveat), cannot actually exercise a real QUIC handshake without
///     <c>libmsquic</c> installed. This class only proves the one thing genuinely both testable and
///     meaningful here: a real, deterministic <see cref="QuicConnection.IsSupported" />-gated
///     failure, not an unhandled exception, when this platform cannot do QUIC at all.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public class QuicZoneTransferClientTests
{
    private static readonly DnsName Zone = DnsName.Parse("example.test.");

    private sealed class NoOpTsigService : ITsigService
    {
        public Task<DnsMessage> SignAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<TsigVerificationResult> VerifyAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<DnsMessage> SignResponseAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            byte[] requestMac,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<DnsMessage> SignContinuationAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            byte[] priorMac,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<TsigVerificationResult> VerifyResponseAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            byte[] requestMac,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<TsigVerificationResult> VerifyContinuationAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            byte[] priorMac,
            IReadOnlyList<byte[]> unsignedMessageBytesSincePriorTsig,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");
    }

    [Fact]
    public async Task PullAsync_WhenQuicIsNotSupportedOnThisPlatform_ReturnsACleanFailure()
    {
        if (QuicConnection.IsSupported)
            return; // Nothing to prove here in an environment that actually has libmsquic.

        QuicZoneTransferClient client = new(
            new NoOpTsigService(),
            new StaticOptionsMonitor<ZoneTransferClientOptions>(new ZoneTransferClientOptions())
        );

        ZoneTransferPullResult result = await client.PullAsync(
            IPAddress.Loopback,
            Zone,
            null,
            null
        );

        Assert.False(result.Succeeded);
        Assert.Contains("not supported", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }
}

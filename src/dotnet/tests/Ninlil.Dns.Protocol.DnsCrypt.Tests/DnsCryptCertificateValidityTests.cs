using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class tests which certificates <see cref="DnsCryptCertificateDiscoveryResponder" />
///     publishes at the edges of their validity. The responder reads the clock itself, so each
///     test builds its certificates around the current second and retries when the second
///     changes during the call. The rule is DNSCrypt v2 section 12: <c>&lt;ts-end&gt;</c> is the
///     date the certificate is "valid until (inclusive)", a Unix timestamp in whole seconds.
/// </summary>
public sealed class DnsCryptCertificateValidityTests
{
    private static readonly DnsName ProviderName = DnsName.Parse("2.dnscrypt-cert.test.example.");

    /// <summary>
    ///     This method asks the responder for the certificates published when the store holds one
    ///     certificate valid from <paramref name="startOffset" /> to <paramref name="endOffset" />
    ///     seconds around the current second, and returns how many TXT answers it gave.
    /// </summary>
    private static int PublishedCount(int startOffset, int endOffset)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            long second = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            (byte[] publicKey, byte[] secretKey) = DnsCryptCryptoBox.GenerateKeyPair(
                DnsCryptEsVersion.XChaCha20Poly1305
            );
            InMemoryDnsCryptCertificateStore store = new(null);
            store.Save([
                new DnsCryptCertificateEntry(
                    new DnsCryptCertificate(
                        DnsCryptEsVersion.XChaCha20Poly1305,
                        publicKey,
                        RandomNumberGenerator.GetBytes(8),
                        1,
                        DateTimeOffset.FromUnixTimeSeconds(second + startOffset),
                        DateTimeOffset.FromUnixTimeSeconds(second + endOffset),
                        new byte[64]
                    ),
                    secretKey
                ),
            ]);
            DnsMessage request = new() { Id = 1, Opcode = DnsOpcodeEnum.Query };
            request.Questions.Add(
                new DnsQuestion(ProviderName, DnsResourceRecordTypeEnum.Txt, DnsClassEnum.Internet)
            );

            Assert.True(
                DnsCryptCertificateDiscoveryResponder.TryBuildResponse(
                    request,
                    ProviderName,
                    store,
                    out DnsMessage response
                )
            );

            // A call that straddles a second boundary answers for a different second than the
            // certificate was built around.
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() == second)
                return response.Answers.Count;
        }

        throw new InvalidOperationException("The clock second changed on every attempt.");
    }

    [Fact]
    public void TryBuildResponse_ACertificateWhoseTsEndIsTheCurrentSecond_IsPublished()
    {
        Assert.Equal(1, PublishedCount(-60, 0));
    }

    [Fact]
    public void TryBuildResponse_ACertificateWhoseTsEndWasThePreviousSecond_IsNotPublished()
    {
        Assert.Equal(0, PublishedCount(-60, -1));
    }

    [Fact]
    public void TryBuildResponse_ACertificateWhoseTsStartIsTheCurrentSecond_IsPublished()
    {
        Assert.Equal(1, PublishedCount(0, 60));
    }

    [Fact]
    public void TryBuildResponse_ACertificateWhoseTsStartIsTheNextSecond_IsNotPublished()
    {
        Assert.Equal(0, PublishedCount(1, 60));
    }
}

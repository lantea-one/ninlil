// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Tests;

/// <summary>
///     This class tests <see cref="DnsCryptCertificate.IsValidAt" /> and
///     <see cref="DnsCryptCertificate.IsExpiredAt" /> at the edges of the validity period.
///     DNSCrypt v2 section 12 defines <c>&lt;ts-end&gt;</c> as the date the certificate is "valid
///     until (inclusive)" in whole Unix seconds.
/// </summary>
public class DnsCryptCertificateValidityTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
    private static readonly DateTimeOffset End = DateTimeOffset.FromUnixTimeSeconds(2_000_000);

    private static DnsCryptCertificate Certificate() =>
        new(DnsCryptEsVersion.XChaCha20Poly1305, [], [], 1, Start, End, []);

    [Fact]
    public void IsValidAt_ThroughoutTheSecondNamedByTsEnd_IsTrue()
    {
        DnsCryptCertificate certificate = Certificate();

        Assert.True(certificate.IsValidAt(End));
        Assert.True(certificate.IsValidAt(End.AddMilliseconds(999)));
        Assert.False(certificate.IsValidAt(End.AddSeconds(1)));
        Assert.True(certificate.IsExpiredAt(End.AddSeconds(1)));
        Assert.False(certificate.IsExpiredAt(End.AddMilliseconds(999)));
    }

    [Fact]
    public void IsValidAt_FromTheSecondNamedByTsStart_IsTrue()
    {
        DnsCryptCertificate certificate = Certificate();

        Assert.False(certificate.IsValidAt(Start.AddMilliseconds(-1)));
        Assert.True(certificate.IsValidAt(Start));
        Assert.False(certificate.IsExpiredAt(Start.AddMilliseconds(-1)));
    }
}

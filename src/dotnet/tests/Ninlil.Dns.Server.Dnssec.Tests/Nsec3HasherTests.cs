using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Tests;

/// <summary>
///     This class tests <see cref="Nsec3Hasher" />: the RFC 5155 §5 salted, iterated hash and the
///     base32hex (RFC 4648 §7) owner name encoding. The tests check determinism and round trips;
///     none compares against a published RFC 5155 test vector.
/// </summary>
public class Nsec3HasherTests
{
    [Fact]
    public void ComputeHashIsDeterministic()
    {
        DnsName name = DnsName.Parse("www.example.com");
        byte[] salt = [0xAB, 0x12];

        byte[] first = Nsec3Hasher.ComputeHash(1, name, 3, salt);
        byte[] second = Nsec3Hasher.ComputeHash(1, name, 3, salt);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeHashChangesWithIterationsAndSalt()
    {
        DnsName name = DnsName.Parse("www.example.com");

        byte[] noIterations = Nsec3Hasher.ComputeHash(1, name, 0, []);
        byte[] withIterations = Nsec3Hasher.ComputeHash(1, name, 3, []);
        byte[] withSalt = Nsec3Hasher.ComputeHash(1, name, 0, [0xAB]);

        Assert.NotEqual(noIterations, withIterations);
        Assert.NotEqual(noIterations, withSalt);
    }

    [Fact]
    public void UnsupportedHashAlgorithmThrows()
    {
        Assert.Throws<NotSupportedException>(() =>
            Nsec3Hasher.ComputeHash(2, DnsName.Parse("www.example.com"), 0, [])
        );
    }

    [Fact]
    public void Base32HexRoundTrips()
    {
        byte[] hash = Nsec3Hasher.ComputeHash(1, DnsName.Parse("www.example.com"), 3, [0xAB, 0x12]);

        string encoded = Nsec3Hasher.ToBase32Hex(hash);
        byte[] decoded = Nsec3Hasher.FromBase32Hex(encoded);

        Assert.Equal(hash, decoded);
    }

    [Fact]
    public void ToOwnerNameAndExtractHashFromOwnerNameRoundTrip()
    {
        DnsName apexName = DnsName.Parse("example.com");
        byte[] hash = Nsec3Hasher.ComputeHash(1, DnsName.Parse("www.example.com"), 3, [0xAB, 0x12]);

        DnsName ownerName = Nsec3Hasher.ToOwnerName(hash, apexName);

        Assert.EndsWith(".example.com.", ownerName.ToString());
        Assert.Equal(hash, Nsec3Hasher.ExtractHashFromOwnerName(ownerName));
    }
}

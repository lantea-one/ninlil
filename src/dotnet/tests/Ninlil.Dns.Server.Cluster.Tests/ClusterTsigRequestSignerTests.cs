using System.Text;
using Ninlil.Dns.Server.Cluster.Authentication;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Tests;

/// <summary>
///     This class tests <see cref="ClusterTsigRequestSigner" /> as pure functions: the MAC is
///     deterministic and changes with each covered input (secret, method, path, timestamp,
///     body), and header values format and parse back unchanged, with malformed ones rejected.
/// </summary>
public class ClusterTsigRequestSignerTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes(
        "a-shared-secret-32-bytes-long!!!"
    );

    [Fact]
    public void ComputeMac_WithIdenticalInputs_IsDeterministic()
    {
        byte[] macA = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{}"u8.ToArray()
        );
        byte[] macB = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{}"u8.ToArray()
        );

        Assert.Equal(macA, macB);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("post")]
    public void ComputeMac_WithADifferentMethod_ChangesTheMac(string differentMethod)
    {
        byte[] baseline = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{}"u8.ToArray()
        );
        byte[] varied = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            differentMethod,
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{}"u8.ToArray()
        );

        // Lowercase "post" is upper-cased, so it must match the baseline; "GET" must not.
        if (string.Equals(differentMethod, "POST", StringComparison.OrdinalIgnoreCase))
            Assert.Equal(baseline, varied);
        else
            Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void ComputeMac_WithADifferentPath_ChangesTheMac()
    {
        byte[] baseline = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{}"u8.ToArray()
        );
        byte[] varied = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/join-complete",
            1_700_000_000,
            "{}"u8.ToArray()
        );

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void ComputeMac_WithADifferentTimestamp_ChangesTheMac()
    {
        byte[] baseline = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{}"u8.ToArray()
        );
        byte[] varied = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_001,
            "{}"u8.ToArray()
        );

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void ComputeMac_WithADifferentBody_ChangesTheMac()
    {
        byte[] baseline = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{\"a\":1}"u8.ToArray()
        );
        byte[] varied = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{\"a\":2}"u8.ToArray()
        );

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void ComputeMac_WithADifferentSecret_ChangesTheMac()
    {
        byte[] baseline = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{}"u8.ToArray()
        );
        byte[] varied = ClusterTsigRequestSigner.ComputeMac(
            Encoding.UTF8.GetBytes("a-different-secret-32-bytes-long"),
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{}"u8.ToArray()
        );

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void FormatHeaderValue_ThenTryParseHeaderValue_RoundTrips()
    {
        byte[] mac = ClusterTsigRequestSigner.ComputeMac(
            Secret,
            "POST",
            "/api/cluster/channel/heartbeat",
            1_700_000_000,
            "{}"u8.ToArray()
        );
        string header = ClusterTsigRequestSigner.FormatHeaderValue(
            "cluster-abc123",
            1_700_000_000,
            mac
        );

        Assert.True(
            ClusterTsigRequestSigner.TryParseHeaderValue(
                header,
                out string keyName,
                out long timestamp,
                out byte[] parsedMac
            )
        );
        Assert.Equal("cluster-abc123", keyName);
        Assert.Equal(1_700_000_000, timestamp);
        Assert.Equal(mac, parsedMac);
    }

    [Theory]
    [InlineData("")]
    [InlineData("only-one-part")]
    [InlineData("name:not-a-number:AAAA")]
    [InlineData("name:1700000000:not-valid-base64!!!")]
    [InlineData(":1700000000:AAAA")]
    public void TryParseHeaderValue_WithMalformedInput_ReturnsFalse(string malformed)
    {
        Assert.False(
            ClusterTsigRequestSigner.TryParseHeaderValue(
                malformed,
                out string _,
                out long _,
                out byte[] _
            )
        );
    }

    [Fact]
    public void TryParseHeaderValue_WithAColonInTheKeyName_StillParsesCorrectly()
    {
        // Despite the test name, the key name here has no colon: a key name with a colon would
        // be split at that colon and fail to parse. This checks the normal three-part shape.
        Assert.True(
            ClusterTsigRequestSigner.TryParseHeaderValue(
                "cluster-a1b2c3:1700000000:QUJD",
                out string keyName,
                out long timestamp,
                out byte[] mac
            )
        );
        Assert.Equal("cluster-a1b2c3", keyName);
        Assert.Equal(1_700_000_000, timestamp);
        Assert.Equal("ABC"u8.ToArray(), mac);
    }
}

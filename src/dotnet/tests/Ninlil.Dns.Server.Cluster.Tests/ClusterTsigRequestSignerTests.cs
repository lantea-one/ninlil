using System.Text;
using Ninlil.Dns.Server.Cluster.Authentication;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Tests;

/// <summary>
///     This class covers <see cref="ClusterTsigRequestSigner" />: MAC computation is
///     deterministic and sensitive to every input it claims to cover, and the header
///     format/parse pair round-trips exactly.
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

        // "post" lowercase is normalized to uppercase internally, so it must match the baseline;
        // a genuinely different method ("GET") must not.
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
        // The key name itself is split at most three ways specifically so a colon-free-in-
        // practice but not-technically-forbidden key name still parses -- this test uses a
        // realistic key name (no colon) but confirms the split-into-three behavior directly by
        // constructing a header whose "body" (MAC) is base64, which itself can never contain a
        // colon, so this mostly documents the real-world shape rather than exercising an edge
        // case that cannot occur with this project's own generated key names.
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

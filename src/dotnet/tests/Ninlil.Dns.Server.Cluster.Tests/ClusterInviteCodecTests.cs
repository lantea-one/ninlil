using Ninlil.Dns.Server.Cluster.Invite;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Tests;

/// <summary>
///     This class covers <see cref="ClusterInviteCodec" />: a real invite round-trips exactly,
///     and every malformed/tampered shape is rejected with a real error message rather than
///     throwing or silently misparsing.
/// </summary>
public class ClusterInviteCodecTests
{
    private static ClusterInviteCode BuildValidInvite() =>
        new()
        {
            Version = 1,
            InviterNodeId = Guid.Parse("4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b"),
            InviterHost = "10.0.1.10",
            InviterPort = 443,
            TsigKeyName = "cluster-a1b2c3d4",
            TsigKeySecretBase64 = Convert.ToBase64String(new byte[32]),
            InviterCertificateThumbprint = new string('a', 64),
            ExpiresAtUtc = DateTimeOffset.Parse("2026-09-04T15:00:00+00:00"),
        };

    [Fact]
    public void Encode_ThenTryDecode_RoundTripsEveryField()
    {
        ClusterInviteCode original = BuildValidInvite();

        string encoded = ClusterInviteCodec.Encode(original);

        Assert.True(
            ClusterInviteCodec.TryDecode(encoded, out ClusterInviteCode decoded, out string error)
        );
        Assert.Null(error);
        Assert.Equal(original.Version, decoded.Version);
        Assert.Equal(original.InviterNodeId, decoded.InviterNodeId);
        Assert.Equal(original.InviterHost, decoded.InviterHost);
        Assert.Equal(original.InviterPort, decoded.InviterPort);
        Assert.Equal(original.TsigKeyName, decoded.TsigKeyName);
        Assert.Equal(original.TsigKeySecretBase64, decoded.TsigKeySecretBase64);
        Assert.Equal(original.InviterCertificateThumbprint, decoded.InviterCertificateThumbprint);
        Assert.Equal(original.ExpiresAtUtc, decoded.ExpiresAtUtc);
    }

    [Fact]
    public void Encode_StartsWithTheDocumentedPrefix()
    {
        string encoded = ClusterInviteCodec.Encode(BuildValidInvite());

        Assert.StartsWith("ninlil-cluster-invite-1.", encoded, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryDecode_WithAnEmptyCode_ReturnsFalse(string emptyCode)
    {
        Assert.False(
            ClusterInviteCodec.TryDecode(emptyCode, out ClusterInviteCode _, out string error)
        );
        Assert.NotNull(error);
    }

    [Fact]
    public void TryDecode_WithoutTheRealPrefix_ReturnsFalse()
    {
        Assert.False(
            ClusterInviteCodec.TryDecode(
                "not-a-real-invite-code",
                out ClusterInviteCode _,
                out string error
            )
        );
        Assert.NotNull(error);
    }

    [Fact]
    public void TryDecode_WithCorruptedBase64_ReturnsFalse()
    {
        Assert.False(
            ClusterInviteCodec.TryDecode(
                "ninlil-cluster-invite-1.not-valid-base64url!!!",
                out ClusterInviteCode _,
                out string error
            )
        );
        Assert.NotNull(error);
    }

    [Fact]
    public void TryDecode_WithAWrongFormatVersion_ReturnsFalse()
    {
        ClusterInviteCode invite = BuildValidInvite();
        invite.Version = 2;

        string encoded = ClusterInviteCodec.Encode(invite);

        Assert.False(
            ClusterInviteCodec.TryDecode(encoded, out ClusterInviteCode _, out string error)
        );
        Assert.Contains("version", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(IncompleteInvites))]
    public void TryDecode_WithAMissingRequiredField_ReturnsFalse(ClusterInviteCode incomplete)
    {
        string encoded = ClusterInviteCodec.Encode(incomplete);

        Assert.False(
            ClusterInviteCodec.TryDecode(encoded, out ClusterInviteCode _, out string error)
        );
        Assert.NotNull(error);
    }

    public static TheoryData<ClusterInviteCode> IncompleteInvites()
    {
        TheoryData<ClusterInviteCode> data = [];

        ClusterInviteCode missingInviterNodeId = BuildValidInvite();
        missingInviterNodeId.InviterNodeId = Guid.Empty;
        data.Add(missingInviterNodeId);

        ClusterInviteCode missingHost = BuildValidInvite();
        missingHost.InviterHost = "";
        data.Add(missingHost);

        ClusterInviteCode badPort = BuildValidInvite();
        badPort.InviterPort = 0;
        data.Add(badPort);

        ClusterInviteCode missingKeyName = BuildValidInvite();
        missingKeyName.TsigKeyName = "";
        data.Add(missingKeyName);

        ClusterInviteCode missingSecret = BuildValidInvite();
        missingSecret.TsigKeySecretBase64 = "";
        data.Add(missingSecret);

        ClusterInviteCode missingThumbprint = BuildValidInvite();
        missingThumbprint.InviterCertificateThumbprint = "";
        data.Add(missingThumbprint);

        return data;
    }
}

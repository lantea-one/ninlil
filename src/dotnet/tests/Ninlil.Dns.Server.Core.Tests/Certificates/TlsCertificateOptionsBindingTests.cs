using System.Text;
using Microsoft.Extensions.Configuration;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Certificates;

/// <summary>
///     This class tests how <see cref="TlsCertificateOptions.SelfSignedSubjectAlternativeNames" />
///     reads back from configuration. The settings controllers store a saved list as a JSON array,
///     and the configuration binder appends array items to a list that already holds items, so a
///     saved list must come back as exactly what was saved.
/// </summary>
public sealed class TlsCertificateOptionsBindingTests
{
    [Theory]
    [InlineData("localhost", "dns.example.test")]
    [InlineData("dns.example.test")]
    [InlineData("192.0.2.1", "localhost")]
    public void Get_WithASavedNameList_ReturnsExactlyThatList(params string[] saved)
    {
        string json =
            "{\"Certificate\":{\"SelfSignedSubjectAlternativeNames\":["
            + string.Join(",", saved.Select(name => $"\"{name}\""))
            + "]}}";
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();

        TlsCertificateOptions options = configuration
            .GetSection("Certificate")
            .Get<TlsCertificateOptions>();

        Assert.Equal(saved, options.EffectiveSelfSignedSubjectAlternativeNames);
    }

    [Fact]
    public void EffectiveSelfSignedSubjectAlternativeNames_WithNothingSaved_IsLocalhost()
    {
        Assert.Equal(
            ["localhost"],
            new TlsCertificateOptions().EffectiveSelfSignedSubjectAlternativeNames
        );
    }
}

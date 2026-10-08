using Ninlil.Dns.Server.Acme;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class tests <see cref="AcmeCertificateIssuer.BuildPfxFileName" />, the guard that
///     keeps a domain name from pointing the PFX file outside the certificate directory.
/// </summary>
public sealed class AcmeCertificateIssuerFileNameTests
{
    [Theory]
    [InlineData("example.com", "example.com.pfx")]
    [InlineData("*.example.com", "*.example.com.pfx")]
    public void APlainDomainGivesItsPfxFileName(string domain, string expected) =>
        Assert.Equal(expected, AcmeCertificateIssuer.BuildPfxFileName(domain));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("../evil")]
    [InlineData(".hidden")]
    [InlineData("..")]
    public void ADomainThatIsNotAPlainFileNameIsRejected(string domain) =>
        Assert.Throws<InvalidOperationException>(() =>
            AcmeCertificateIssuer.BuildPfxFileName(domain)
        );
}

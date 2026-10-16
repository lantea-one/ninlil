using Ninlil.Dns.Server.Acme;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class tests <see cref="AcmeCertificateIssuer.BuildPfxFileName" />, the guard that
///     keeps a domain name from pointing the PFX file outside the certificate directory, and the
///     mapping of a wildcard's <c>*.</c> to <c>_wildcard.</c>, which must hold on every platform.
/// </summary>
public sealed class AcmeCertificateIssuerFileNameTests
{
    [Theory]
    [InlineData("example.com", "example.com.pfx")]
    [InlineData("*.example.com", "_wildcard.example.com.pfx")]
    [InlineData("Example.com.", "example.com.pfx")]
    public void ADomainGivesItsLowercasePfxFileAndAWildcardGetsAWildcardPrefix(
        string domain,
        string expected
    ) => Assert.Equal(expected, AcmeCertificateIssuer.BuildPfxFileName(domain));

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

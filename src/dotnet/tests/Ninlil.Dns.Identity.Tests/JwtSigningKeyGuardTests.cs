using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class tests <see cref="JwtSigningKeyGuard" />: the limit is counted in UTF-8 bytes, not
///     characters, and a refused key raises the exception the host turns into exit code 78.
/// </summary>
public class JwtSigningKeyGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("123456789012345")]
    public void EnsureLongEnough_AKeyUnder16Bytes_Refuses(string key)
    {
        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                JwtSigningKeyGuard.EnsureLongEnough(key)
        );

        Assert.Contains("NINLIL_JWT_SIGNING_KEY", refusal.Message);
        Assert.Contains("16", refusal.Message);
    }

    [Theory]
    [InlineData("1234567890123456")]
    [InlineData("a-key-of-more-than-sixteen-bytes")]
    public void EnsureLongEnough_AKeyOf16BytesOrMore_Passes(string key) =>
        JwtSigningKeyGuard.EnsureLongEnough(key);

    [Fact]
    public void EnsureLongEnough_CountsUtf8BytesNotCharacters()
    {
        // Eight characters, sixteen bytes in UTF-8.
        JwtSigningKeyGuard.EnsureLongEnough("éééééééé");

        // Eight characters, eight bytes.
        Assert.Throws<DatabaseStartupRefusedException>(() =>
            JwtSigningKeyGuard.EnsureLongEnough("eeeeeeee")
        );
    }
}

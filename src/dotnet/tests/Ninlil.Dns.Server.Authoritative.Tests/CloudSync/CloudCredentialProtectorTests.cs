using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Ninlil.Dns.Server.Authoritative.CloudSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CloudSync;

/// <summary>
///     This class tests the encrypt and decrypt round trip of <see cref="CloudCredentialProtector" />.
///     It uses one shared <see cref="IDataProtectionProvider" />, as a persistent key ring would;
///     two <see cref="EphemeralDataProtectionProvider" /> instances would each have a different
///     random key and could not read each other's output.
/// </summary>
public sealed class CloudCredentialProtectorTests
{
    [Fact]
    public void ProtectThenUnprotectRecoversTheOriginalCredentialFields()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        CloudCredentialProtector protector = new(provider);

        Dictionary<string, string> credentials = new()
        {
            ["ApiToken"] = "test-token-value",
            ["AccessKeyId"] = "AKIAEXAMPLE",
        };

        string encrypted = protector.Protect(credentials);
        IReadOnlyDictionary<string, string> decrypted = protector.Unprotect(encrypted);

        Assert.Equal(credentials["ApiToken"], decrypted["ApiToken"]);
        Assert.Equal(credentials["AccessKeyId"], decrypted["AccessKeyId"]);
    }

    [Fact]
    public void EncryptedPayloadIsNotThePlaintextItself()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        CloudCredentialProtector protector = new(provider);

        string encrypted = protector.Protect(
            new Dictionary<string, string> { ["ApiToken"] = "super-secret" }
        );

        Assert.DoesNotContain("super-secret", encrypted);
    }

    [Fact]
    public void TwoIndependentProviderInstancesCannotDecryptEachOthersPayloads()
    {
        CloudCredentialProtector protectorA = new(new EphemeralDataProtectionProvider());
        CloudCredentialProtector protectorB = new(new EphemeralDataProtectionProvider());

        string encrypted = protectorA.Protect(
            new Dictionary<string, string> { ["ApiToken"] = "value" }
        );

        Assert.ThrowsAny<Exception>(() => protectorB.Unprotect(encrypted));
    }
}

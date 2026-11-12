using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Web.DataProtection;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests the key ring wiring in <c>Program.cs</c> on real hosts: what the shared
///     host chose, and that a second host with a certificate configured through the
///     <c>NINLIL_DATA_PROTECTION_*</c> variables still reads a secret the shared host stored
///     before. Both hosts use the same data directory, so the second host reads the key the
///     shared host wrote.
/// </summary>
[Collection(WebTestCollection.Name)]
public sealed class KeyRingHostTests(NinlilDnsWebApplicationFactory factory) : IDisposable
{
    private readonly string _directory = KeyRingTestCertificates.NewDirectory();

    /// <summary>
    ///     This method deletes the temporary directory.
    /// </summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    ///     With no certificate configured the host uses DPAPI on Windows and no encryption
    ///     elsewhere.
    /// </summary>
    [Fact]
    public void SharedHost_WithoutACertificate_UsesThePlatformDefault()
    {
        KeyRingProtection protection = factory.Services.GetRequiredService<KeyRingProtection>();

        Assert.Equal(
            OperatingSystem.IsWindows() ? KeyRingProtectionMode.Dpapi : KeyRingProtectionMode.None,
            protection.Mode
        );
    }

    /// <summary>
    ///     A host started with a certificate reads a secret that was stored before the
    ///     certificate was configured.
    /// </summary>
    [Fact]
    public void SecondHost_WithACertificate_ReadsASecretStoredBeforeTheSwitch()
    {
        string pfx = Path.Combine(_directory, "ring.pfx");
        KeyRingTestCertificates.WriteRsaPfx(pfx, "pw");
        byte[] stored = factory
            .Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Ninlil.Web.Tests.KeyRingHost")
            .Protect([1, 2, 3, 4]);

        string pathVariable = "NINLIL_DATA_PROTECTION_CERTIFICATE_PATH";
        string passwordVariable = "NINLIL_DATA_PROTECTION_CERTIFICATE_PASSWORD";
        Environment.SetEnvironmentVariable(pathVariable, pfx);
        Environment.SetEnvironmentVariable(passwordVariable, "pw");
        try
        {
            using WebApplicationFactory<Program> second = factory.CreateSecondHost(builder =>
                builder.ConfigureTestServices(_ => { })
            );

            Assert.Equal(
                KeyRingProtectionMode.Certificate,
                second.Services.GetRequiredService<KeyRingProtection>().Mode
            );
            Assert.Equal(
                [1, 2, 3, 4],
                second
                    .Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("Ninlil.Web.Tests.KeyRingHost")
                    .Unprotect(stored)
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable(pathVariable, null);
            Environment.SetEnvironmentVariable(passwordVariable, null);
        }
    }
}

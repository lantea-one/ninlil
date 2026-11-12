using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Web.DataProtection;

// Define our namespace.
namespace Ninlil.Web.Tests.Fakes;

/// <summary>
///     This class builds Data Protection key rings over a directory the way <c>Program.cs</c>
///     does (<see cref="KeyRingProtection.Apply" /> on a file-system key ring) and protects and
///     unprotects short texts with them. A new ring over the same directory is what a restart
///     looks like.
/// </summary>
public static class KeyRingTestRings
{
    /// <summary>
    ///     This method builds a key ring over a directory the way <c>Program.cs</c> does
    ///     (<see cref="KeyRingProtection.Apply" /> on a file-system key ring) and returns its
    ///     provider and the disposable service provider behind it.
    /// </summary>
    public static ServiceProvider BuildRing(string ringDirectory, KeyRingProtection protection)
    {
        ServiceCollection services = new();
        protection.Apply(
            services
                .AddDataProtection()
                .SetApplicationName("Ninlil.Dns.KeyRingTests")
                .PersistKeysToFileSystem(new DirectoryInfo(ringDirectory))
        );
        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     This method protects a text with a new ring over the directory, which creates the
    ///     first key if the directory has none.
    /// </summary>
    public static byte[] Protect(string ringDirectory, KeyRingProtection protection, string text)
    {
        using ServiceProvider provider = BuildRing(ringDirectory, protection);
        return provider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("test")
            .Protect(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     This method adds a new default key to the ring, as key rotation does, and protects a
    ///     text with it.
    /// </summary>
    public static byte[] ProtectWithNewKey(
        string ringDirectory,
        KeyRingProtection protection,
        string text
    )
    {
        using ServiceProvider provider = BuildRing(ringDirectory, protection);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        provider.GetRequiredService<IKeyManager>().CreateNewKey(now, now.AddDays(90));
        return provider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("test")
            .Protect(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     This method unprotects a payload with a new ring over the directory.
    /// </summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The ring cannot read the key the payload names.</exception>
    public static string Unprotect(string ringDirectory, KeyRingProtection protection, byte[] data)
    {
        using ServiceProvider provider = BuildRing(ringDirectory, protection);
        return Encoding.UTF8.GetString(
            provider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("test")
                .Unprotect(data)
        );
    }
}

using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Events.ActiveMq;
using Ninlil.Dns.Server.Events.RabbitMq;
using Ninlil.Web.Configuration;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class is the read side of the secrets in <see cref="ConfigurationSecretSettings" />. At
///     startup it fills the plaintext property of each options class with the secret the
///     component should use, so the components (for example
///     <see cref="Middleware.ReverseProxyTrustGatingMiddleware" />) keep reading one plaintext
///     property and know nothing about encryption.
///     <para>
///         The order is: a non-empty plaintext value in the configuration wins (an environment
///         variable such as <c>NINLIL_DNS_SERVER_REVERSE_PROXY_SHARED_SECRET</c> is never written
///         to a file, so it is the way to supply a secret from outside); otherwise the encrypted
///         value is decrypted with this node's key ring; otherwise the class default stays. The
///         plaintext is read through <see cref="IConfiguration" /> and not from the bound options,
///         because a class default (such as a broker's <c>guest</c> user name) would otherwise
///         look like an operator's value and hide the stored one.
///     </para>
///     <para>
///         <b>An unreadable secret.</b> The key ring was replaced, or the file was copied from
///         another node. The failure is logged once as an Error that names the setting and says
///         how to recover. The server keeps running.
///         For the reverse-proxy shared secret the option is set to a fresh random value that no
///         request can present, so the secret check can never pass and no proxy is trusted: the
///         forwarded headers of every request are ignored. Falling back to no secret would instead
///         let any request from an allowlisted address through without it, which is the
///         opposite of what the operator configured.
///     </para>
///     <para>
///         <b>Broker passwords.</b> The same order applies to the RabbitMQ and ActiveMQ passwords,
///         and the class defaults (<c>guest</c>, <c>admin</c>) stay when nothing is configured. An
///         unreadable password becomes an empty password: the broker refuses the login, the
///         transport logs it and keeps retrying, and the server keeps running. A random value
///         would fail the same way and hide the cause, so the empty value is used. The broker
///         options classes live in projects that do not reference this one, so they are reached
///         only through the post-configuration below.
///     </para>
/// </summary>
public static class ConfigurationSecretResolution
{
    /// <summary>
    ///     This method registers the post-configuration of the options classes that hold a
    ///     catalogued secret. <c>Program.cs</c> calls it once.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Read for the plaintext values, so a class default does not count.</param>
    public static IServiceCollection AddNinlilConfigurationSecretResolution(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ConfigurationSecretSetting sharedSecret = ConfigurationSecretSettings.All.Single(secret =>
            secret.ProtectorPurpose == ReverseProxyOptions.SharedSecretProtectorPurpose
        );

        services
            .AddOptions<ReverseProxyOptions>()
            .PostConfigure<IDataProtectionProvider, ILoggerFactory>(
                (options, dataProtectionProvider, loggerFactory) =>
                {
                    ILogger logger = loggerFactory.CreateLogger("Ninlil.Web.ReverseProxy");

                    switch (
                        Resolve(
                            configuration,
                            sharedSecret,
                            options.SharedSecretEncrypted,
                            dataProtectionProvider,
                            logger,
                            out string plaintext
                        )
                    )
                    {
                        case SecretResolutionOutcome.Decrypted:
                            options.SharedSecret = plaintext;
                            break;
                        case SecretResolutionOutcome.Unreadable:
                            // Fail closed: a random value nobody can present, so
                            // ReverseProxyTrustGatingMiddleware trusts no proxy. An empty or
                            // missing secret would mean "the allowlist alone decides".
                            options.SharedSecret = Convert.ToBase64String(
                                RandomNumberGenerator.GetBytes(32)
                            );
                            break;
                    }
                }
            );

        AddBrokerPassword<RabbitMqEventBrokerOptions>(
            services,
            configuration,
            RabbitMqEventBrokerOptions.PasswordProtectorPurpose,
            "Ninlil.Web.EventBroker.RabbitMq",
            options => options.PasswordEncrypted,
            (options, password) => options.Password = password
        );
        AddBrokerPassword<ActiveMqEventBrokerOptions>(
            services,
            configuration,
            ActiveMqEventBrokerOptions.PasswordProtectorPurpose,
            "Ninlil.Web.EventBroker.ActiveMq",
            options => options.PasswordEncrypted,
            (options, password) => options.Password = password
        );

        return services;
    }

    /// <summary>
    ///     This method registers the post-configuration of one broker options class: it resolves
    ///     the password as described on the class and writes the result into the plaintext
    ///     property. An unreadable password becomes an empty one.
    /// </summary>
    /// <typeparam name="TOptions">The broker options class.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Read for the plaintext value, so a class default does not count.</param>
    /// <param name="protectorPurpose">Finds the catalog entry of this broker's password.</param>
    /// <param name="loggerCategory">The category of the Error logged for an unreadable password.</param>
    /// <param name="getEncrypted">Reads the bound encrypted value.</param>
    /// <param name="setPassword">Writes the resolved plaintext password.</param>
    private static void AddBrokerPassword<TOptions>(
        IServiceCollection services,
        IConfiguration configuration,
        string protectorPurpose,
        string loggerCategory,
        Func<TOptions, string> getEncrypted,
        Action<TOptions, string> setPassword
    )
        where TOptions : class
    {
        ConfigurationSecretSetting secret = ConfigurationSecretSettings.All.Single(entry =>
            entry.ProtectorPurpose == protectorPurpose
        );

        services
            .AddOptions<TOptions>()
            .PostConfigure<IDataProtectionProvider, ILoggerFactory>(
                (options, dataProtectionProvider, loggerFactory) =>
                {
                    switch (
                        Resolve(
                            configuration,
                            secret,
                            getEncrypted(options),
                            dataProtectionProvider,
                            loggerFactory.CreateLogger(loggerCategory),
                            out string plaintext
                        )
                    )
                    {
                        case SecretResolutionOutcome.Decrypted:
                            setPassword(options, plaintext);
                            break;
                        case SecretResolutionOutcome.Unreadable:
                            // An empty password: the broker refuses the login and the server
                            // keeps running. Falling back to the class default would try a
                            // well-known credential the operator did not choose.
                            setPassword(options, string.Empty);
                            break;
                    }
                }
            );
    }

    /// <summary>
    ///     This method applies the resolution order to one secret and reports what happened.
    ///     An unreadable payload is logged here, once per call.
    /// </summary>
    /// <param name="configuration">Read for the plaintext value.</param>
    /// <param name="secret">The catalog entry.</param>
    /// <param name="encryptedValue">The bound value of the encrypted key.</param>
    /// <param name="dataProtectionProvider">Supplies this node's key ring.</param>
    /// <param name="logger">Receives the Error for an unreadable payload.</param>
    /// <param name="plaintext">The decrypted secret when the outcome is <see cref="SecretResolutionOutcome.Decrypted" />.</param>
    internal static SecretResolutionOutcome Resolve(
        IConfiguration configuration,
        ConfigurationSecretSetting secret,
        string encryptedValue,
        IDataProtectionProvider dataProtectionProvider,
        ILogger logger,
        out string plaintext
    )
    {
        plaintext = null;

        if (!string.IsNullOrEmpty(configuration[secret.PlainPath]))
            return SecretResolutionOutcome.PlaintextWins;

        if (string.IsNullOrEmpty(encryptedValue))
            return SecretResolutionOutcome.NotConfigured;

        try
        {
            plaintext = ConfigurationSecretSettings.Unprotect(
                secret,
                dataProtectionProvider,
                encryptedValue
            );
            return SecretResolutionOutcome.Decrypted;
        }
        catch (CryptographicException exception)
        {
            logger.LogError(
                exception,
                "The stored value of {Setting} cannot be decrypted with this node's Data Protection "
                    + "key ring (the key ring was replaced, or the configuration file came from "
                    + "another host). Enter the value again in the settings page, or set the "
                    + "plaintext environment variable.",
                secret.DisplayName
            );
            return SecretResolutionOutcome.Unreadable;
        }
    }
}

/// <summary>
///     This enum is what <c>ConfigurationSecretResolution.Resolve</c> decided for one secret.
/// </summary>
internal enum SecretResolutionOutcome
{
    /// <summary>A non-empty plaintext value in the configuration wins; nothing to change.</summary>
    PlaintextWins,

    /// <summary>Neither a plaintext nor an encrypted value is set; the class default stays.</summary>
    NotConfigured,

    /// <summary>The encrypted value was decrypted.</summary>
    Decrypted,

    /// <summary>The encrypted value cannot be decrypted with this node's key ring.</summary>
    Unreadable,
}

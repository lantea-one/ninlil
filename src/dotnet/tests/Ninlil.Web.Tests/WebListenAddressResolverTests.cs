using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Web.Options;
using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="WebListenAddressResolver" />'s rules for the web listener: the
///     default of every address, skipping an address this host doesn't have instead of failing
///     the whole startup, and falling back to loopback -- never binding nothing -- when no
///     configured address is usable, so the admin console stays reachable from the host itself.
///     Address ownership is faked, so these tests don't depend on the machine's interfaces.
/// </summary>
public class WebListenAddressResolverTests
{
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.10");

    [Fact]
    public void NoAddressesConfigured_BindsEverywhere()
    {
        Assert.Null(Resolve("", _ => true, out _));
    }

    [Fact]
    public void TheIPv6Wildcard_BindsEverywhere_SinceKestrelsCoversIPv4Too()
    {
        Assert.Null(Resolve("127.0.0.1, ::", _ => true, out _));
    }

    [Fact]
    public void AssignedAddresses_AreBoundAsListed()
    {
        Assert.Equal(
            [Lan, IPAddress.Loopback],
            Resolve("192.168.1.10, 127.0.0.1", _ => true, out _)
        );
    }

    [Fact]
    public void AnAddressThisHostDoesNotHave_IsSkippedAndLogged()
    {
        IReadOnlyList<IPAddress> addresses = Resolve(
            "192.168.1.10, 127.0.0.1",
            address => !address.Equals(Lan),
            out ListLogger logger
        );

        Assert.Equal([IPAddress.Loopback], addresses);
        Assert.Contains(logger.Errors, message => message.Contains("192.168.1.10"));
    }

    [Fact]
    public void NoUsableAddress_FallsBackToLoopbackRatherThanLockingTheOperatorOut()
    {
        IReadOnlyList<IPAddress> addresses = Resolve(
            "192.168.1.10, not-an-address",
            address => !address.Equals(Lan),
            out ListLogger logger
        );

        Assert.Equal([IPAddress.Loopback, IPAddress.IPv6Loopback], addresses);
        Assert.Contains(logger.Errors, message => message.Contains("not-an-address"));
        Assert.Contains(logger.Errors, message => message.Contains("loopback only"));
    }

    [Fact]
    public void NoUsableAddress_WithoutIPv6_FallsBackToIPv4LoopbackAlone()
    {
        IReadOnlyList<IPAddress> addresses = Resolve(
            "192.168.1.10",
            address => address.Equals(IPAddress.Loopback),
            out _
        );

        Assert.Equal([IPAddress.Loopback], addresses);
    }

    private static IReadOnlyList<IPAddress> Resolve(
        string listenAddresses,
        Func<IPAddress, bool> isAssigned,
        out ListLogger logger
    )
    {
        logger = new ListLogger();
        return WebListenAddressResolver.Resolve(
            new WebListenOptions { ListenAddresses = listenAddresses },
            logger,
            isAssigned
        );
    }

    /// <summary>This class keeps every error-level message logged to it.</summary>
    private sealed class ListLogger : ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Acme.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class tests that the poll loop of <see cref="AcmeCertificateRenewalService" /> survives
///     a pass that throws (an account state file that cannot be read) and attempts issuance on the
///     next pass. It uses a fake store and an empty service provider, so no CA, database or host
///     is involved.
/// </summary>
public sealed class AcmeCertificateRenewalServiceLoopTests
{
    [Fact]
    public async Task ExecuteAsync_WhenTheFirstPassThrows_DoesNotFaultAndTriesAgain()
    {
        ThrowingOnceStore store = new();
        AcmeCertificateRenewalService service = new(
            new ServiceCollection()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            store,
            new StaticOptionsMonitor<AcmeOptions>(
                new AcmeOptions
                {
                    Enable = true,
                    Domains = ["example.com"],
                    PollInterval = TimeSpan.FromMilliseconds(1),
                }
            ),
            NullLogger<AcmeCertificateRenewalService>.Instance
        );

        await service.StartAsync(CancellationToken.None);

        // The second pass tries to issue; the empty provider has no issuer, so the attempt is
        // recorded as a failure. That record is what shows the pass ran.
        await store.SecondPassAttempted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(service.ExecuteTask.IsFaulted);
        Assert.False(service.ExecuteTask.IsCompleted);

        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    ///     This class is an <see cref="IAcmeAccountStore" /> whose <see cref="DomainStates" /> throws
    ///     <see cref="InvalidOperationException" /> on the first read, like
    ///     <see cref="FileAcmeAccountStore" /> does while its file cannot be read, and returns no
    ///     states afterwards. A failure recorded for the domain completes
    ///     <see cref="SecondPassAttempted" />.
    /// </summary>
    private sealed class ThrowingOnceStore : IAcmeAccountStore
    {
        private int _reads;

        /// <summary>This task completes when the renewal service records a failure for a domain.</summary>
        public TaskCompletionSource SecondPassAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public string AccountKeyPem => null;

        /// <inheritdoc />
        public IReadOnlyDictionary<string, AcmeDomainCertificateState> DomainStates =>
            Interlocked.Increment(ref _reads) == 1
                ? throw new InvalidOperationException("The state file could not be read.")
                : new Dictionary<string, AcmeDomainCertificateState>();

        /// <inheritdoc />
        public IReadOnlyDictionary<string, AcmeDomainRenewalFailure> Failures { get; } =
            new Dictionary<string, AcmeDomainRenewalFailure>();

        /// <inheritdoc />
        public void SaveAccountKey(string accountKeyPem) { }

        /// <inheritdoc />
        public void SaveDomainState(string domain, AcmeDomainCertificateState state) { }

        /// <inheritdoc />
        public void SaveDomainFailure(string domain, AcmeDomainRenewalFailure failure) =>
            SecondPassAttempted.TrySetResult();

        /// <inheritdoc />
        public void ClearDomainFailure(string domain) { }
    }
}

using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class proves, directly against the real .NET configuration/options binding pipeline
///     and a real <see cref="RecursiveQueryResolver" /> instance -- not merely
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> read in isolation, the way
///     <c>AppearanceOptionsHotReloadTests</c> proves it for <c>AppearanceOptions</c> -- that
///     flipping <see cref="RecursiveServerOptions.EnableRecursion" /> via a write
///     <see cref="PresetConfigurationFileApplier" /> merges into a JSON file (the exact mechanism
///     <see cref="Controllers.RecursiveSettingsController.Patch" /> itself calls) takes effect on
///     the <b>same, already-constructed</b> resolver instance, with no restart -- closing the real
///     functional gap a full REST/UI audit found: before this class's own hot-reload fix,
///     <see cref="RecursiveQueryResolver" /> read <see cref="RecursiveServerOptions" /> via the
///     usual, snapshotted <see cref="IOptions{TOptions}" />, which is computed once and cached for
///     the life of the process -- a write reaching the configuration file would never have been
///     seen by an already-running, singleton-registered resolver at all.
///     <para>
///         Deliberately exercised at <see cref="RecursiveQueryResolver" /> itself, not through the
///         full HTTP/DNS transport stack: a query for a name this server neither hosts nor
///         forwards for is, before the write, declined outright
///         (<see cref="DnsQueryResolution.NotHandled" />, which <c>IDnsQueryDispatcher</c> turns
///         into a REFUSED response the client actually sees); after the write, the identical
///         resolver instance genuinely reaches <see cref="IIterativeResolutionEngine.ResolveAsync(DnsName,DnsResourceRecordTypeEnum,DnsClassEnum,bool,DnsEdnsOption,CancellationToken)" />
///         and returns a real, handled response -- a SERVFAIL, since the fake engine below has no
///         real network to resolve against, but categorically different from the earlier
///         "declined entirely" outcome, and proof the toggle's own gate was actually re-evaluated
///         rather than the resolver simply always answering the same way regardless of
///         configuration.
///     </para>
/// </summary>
public class RecursiveOptionsHotReloadTests
{
    /// <summary>
    ///     This class stands in for a real <see cref="IIterativeResolutionEngine" />: since this
    ///     test never seeds it with a canned answer for the name it queries, any invocation at all
    ///     reports <see cref="RecursiveResolutionStatusEnum.ServFail" /> -- reachable proof
    ///     this engine was actually invoked, distinguishing "the resolver declined outright" from
    ///     "the resolver tried and failed."
    /// </summary>
    private sealed class UnreachableIterativeResolutionEngine : IIterativeResolutionEngine
    {
        public Task<IterativeResolutionResult> ResolveAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            DnsEdnsOption clientSubnetOption = null,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(IterativeResolutionResult.ServFail());

        public Task<IterativeResolutionResult> ResolveAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            QueryBudget budget,
            bool forceRootStart,
            DnsEdnsOption clientSubnetOption = null,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(IterativeResolutionResult.ServFail());

        public Task<IterativeResolutionResult> PrefetchAsync(
            DnsName name,
            DnsResourceRecordTypeEnum type,
            DnsClassEnum recordClass,
            bool requestDnssec,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(IterativeResolutionResult.ServFail());
    }

    /// <summary>
    ///     This class stands in for a real <see cref="IDnssecValidator" />, throwing if it is ever
    ///     actually invoked -- this test's own <see cref="RecursiveServerOptions.EnableDnssecValidation" />
    ///     is off throughout, so the resolver under test should never reach it.
    /// </summary>
    private sealed class UnreachableDnssecValidator : IDnssecValidator
    {
        public Task<DnssecValidationResult> ValidateAsync(
            DnsName queriedName,
            DnsResourceRecordTypeEnum queriedType,
            DnsClassEnum recordClass,
            IterativeResolutionResult result,
            QueryBudget budget,
            CancellationToken stoppingToken = default
        ) =>
            throw new InvalidOperationException(
                "EnableDnssecValidation is off throughout this test; the validator should never be reached."
            );
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_MakesTheSameResolverInstanceAttemptRecursionWithNoRestart()
    {
        // A directory of this test's own, rather than the shared temp directory itself: the file
        // watcher behind reloadOnChange sees every change in the directory it watches, and on a
        // busy machine unrelated churn there can overflow its queue, which makes it report a
        // change to every watched file at once.
        string directory = Directory
            .CreateTempSubdirectory("ninlil-dns-recursive-hot-reload-")
            .FullName;
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
            // appsettings.json in a real deployment.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<RecursiveServerOptions>(
                configuration.GetSection(RecursiveServerOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<RecursiveServerOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<RecursiveServerOptions>
            >();

            // Before any write, the file does not exist yet -- this class's own compiled-in
            // default, exactly as a fresh installation would see it: recursion off.
            Assert.False(monitor.CurrentValue.EnableRecursion);

            // Constructed once, exactly as the real DI container constructs it as a singleton --
            // never rebuilt for the rest of this test.
            RecursiveQueryResolver resolver = new(
                new UnreachableIterativeResolutionEngine(),
                new UnreachableDnssecValidator(),
                monitor
            );

            DnsQueryContext BuildContext() =>
                new(
                    new DnsMessage
                    {
                        Id = 1,
                        Opcode = DnsOpcodeEnum.Query,
                        RecursionDesired = true,
                        Questions =
                        {
                            new DnsQuestion(
                                DnsName.Parse("example.invalid."),
                                DnsResourceRecordTypeEnum.A,
                                DnsClassEnum.Internet
                            ),
                        },
                    },
                    DnsTransportProtocolEnum.Udp,
                    IPAddress.Loopback,
                    DateTimeOffset.UtcNow
                );

            DnsQueryResolution before = await resolver.TryResolveAsync(BuildContext());
            Assert.False(before.IsHandled);

            // The exact same write mechanism RecursiveSettingsController.Patch itself calls.
            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        RecursiveServerOptions.SectionName,
                        nameof(RecursiveServerOptions.EnableRecursion),
                        true
                    ),
                ],
                path
            );

            // .NET's own JSON configuration provider debounces file-change notifications by a
            // few hundred milliseconds -- this waits (up to ten seconds) for the written value to
            // actually arrive, rather than for the first change notification: a spurious
            // notification can trigger an earlier reload that still reads the old file.
            for (int attempt = 0; attempt < 200 && !monitor.CurrentValue.EnableRecursion; attempt++)
                await Task.Delay(50);
            Assert.True(monitor.CurrentValue.EnableRecursion);

            // The identical resolver instance, constructed before the write, now genuinely
            // attempts resolution -- reaching the fake engine (which reports ServerFailure, since
            // it was never seeded with a canned answer) -- instead of declining outright. This is
            // the "REFUSED vs. a genuine resolution attempt" behavioral proof: no restart, no new
            // resolver instance, just the live configuration write actually taking effect.
            DnsQueryResolution after = await resolver.TryResolveAsync(BuildContext());
            Assert.True(after.IsHandled);
            Assert.Equal(DnsResponseCodeEnum.ServerFailure, after.Response.ResponseCode);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
            }
        }
    }
}

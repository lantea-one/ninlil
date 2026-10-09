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
///     This class checks that turning on <see cref="RecursiveServerOptions.EnableRecursion" /> with
///     a write that <see cref="PresetConfigurationFileApplier" /> merges into a JSON file (the call
///     <see cref="Controllers.RecursiveSettingsController.Patch" /> makes) takes effect on a
///     <see cref="RecursiveQueryResolver" /> constructed before the write, with no restart. Unlike
///     <c>AppearanceOptionsHotReloadTests</c>, it checks the resolver's behavior and not only
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" />. The resolver must read the options
///     through <see cref="IOptionsMonitor{TOptions}" />; with <see cref="IOptions{TOptions}" />,
///     which is computed once, the singleton resolver would never see the write.
///     <para>
///         The test calls the resolver directly, with a fake iterative engine and validator, not
///         through the DNS transports. Before the write, a query for a name the server neither
///         hosts nor forwards is declined (<see cref="DnsQueryResolution.NotHandled" />, which
///         <c>IDnsQueryDispatcher</c> turns into REFUSED). After it, the same resolver calls
///         <see cref="IIterativeResolutionEngine.ResolveAsync(DnsName,DnsResourceRecordTypeEnum,DnsClassEnum,bool,DnsEdnsOption,CancellationToken)" />
///         and returns a handled SERVFAIL from the fake engine, which shows the switch was read
///         again.
///     </para>
/// </summary>
public class RecursiveOptionsHotReloadTests
{
    /// <summary>
    ///     This class is a fake <see cref="IIterativeResolutionEngine" /> that answers every call
    ///     with <see cref="RecursiveResolutionStatusEnum.ServFail" />, so a SERVFAIL shows the
    ///     resolver tried to resolve instead of declining.
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
    ///     This class is a fake <see cref="IDnssecValidator" /> that throws if called, since
    ///     <see cref="RecursiveServerOptions.EnableDnssecValidation" /> is off throughout the test.
    /// </summary>
    private sealed class UnreachableDnssecValidator : IDnssecValidator
    {
        public Task<DnssecValidationResult> ValidateAsync(
            DnsName queriedName,
            DnsResourceRecordTypeEnum queriedType,
            DnsClassEnum recordClass,
            IterativeResolutionResult result,
            QueryBudget budget,
            CancellationToken stoppingToken = default,
            IDnssecRecordFetcher fetcher = null
        ) =>
            throw new InvalidOperationException(
                "EnableDnssecValidation is off throughout this test; the validator should never be reached."
            );
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_MakesTheSameResolverInstanceAttemptRecursionWithNoRestart()
    {
        // A directory for this test rather than the shared temp directory (see
        // QueryLoggingOptionsHotReloadTests for why).
        string directory = Directory
            .CreateTempSubdirectory("ninlil-dns-recursive-hot-reload-")
            .FullName;
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            // A reload-on-change JSON file source, the kind WebApplicationBuilder.CreateBuilder()
            // adds for appsettings.json.
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

            // The file does not exist yet, so this is the compiled-in default: recursion off.
            Assert.False(monitor.CurrentValue.EnableRecursion);

            // Constructed once, as the singleton is, and never rebuilt during the test.
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

            // The same call RecursiveSettingsController.Patch makes.
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

            // Wait up to ten seconds for the written value itself rather than the first change
            // notification, which can be spurious (see QueryLoggingOptionsHotReloadTests).
            for (int attempt = 0; attempt < 200 && !monitor.CurrentValue.EnableRecursion; attempt++)
                await Task.Delay(50);
            Assert.True(monitor.CurrentValue.EnableRecursion);

            // The resolver built before the write now tries to resolve, reaching the fake engine
            // (which answers ServerFailure), instead of declining.
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
                // Best-effort cleanup; a leftover temporary file is harmless.
            }
        }
    }
}

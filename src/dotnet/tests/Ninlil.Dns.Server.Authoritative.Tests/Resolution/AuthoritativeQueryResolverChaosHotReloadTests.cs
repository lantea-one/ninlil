using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Authoritative.Resolution;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Resolution;

/// <summary>
///     This class proves, against the real .NET configuration/options binding pipeline and a real
///     reload-on-change JSON configuration file -- not merely
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> read in isolation -- that a write
///     reaching <see cref="AuthoritativeServerOptions" /> via
///     <see cref="Server.Presets.PresetConfigurationFileApplier" /> (the exact mechanism
///     <c>Ninlil.Web.Controllers.BrandingSettingsController.Patch</c> itself calls) is
///     honored by the <b>same, already-constructed</b> <see cref="AuthoritativeQueryResolver" />
///     instance on its very next CHAOS-class query, with no restart.
///     <para>
///         Before this fix, <see cref="AuthoritativeQueryResolver" /> -- registered
///         as a singleton, per <c>ServiceCollectionAuthoritativeExtension</c> -- read
///         <see cref="AuthoritativeServerOptions" /> via the usual, snapshotted
///         <see cref="IOptions{TOptions}" />, computed once and cached for the life of the process
///         -- a write reaching the configuration file would never have been observed by an
///         already-running instance at all, the same functional gap this project's other
///         options-bound classes each already found and fixed in their own turn. Fixed by
///         converting the resolver's own constructor parameter to
///         <see cref="IOptionsMonitor{TOptions}" />.
///     </para>
/// </summary>
public sealed class AuthoritativeQueryResolverChaosHotReloadTests
{
    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_MakesTheSameResolverInstanceAnswerChaosQueries_WithNoRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-authoritative-chaos-hot-reload-{Guid.NewGuid():N}.json"
        );

        try
        {
            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
            // appsettings.json in a real deployment.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<AuthoritativeServerOptions>(
                configuration.GetSection(AuthoritativeServerOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<AuthoritativeServerOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<AuthoritativeServerOptions>
            >();

            // Before any write, CHAOS queries are off -- this class's own compiled-in default.
            Assert.False(monitor.CurrentValue.EnableChaosVersionQueries);

            // AuthoritativeZoneCache/SignedZoneCache need only an IServiceScopeFactory (and, for
            // the latter, a logger) at construction -- neither is ever started here, since a
            // CHAOS-class query never touches zone data at all.
            IServiceScopeFactory emptyScopeFactory = new ServiceCollection()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();
            AuthoritativeZoneCache zoneCache = new(emptyScopeFactory);
            SignedZoneCache signedZoneCache = new(
                emptyScopeFactory,
                NullLogger<SignedZoneCache>.Instance
            );

            AuthoritativeQueryResolver resolver = new(zoneCache, signedZoneCache, monitor);

            DnsQueryContext BuildChaosQuery(DnsResourceRecordTypeEnum type) =>
                new(
                    new DnsMessage
                    {
                        Id = 1,
                        Questions =
                        {
                            new DnsQuestion(
                                DnsName.Parse("hostname.bind."),
                                type,
                                DnsClassEnum.Chaos
                            ),
                        },
                    },
                    DnsTransportProtocolEnum.Udp,
                    IPAddress.Loopback,
                    DateTimeOffset.UtcNow
                );

            // Before the write, CHAOS queries are declined outright -- left for
            // IDnsQueryDispatcher to fall through, which (with no other resolver configured to
            // answer CHAOS) ultimately means REFUSED.
            DnsQueryResolution before = await resolver.TryResolveAsync(
                BuildChaosQuery(DnsResourceRecordTypeEnum.Txt)
            );
            Assert.False(before.IsHandled);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            // The exact same write mechanism BrandingSettingsController.Patch itself calls.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        AuthoritativeServerOptions.SectionName,
                        nameof(AuthoritativeServerOptions.EnableChaosVersionQueries),
                        true
                    ),
                    new Server.Presets.PresetConfigurationSetting(
                        AuthoritativeServerOptions.SectionName,
                        nameof(AuthoritativeServerOptions.HostnameString),
                        "acme-ns1"
                    ),
                ],
                path
            );

            // .NET's own JSON configuration provider debounces file-change notifications by a
            // few hundred milliseconds -- this waits for that real reload to actually happen
            // rather than asserting against a fixed delay.
            Task completed = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, completed);
            Assert.True(monitor.CurrentValue.EnableChaosVersionQueries);

            // The identical resolver instance, constructed before the write, now genuinely
            // answers CHAOS queries -- proof the value was actually re-read on this call rather
            // than captured once at construction.
            DnsQueryResolution afterHostname = await resolver.TryResolveAsync(
                BuildChaosQuery(DnsResourceRecordTypeEnum.Txt)
            );
            Assert.True(afterHostname.IsHandled);
            DnsResourceRecord answer = Assert.Single(afterHostname.Response.Answers);
            Assert.Equal(DnsResourceRecordTypeEnum.Txt, answer.Type);
            Assert.Equal(DnsClassEnum.Chaos, answer.Class);

            TxtRecordData txtData = (TxtRecordData)
                DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Txt, answer.Data);
            Assert.Equal("acme-ns1", Assert.Single(txtData.Values));
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
            }
        }
    }
}

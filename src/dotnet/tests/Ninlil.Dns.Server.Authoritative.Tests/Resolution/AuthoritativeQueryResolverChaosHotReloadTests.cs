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
///     This class checks that a change to <see cref="AuthoritativeServerOptions" /> written through
///     <see cref="Server.Presets.PresetConfigurationFileApplier" /> (what
///     <c>Ninlil.Web.Controllers.BrandingSettingsController.Patch</c> calls) into a reload-on-change
///     JSON file is used by the same, singleton <see cref="AuthoritativeQueryResolver" /> on its next
///     CHAOS query, without a restart. The resolver must read the options through
///     <see cref="IOptionsMonitor{TOptions}" />; an <see cref="IOptions{TOptions}" /> value is cached
///     for the life of the process.
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
            // A reload-on-change JSON file source, the same kind the web host uses for its
            // configuration files.
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

            // Before any write, CHAOS queries are off -- this class's compiled-in default.
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

            // The same write BrandingSettingsController.Patch makes.
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

            // The JSON provider debounces file-change notifications, so wait for the reload event
            // rather than a fixed delay.
            Task completed = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, completed);
            Assert.True(monitor.CurrentValue.EnableChaosVersionQueries);

            // The same resolver, built before the write, now answers CHAOS queries, so the options are
            // re-read per query.
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
                // Best-effort cleanup; a leftover temp file is harmless.
            }
        }
    }
}

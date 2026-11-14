using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Tsig;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class measures what the pipeline does for a TSIG-signed request:
///     <see cref="IDnsTransactionSignatureService.VerifyAsync" /> on the request and
///     <see cref="IDnsTransactionSignatureService.SignAsync" /> on the reply, through the real
///     <see cref="ITsigService" /> and a TSIG key stored in the database. The key is added in
///     setup and removed in cleanup. The memory column is the number to read: a message needs no
///     database command and no service scope, so the column shows only the work on the message.
/// </summary>
[Config(typeof(StorageConfig))]
public class TsigSignatureBenchmarks : DnsPathBenchmarkBase
{
    private const string KeyName = "bench-tsig-key";

    private IDnsTransactionSignatureService _signatures;
    private DnsQueryContext _context;
    private DnsTransactionSignature _signature;
    private DnsMessage _response;
    private Guid _keyId;

    /// <inheritdoc />
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    )
    {
        base.ConfigureServices(services, configuration);
        services.AddNinlilDnsServerTsig(configuration);
    }

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        TsigSecretProtector protector = Database.Services.GetRequiredService<TsigSecretProtector>();

        await using (AsyncServiceScope scope = Database.Services.CreateAsyncScope())
        {
            ITsigKeyRepository keys =
                scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();

            // A key left by a run that did not clean up is replaced.
            TsigKeyEntity old = await keys.FindByNameAsync(KeyName);
            if (old is not null)
                keys.Remove(old);

            _keyId = Guid.NewGuid();
            await keys.AddAsync(
                new TsigKeyEntity
                {
                    Id = _keyId,
                    Name = KeyName,
                    AlgorithmName = "hmac-sha256.",
                    SecretEncrypted = protector.Protect(new byte[32]),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        _signatures = Database.Services.GetRequiredService<IDnsTransactionSignatureService>();

        DnsMessage query = new()
        {
            Id = 7,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("www.example.com"),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };
        DnsMessage signed = await InScopeAsync(async services =>
            await services.GetRequiredService<ITsigService>().SignAsync(query, KeyName, query.Id)
        );
        byte[] raw = DnsMessageWriter.Write(signed);
        DnsMessage request = DnsMessageReader.Read(raw, out int tsigOffset);

        _context = new DnsQueryContext(
            request,
            DnsTransportProtocolEnum.Udp,
            IPAddress.Loopback,
            DateTimeOffset.UtcNow,
            raw,
            tsigOffset
        );
        _response = new DnsMessage
        {
            Id = request.Id,
            IsResponse = true,
            Questions = [.. request.Questions],
        };

        _signature = await _signatures.VerifyAsync(_context);
        Expect(_signature.IsVerified, "the signed request verifies against the stored key");

        DnsMessage reply = await _signatures.SignAsync(_response, _signature);
        Expect(reply.Tsig is not null, "the reply carries a TSIG record");
    }

    /// <inheritdoc />
    protected override async Task OnCleanupAsync()
    {
        await using AsyncServiceScope scope = Database.Services.CreateAsyncScope();
        ITsigKeyRepository keys = scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        TsigKeyEntity key = await keys.GetByIdAsync(_keyId);
        if (key is not null)
        {
            keys.Remove(key);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
    }

    /// <summary>A signed request: the key lookup and the MAC check.</summary>
    [Benchmark]
    public async Task<bool> VerifySignedRequest() =>
        (await _signatures.VerifyAsync(_context)).IsVerified;

    /// <summary>The reply to a verified request: the key lookup and the MAC.</summary>
    [Benchmark]
    public async Task<bool> SignReply() =>
        (await _signatures.SignAsync(_response, _signature)).Tsig is not null;
}

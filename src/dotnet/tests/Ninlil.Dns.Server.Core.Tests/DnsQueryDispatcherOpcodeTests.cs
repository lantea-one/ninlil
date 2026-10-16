using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests the opcode check of <see cref="DnsQueryDispatcher" />: opcodes the server
///     does not implement get NOTIMP before any resolver runs, and QUERY, NOTIFY and UPDATE still
///     reach the resolvers. It calls the dispatcher directly with a recording resolver.
/// </summary>
public sealed class DnsQueryDispatcherOpcodeTests
{
    /// <summary>
    ///     This fake resolver claims every query and counts the calls it gets.
    /// </summary>
    private sealed class RecordingResolver : IDnsQueryResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            CallCount++;
            return ValueTask.FromResult(
                DnsQueryResolution.Handled(
                    new DnsMessage
                    {
                        Id = context.Request.Id,
                        IsResponse = true,
                        Opcode = context.Request.Opcode,
                        ResponseCode = DnsResponseCodeEnum.NoError,
                    }
                )
            );
        }
    }

    /// <summary>
    ///     This fake publisher drops every event.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    private static DnsQueryContext BuildContext(DnsOpcodeEnum opcode, bool withQuestion = true)
    {
        DnsMessage request = new() { Id = 7, Opcode = opcode };

        if (withQuestion)
            request.Questions.Add(
                new DnsQuestion(
                    DnsName.Parse("example.com."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                )
            );

        return new DnsQueryContext(
            request,
            DnsTransportProtocolEnum.Udp,
            IPAddress.Loopback,
            DateTimeOffset.UtcNow
        );
    }

    [Theory]
    [InlineData(DnsOpcodeEnum.InverseQuery)]
    [InlineData(DnsOpcodeEnum.Status)]
    [InlineData((DnsOpcodeEnum)3)]
    [InlineData((DnsOpcodeEnum)6)]
    public async Task DispatchAsync_ForAnUnsupportedOpcode_AnswersNotImplementedWithoutCallingAResolver(
        DnsOpcodeEnum opcode
    )
    {
        RecordingResolver resolver = new();
        DnsQueryDispatcher dispatcher = new([resolver], new NoOpEventPublisher());

        DnsMessage response = await dispatcher.DispatchAsync(BuildContext(opcode));

        Assert.Equal(DnsResponseCodeEnum.NotImplemented, response.ResponseCode);
        Assert.Equal(opcode, response.Opcode);
        Assert.Equal(7, response.Id);
        Assert.Single(response.Questions);
        Assert.Equal(0, resolver.CallCount);
    }

    [Theory]
    [InlineData(DnsOpcodeEnum.Query)]
    [InlineData(DnsOpcodeEnum.Notify)]
    [InlineData(DnsOpcodeEnum.Update)]
    public async Task DispatchAsync_ForASupportedOpcode_OffersTheQueryToTheResolvers(
        DnsOpcodeEnum opcode
    )
    {
        RecordingResolver resolver = new();
        DnsQueryDispatcher dispatcher = new([resolver], new NoOpEventPublisher());

        DnsMessage response = await dispatcher.DispatchAsync(BuildContext(opcode));

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public async Task DispatchAsync_ForAnInverseQueryWithNoQuestion_AnswersFormatError()
    {
        RecordingResolver resolver = new();
        DnsQueryDispatcher dispatcher = new([resolver], new NoOpEventPublisher());

        DnsMessage response = await dispatcher.DispatchAsync(
            BuildContext(DnsOpcodeEnum.InverseQuery, withQuestion: false)
        );

        Assert.Equal(DnsResponseCodeEnum.FormatError, response.ResponseCode);
        Assert.Equal(0, resolver.CallCount);
    }
}

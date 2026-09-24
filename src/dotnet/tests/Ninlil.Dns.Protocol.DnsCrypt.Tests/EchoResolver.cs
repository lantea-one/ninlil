using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class is a minimal <see cref="IDnsQueryResolver" /> test double: it answers any A
///     query for <see cref="QuestionName" /> with a single, fixed A record, and declines every
///     other query -- enough for this suite to prove a query really made it through the DNSCrypt
///     listener, the shared pipeline, and back out again, without needing a real zone/database.
/// </summary>
internal sealed class EchoResolver : IDnsQueryResolver
{
    /// <summary>
    ///     This constant contains the one name this resolver answers.
    /// </summary>
    public static readonly DnsName QuestionName = DnsName.Parse("query.example.test.");

    /// <summary>
    ///     This constant contains the fixed IPv4 address <see cref="QuestionName" /> resolves to.
    /// </summary>
    public static readonly byte[] AnswerAddress = [203, 0, 113, 42];

    /// <inheritdoc />
    public ValueTask<DnsQueryResolution> TryResolveAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    )
    {
        DnsQuestion question =
            context.Request.Questions.Count > 0 ? context.Request.Questions[0] : null;

        if (
            question is null
            || question.Type != DnsResourceRecordTypeEnum.A
            || question.Name != QuestionName
        )
            return ValueTask.FromResult(DnsQueryResolution.NotHandled);

        DnsMessage response = new()
        {
            Id = context.Request.Id,
            IsResponse = true,
            Opcode = DnsOpcodeEnum.Query,
            IsAuthoritativeAnswer = true,
            RecursionAvailable = false,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };
        response.Questions.Add(question);
        response.Answers.Add(
            new DnsResourceRecord(
                question.Name,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                60,
                AnswerAddress
            )
        );

        return ValueTask.FromResult(DnsQueryResolution.Handled(response));
    }
}

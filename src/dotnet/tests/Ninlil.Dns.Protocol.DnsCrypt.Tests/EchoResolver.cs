using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class is a fake <see cref="IDnsQueryResolver" />: it answers an A query for
///     <see cref="QuestionName" /> with one fixed A record and declines every other query. That
///     is enough to show a query went through the DNSCrypt listener and the pipeline and back,
///     without a zone or a database. Two more names, <see cref="MediumQuestionName" /> and
///     <see cref="LargeQuestionName" />, answer with many A records, so the size-limit tests
///     can ask for an answer of a known size.
/// </summary>
internal sealed class EchoResolver : IDnsQueryResolver
{
    /// <summary>
    ///     This constant contains the one name this resolver answers.
    /// </summary>
    public static readonly DnsName QuestionName = DnsName.Parse("query.example.test.");

    /// <summary>
    ///     This constant contains a name whose A answer holds 50 records, about 850 bytes: more
    ///     than 512 but less than the default UDP payload limit of 1232.
    /// </summary>
    public static readonly DnsName MediumQuestionName = DnsName.Parse("medium.example.test.");

    /// <summary>
    ///     This constant contains a name whose A answer holds 150 records, about 2.5 KB: more
    ///     than any default UDP limit.
    /// </summary>
    public static readonly DnsName LargeQuestionName = DnsName.Parse("large.example.test.");

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

        if (question is null || question.Type != DnsResourceRecordTypeEnum.A)
            return ValueTask.FromResult(DnsQueryResolution.NotHandled);

        int recordCount;

        if (question.Name == QuestionName)
            recordCount = 1;
        else if (question.Name == MediumQuestionName)
            recordCount = 50;
        else if (question.Name == LargeQuestionName)
            recordCount = 150;
        else
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

        for (int index = 0; index < recordCount; index++)
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

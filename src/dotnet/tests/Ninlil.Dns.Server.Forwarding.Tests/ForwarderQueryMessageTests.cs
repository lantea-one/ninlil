using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Forwarding.Clients;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests <see cref="ForwarderQueryMessage" /> on plain in-memory messages: that
///     <see cref="ForwarderQueryMessage.WithId" /> copies without touching its input, and that
///     <see cref="ForwarderQueryMessage.IsReplyTo" /> compares the question section the way
///     RFC 5452 §9.1 and RFC 1035 §3.1 ask.
/// </summary>
public class ForwarderQueryMessageTests
{
    /// <summary>
    ///     This method builds a one-question query for the given name and type.
    /// </summary>
    private static DnsMessage Query(
        string name,
        DnsResourceRecordTypeEnum type = DnsResourceRecordTypeEnum.A,
        DnsClassEnum questionClass = DnsClassEnum.Internet
    )
    {
        DnsMessage message = new()
        {
            Id = 0x1234,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            CheckingDisabled = true,
        };
        message.Questions.Add(new DnsQuestion(DnsName.Parse(name), type, questionClass));
        return message;
    }

    [Fact]
    public void WithId_Always_ReturnsACopyWithTheNewIdAndLeavesTheInputAlone()
    {
        DnsMessage original = Query("www.example.com");

        DnsMessage copy = ForwarderQueryMessage.WithId(original, 0xBEEF);

        Assert.NotSame(original, copy);
        Assert.Equal(0xBEEF, copy.Id);
        Assert.Equal(0x1234, original.Id);
        Assert.True(copy.RecursionDesired);
        Assert.True(copy.CheckingDisabled);
        Assert.Equal(original.Questions, copy.Questions);

        // Changing the copy's lists must not reach the original.
        copy.Questions.Clear();
        Assert.Single(original.Questions);
    }

    [Fact]
    public void IsReplyTo_ForTheSameQuestionInAnotherCase_IsTrue()
    {
        Assert.True(
            ForwarderQueryMessage.IsReplyTo(Query("www.example.com"), Query("WWW.Example.COM"))
        );
    }

    [Fact]
    public void IsReplyTo_ForAnotherName_IsFalse()
    {
        Assert.False(
            ForwarderQueryMessage.IsReplyTo(Query("www.example.com"), Query("evil.example"))
        );
    }

    [Fact]
    public void IsReplyTo_ForAnotherType_IsFalse()
    {
        Assert.False(
            ForwarderQueryMessage.IsReplyTo(
                Query("www.example.com"),
                Query("www.example.com", DnsResourceRecordTypeEnum.Aaaa)
            )
        );
    }

    [Fact]
    public void IsReplyTo_ForAnotherClass_IsFalse()
    {
        Assert.False(
            ForwarderQueryMessage.IsReplyTo(
                Query("www.example.com"),
                Query("www.example.com", questionClass: DnsClassEnum.Chaos)
            )
        );
    }

    [Fact]
    public void IsReplyTo_ForADifferentQuestionCount_IsFalse()
    {
        DnsMessage noQuestion = Query("www.example.com");
        noQuestion.Questions.Clear();
        DnsMessage twoQuestions = Query("www.example.com");
        twoQuestions.Questions.Add(twoQuestions.Questions[0]);

        Assert.False(ForwarderQueryMessage.IsReplyTo(Query("www.example.com"), noQuestion));
        Assert.False(ForwarderQueryMessage.IsReplyTo(Query("www.example.com"), twoQuestions));
    }

    [Fact]
    public void NewRandomId_CalledRepeatedly_DoesNotReturnOneValueOnly()
    {
        HashSet<ushort> ids = [];

        for (int i = 0; i < 64; i++)
            ids.Add(ForwarderQueryMessage.NewRandomId());

        Assert.True(ids.Count > 1);
    }
}

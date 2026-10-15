using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests <see cref="ZoneTransferResponseAssembler" /> directly, with no sockets: it
///     feeds hand-built answer records message by message and checks when the transfer is complete,
///     which form it takes (full, incremental, "up to date") and which malformed answers fail.
/// </summary>
public class ZoneTransferResponseAssemblerTests
{
    private static readonly DnsName Zone = DnsName.Parse("example.test.");

    private static DnsResourceRecord Soa(uint serial) =>
        new(
            Zone,
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                new SoaRecordData(DnsName.Root, DnsName.Root, serial, 0, 0, 0, 0)
            )
        );

    private static DnsResourceRecord A(string name, string address) =>
        new(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(IPAddress.Parse(address))
            )
        );

    private static DnsResourceRecord Garbage() =>
        new(Zone, DnsResourceRecordTypeEnum.Soa, DnsClassEnum.Internet, 3600, [1, 2, 3]);

    [Fact]
    public void AddMessage_AFullAnswerInOneMessage_CompletesWithoutTheClosingSoa()
    {
        ZoneTransferResponseAssembler assembler = new(null);

        string failure = assembler.AddMessage([
            Soa(5),
            A("a.example.test.", "192.0.2.1"),
            A("b.example.test.", "192.0.2.2"),
            Soa(5),
        ]);

        Assert.Null(failure);
        Assert.True(assembler.IsComplete);
        ZoneTransferPullResult result = assembler.BuildResult();
        Assert.True(result.Succeeded);
        Assert.False(result.IsIncremental);
        Assert.Equal(5u, result.FinalSerial);
        Assert.Equal(3, result.FullRecordSet.Count);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, result.FullRecordSet[0].Type);
    }

    [Fact]
    public void AddMessage_AFullAnswerOverSeveralMessages_CompletesOnlyAtTheClosingSoa()
    {
        ZoneTransferResponseAssembler assembler = new(null);

        Assert.Null(assembler.AddMessage([Soa(5), A("a.example.test.", "192.0.2.1")]));
        Assert.False(assembler.IsComplete);
        Assert.Null(assembler.AddMessage([A("b.example.test.", "192.0.2.2")]));
        Assert.False(assembler.IsComplete);
        Assert.Null(assembler.AddMessage([Soa(5)]));

        Assert.True(assembler.IsComplete);
        Assert.Equal(3, assembler.BuildResult().FullRecordSet.Count);
    }

    [Fact]
    public void AddMessage_ASoaOnlyZoneAxfr_CompletesWithTheLeadingSoaAlone()
    {
        ZoneTransferResponseAssembler assembler = new(null);

        Assert.Null(assembler.AddMessage([Soa(5), Soa(5)]));

        Assert.True(assembler.IsComplete);
        Assert.Single(assembler.BuildResult().FullRecordSet);
    }

    [Fact]
    public void AddMessage_AnAnswerNotStartingWithAnSoa_Fails()
    {
        ZoneTransferResponseAssembler assembler = new(null);

        string failure = assembler.AddMessage([A("a.example.test.", "192.0.2.1"), Soa(5)]);

        Assert.Contains("did not begin with an SOA", failure);
        Assert.False(assembler.IsComplete);
    }

    [Fact]
    public void AddMessage_ALeadingSoaThatDoesNotDecode_Fails()
    {
        ZoneTransferResponseAssembler assembler = new(null);

        string failure = assembler.AddMessage([Garbage()]);

        Assert.Contains("could not be decoded", failure);
    }

    [Fact]
    public void AddMessage_ASingleSoaEqualToTheRequestedSerial_IsAlreadyCurrent()
    {
        ZoneTransferResponseAssembler assembler = new(10);

        Assert.Null(assembler.AddMessage([Soa(10)]));

        Assert.True(assembler.IsComplete);
        ZoneTransferPullResult result = assembler.BuildResult();
        Assert.True(result.IsAlreadyCurrent);
        Assert.Equal(10u, result.FinalSerial);
    }

    [Fact]
    public void AddMessage_ASingleSoaOlderThanTheRequestedSerial_IsAlreadyCurrent()
    {
        ZoneTransferResponseAssembler assembler = new(10);

        Assert.Null(assembler.AddMessage([Soa(9)]));

        Assert.True(assembler.IsComplete);
        Assert.True(assembler.BuildResult().IsAlreadyCurrent);
    }

    [Fact]
    public void AddMessage_ASingleSoaNewerThanTheRequestedSerial_KeepsReading()
    {
        ZoneTransferResponseAssembler assembler = new(10);

        Assert.Null(assembler.AddMessage([Soa(12)]));

        Assert.False(assembler.IsComplete);
        Assert.False(assembler.BuildResult().Succeeded);
    }

    [Fact]
    public void AddMessage_ANewerSingleSoaFollowedByARecordMessage_ContinuesAsAFullAnswer()
    {
        ZoneTransferResponseAssembler assembler = new(10);

        Assert.Null(assembler.AddMessage([Soa(12)]));
        Assert.Null(assembler.AddMessage([A("a.example.test.", "192.0.2.1"), Soa(12)]));

        Assert.True(assembler.IsComplete);
        Assert.Equal(2, assembler.BuildResult().FullRecordSet.Count);
    }

    [Fact]
    public void AddMessage_ASingleSoaForAnAxfr_DoesNotCompleteAndDoesNotBuildAResult()
    {
        ZoneTransferResponseAssembler assembler = new(null);

        Assert.Null(assembler.AddMessage([Soa(5)]));

        Assert.False(assembler.IsComplete);
        Assert.False(assembler.BuildResult().Succeeded);
    }

    [Fact]
    public void AddMessage_AnIncrementalAnswer_YieldsItsStepsWithoutTheClosingSoa()
    {
        ZoneTransferResponseAssembler assembler = new(10);

        string failure = assembler.AddMessage([
            Soa(12),
            Soa(10),
            A("removed.example.test.", "192.0.2.9"),
            Soa(11),
            A("added1.example.test.", "192.0.2.10"),
            Soa(11),
            Soa(12),
            A("added2.example.test.", "192.0.2.11"),
            Soa(12),
        ]);

        Assert.Null(failure);
        Assert.True(assembler.IsComplete);
        ZoneTransferPullResult result = assembler.BuildResult();
        Assert.True(result.IsIncremental);
        Assert.Equal(12u, result.FinalSerial);
        Assert.Equal(2, result.DiffSteps.Count);
        Assert.Equal(10u, result.DiffSteps[0].OldSerial);
        Assert.Equal(11u, result.DiffSteps[0].NewSerial);
        Assert.Single(result.DiffSteps[0].Removed);
        Assert.Single(result.DiffSteps[0].Added);
        Assert.Equal(11u, result.DiffSteps[1].OldSerial);
        Assert.Equal(12u, result.DiffSteps[1].NewSerial);
        Assert.Empty(result.DiffSteps[1].Removed);
        Assert.Single(result.DiffSteps[1].Added);
    }

    [Fact]
    public void AddMessage_AnIncrementalStepThatOnlyRemoves_EndsAtTheClosingSoaNotTheStepSoa()
    {
        ZoneTransferResponseAssembler assembler = new(10);

        // The step's new SOA (12) and the closing SOA (12) are equal; the position tells them apart.
        Assert.Null(
            assembler.AddMessage([
                Soa(12),
                Soa(10),
                A("gone.example.test.", "192.0.2.9"),
                Soa(12),
                Soa(12),
            ])
        );

        Assert.True(assembler.IsComplete);
        ZoneTransferDiffStep step = Assert.Single(assembler.BuildResult().DiffSteps);
        Assert.Single(step.Removed);
        Assert.Empty(step.Added);
    }

    [Fact]
    public void AddMessage_AnIncrementalStep_KeepsTheStepsNewSoaRecord()
    {
        ZoneTransferResponseAssembler assembler = new(10);
        DnsResourceRecord newSoa = Soa(11);

        // Leading SOA 11, then the step: old SOA 10, removed record, new SOA 11, added record, closing SOA 11.
        Assert.Null(
            assembler.AddMessage([
                Soa(11),
                Soa(10),
                A("gone.example.test.", "192.0.2.9"),
                newSoa,
                A("new.example.test.", "192.0.2.10"),
                Soa(11),
            ])
        );

        ZoneTransferDiffStep step = Assert.Single(assembler.BuildResult().DiffSteps);
        Assert.Equal(newSoa, step.NewSoa);
    }

    [Fact]
    public void AddMessage_AnIncrementalAnswerOverSeveralMessages_CompletesAtTheLastOne()
    {
        ZoneTransferResponseAssembler assembler = new(10);

        Assert.Null(assembler.AddMessage([Soa(12), Soa(10)]));
        Assert.Null(assembler.AddMessage([A("gone.example.test.", "192.0.2.9"), Soa(12)]));
        Assert.False(assembler.IsComplete);
        Assert.Null(assembler.AddMessage([A("new.example.test.", "192.0.2.10")]));
        Assert.False(assembler.IsComplete);
        Assert.Null(assembler.AddMessage([Soa(12)]));

        Assert.True(assembler.IsComplete);
        Assert.Single(assembler.BuildResult().DiffSteps);
    }

    [Fact]
    public void AddMessage_AnIncrementalAnswerToAnAxfr_Fails()
    {
        ZoneTransferResponseAssembler assembler = new(null);

        string failure = assembler.AddMessage([Soa(12), Soa(10), Soa(12), Soa(12)]);

        Assert.Contains("incremental", failure);
    }

    [Fact]
    public void AddMessage_AFullAnswerWhoseClosingSoaHasAnotherSerial_Fails()
    {
        ZoneTransferResponseAssembler assembler = new(null);

        string failure = assembler.AddMessage([Soa(5), A("a.example.test.", "192.0.2.1"), Soa(6)]);

        Assert.Contains("different serial", failure);
        Assert.False(assembler.IsComplete);
    }

    [Fact]
    public void AddMessage_RecordsAfterTheClosingSoaInTheSameMessage_Fail()
    {
        ZoneTransferResponseAssembler assembler = new(null);

        string failure = assembler.AddMessage([
            Soa(5),
            A("a.example.test.", "192.0.2.1"),
            Soa(5),
            A("b.example.test.", "192.0.2.2"),
        ]);

        Assert.Contains("after its closing SOA", failure);
    }

    [Fact]
    public void AddMessage_ANextMessageAfterCompletion_Fails()
    {
        ZoneTransferResponseAssembler assembler = new(null);
        Assert.Null(assembler.AddMessage([Soa(5), Soa(5)]));

        string failure = assembler.AddMessage([A("a.example.test.", "192.0.2.1")]);

        Assert.Contains("after its closing SOA", failure);
    }

    [Fact]
    public void AddMessage_AClosingSoaThatDoesNotDecode_Fails()
    {
        ZoneTransferResponseAssembler assembler = new(null);

        string failure = assembler.AddMessage([
            Soa(5),
            A("a.example.test.", "192.0.2.1"),
            Garbage(),
        ]);

        Assert.Contains("could not be decoded", failure);
    }

    [Fact]
    public void BuildResult_BeforeCompletion_IsAFailure()
    {
        ZoneTransferResponseAssembler assembler = new(null);
        Assert.Null(assembler.AddMessage([Soa(5), A("a.example.test.", "192.0.2.1")]));

        ZoneTransferPullResult result = assembler.BuildResult();

        Assert.False(result.Succeeded);
        Assert.Contains("closing SOA", result.FailureReason);
    }
}

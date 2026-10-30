using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class reads an AXFR or IXFR answer message by message and decides when it is complete
///     (RFC 5936 §2.2, RFC 1995 §4), so a pull never depends on the primary closing the
///     connection. <see cref="ZoneTransferPullOverStream" /> feeds it the answer records of each
///     verified message and stops reading once <see cref="IsComplete" /> is true.
///     <para>
///     The answer is the leading SOA (serial S) followed by one of three forms. A full transfer is
///     the records and then a closing SOA with serial S. An incremental one is, per step, an old
///     SOA, the removed records, a new SOA and the added records, and then the closing SOA with
///     serial S. An IXFR "up to date" answer is the single SOA. The closing SOA is never part of
///     the result. An incomplete or malformed answer is never turned into a result, so a cut
///     transfer cannot reach the zone.
///     </para>
/// </summary>
/// <param name="requestedSerial">
///     The serial the IXFR query carried, or <see langword="null" /> for an AXFR.
/// </param>
internal sealed class ZoneTransferResponseAssembler(uint? requestedSerial)
{
    /// <summary>
    ///     This enum is where the assembler is in the answer.
    /// </summary>
    private enum Phase
    {
        /// <summary>No record has been read.</summary>
        LeadingSoa,

        /// <summary>The leading SOA is read; the next record decides the form.</summary>
        SecondRecord,

        /// <summary>A full transfer: records until the closing SOA.</summary>
        FullBody,

        /// <summary>An incremental step: the records the step removes.</summary>
        Removed,

        /// <summary>An incremental step: the records the step adds.</summary>
        Added,

        /// <summary>The closing SOA (or the "up to date" SOA) is read.</summary>
        Complete,
    }

    /// <summary>The current position in the answer.</summary>
    private Phase phase = Phase.LeadingSoa;

    /// <summary>The number of messages added so far.</summary>
    private int messageCount;

    /// <summary>The serial of the leading SOA.</summary>
    private uint leadingSerial;

    /// <summary>Whether the answer was the IXFR "up to date" single SOA.</summary>
    private bool alreadyCurrent;

    /// <summary>The leading SOA followed by a full transfer's records.</summary>
    private readonly List<DnsResourceRecord> fullRecords = [];

    /// <summary>The finished incremental steps.</summary>
    private readonly List<ZoneTransferDiffStep> steps = [];

    /// <summary>Whether the answer took the incremental form.</summary>
    private bool isIncremental;

    /// <summary>The open step's old serial.</summary>
    private uint stepOldSerial;

    /// <summary>The open step's new serial.</summary>
    private uint stepNewSerial;

    /// <summary>The open step's removed records.</summary>
    private List<DnsResourceRecord> stepRemoved = [];

    /// <summary>The open step's added records.</summary>
    private List<DnsResourceRecord> stepAdded = [];

    /// <summary>The open step's new SOA record, which carries the primary's SOA fields.</summary>
    private DnsResourceRecord stepNewSoa;

    /// <summary>
    ///     This property is true once the closing SOA, or an IXFR "up to date" single SOA, has
    ///     been read.
    /// </summary>
    public bool IsComplete => phase == Phase.Complete;

    /// <summary>
    ///     This method adds the next message's answer records, in order.
    /// </summary>
    /// <param name="answers">The answer section of the next response message.</param>
    /// <returns>A failure reason, or <see langword="null" /> when the records were accepted.</returns>
    public string AddMessage(IReadOnlyList<DnsResourceRecord> answers)
    {
        messageCount++;

        foreach (DnsResourceRecord record in answers)
        {
            string failure = AddRecord(record);

            if (failure is not null)
                return failure;
        }

        // RFC 1995 §2: a primary answers an IXFR whose client is current with the SOA alone in
        // the first message. A single SOA that is newer than the request is only the start of a
        // longer answer, so reading goes on; a close before its end is then a failure.
        if (
            phase == Phase.SecondRecord
            && messageCount == 1
            && answers.Count == 1
            && requestedSerial is { } requested
            && !SoaSerialArithmetic.IsGreaterThan(leadingSerial, requested)
        )
        {
            alreadyCurrent = true;
            phase = Phase.Complete;
        }

        return null;
    }

    /// <summary>
    ///     This method returns the result of the completed answer.
    /// </summary>
    /// <returns>
    ///     The result, or a failure when called before <see cref="IsComplete" />.
    /// </returns>
    public ZoneTransferPullResult BuildResult()
    {
        if (!IsComplete)
            return ZoneTransferPullResult.Failure(
                "The transfer response ended before its closing SOA."
            );

        if (alreadyCurrent)
            return ZoneTransferPullResult.AlreadyCurrent(leadingSerial);

        return isIncremental
            ? ZoneTransferPullResult.Incremental(leadingSerial, steps)
            : ZoneTransferPullResult.Full(leadingSerial, fullRecords);
    }

    /// <summary>
    ///     This method applies one record to the state machine described in the class summary.
    /// </summary>
    private string AddRecord(DnsResourceRecord record)
    {
        bool isSoa = record.Type == DnsResourceRecordTypeEnum.Soa;
        uint serial = 0;

        if (isSoa)
        {
            SoaRecordData soaData;

            try
            {
                soaData =
                    DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Soa, record.Data)
                    as SoaRecordData;
            }
            catch (Exception exception)
                when (exception is FormatException or ArgumentException or IndexOutOfRangeException)
            {
                // The SOA codec reports truncated RDATA with a range exception, not a format one.
                // A malformed answer must be a failure, never an exception out of the pull.
                soaData = null;
            }

            if (soaData is null)
                return "A transfer response SOA record could not be decoded.";

            serial = soaData.Serial;
        }

        switch (phase)
        {
            case Phase.Complete:
                return "The transfer response holds records after its closing SOA.";

            case Phase.LeadingSoa:
                if (!isSoa)
                    return "The transfer response did not begin with an SOA record.";

                leadingSerial = serial;
                fullRecords.Add(record);
                phase = Phase.SecondRecord;
                return null;

            case Phase.SecondRecord:
                if (!isSoa)
                {
                    fullRecords.Add(record);
                    phase = Phase.FullBody;
                    return null;
                }

                if (serial == leadingSerial)
                {
                    // A zone with only an SOA: the leading SOA and the closing one.
                    phase = Phase.Complete;
                    return null;
                }

                if (requestedSerial is null)
                    return "The primary answered an AXFR request with an incremental response.";

                isIncremental = true;
                stepOldSerial = serial;
                phase = Phase.Removed;
                return null;

            case Phase.FullBody:
                if (!isSoa)
                {
                    fullRecords.Add(record);
                    return null;
                }

                if (serial != leadingSerial)
                    return "The transfer response's closing SOA carries a different serial than its leading SOA.";

                phase = Phase.Complete;
                return null;

            case Phase.Removed:
                if (!isSoa)
                {
                    stepRemoved.Add(record);
                    return null;
                }

                stepNewSerial = serial;
                stepNewSoa = record;
                phase = Phase.Added;
                return null;

            default:
                if (!isSoa)
                {
                    stepAdded.Add(record);
                    return null;
                }

                // Added state. The step ends here. The leading serial marks the closing SOA, since
                // a valid chain never repeats it before its end; any other serial is the old SOA
                // of the next step.
                steps.Add(
                    new ZoneTransferDiffStep(
                        stepOldSerial,
                        stepRemoved,
                        stepNewSerial,
                        stepAdded,
                        stepNewSoa
                    )
                );
                stepRemoved = [];
                stepAdded = [];

                if (serial == leadingSerial)
                {
                    phase = Phase.Complete;
                    return null;
                }

                stepOldSerial = serial;
                phase = Phase.Removed;
                return null;
        }
    }
}

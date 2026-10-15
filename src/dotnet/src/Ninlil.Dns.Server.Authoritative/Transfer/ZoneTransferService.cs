using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class implements <see cref="IZoneTransferService" />. It serves AXFR and IXFR from
///     <see cref="AuthoritativeZoneCache" /> and the change journal
///     (<see cref="IZoneChangeJournalRepository" />), after checking transport, TSIG and zone
///     name. For a signed zone it adds the DNSKEY, RRSIG, NSEC or NSEC3, NSEC3PARAM and generated
///     DS records from <see cref="SignedZoneCache" />; nothing is signed here. Records marked
///     <c>ExcludeFromSync</c> are left out. Every response message is TSIG-signed, chained as RFC
///     8945 §5.3.1 describes.
/// </summary>
/// <param name="zoneCache">The cache this service transfers zones out of.</param>
/// <param name="signedZoneCache">
///     The cache the DNSSEC records of signed zones are read from.
/// </param>
/// <param name="journalRepository">The repository IXFR's incremental diffs are read from.</param>
/// <param name="tsigService">The service used to verify the request and sign each response.</param>
/// <param name="options">
///     The records-per-message setting, read through <see cref="IOptionsMonitor{TOptions}" /> on
///     every transfer.
/// </param>
public class ZoneTransferService(
    AuthoritativeZoneCache zoneCache,
    SignedZoneCache signedZoneCache,
    IZoneChangeJournalRepository journalRepository,
    ITsigService tsigService,
    IOptionsMonitor<ZoneTransferOptions> options
) : IZoneTransferService
{
    /// <inheritdoc />
    public async IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
        DnsQueryContext context,
        [EnumeratorCancellation] CancellationToken stoppingToken = default
    )
    {
        DnsMessage request = context.Request;

        if (request.Questions.Count == 0)
        {
            yield return ErrorResponse(request, DnsResponseCodeEnum.FormatError);
            yield break;
        }

        DnsQuestion question = request.Questions[0];
        (CachedZone zone, TsigVerificationResult tsigResult, DnsMessage authorizationError) =
            await AuthorizeAsync(request, context, question, stoppingToken);

        if (authorizationError is not null)
        {
            yield return authorizationError;
            yield break;
        }

        IReadOnlyList<RecordEntity> soaRecords = zone.GetRecords(
            zone.ApexName,
            DnsResourceRecordTypeEnum.Soa
        );

        if (soaRecords.Count == 0)
        {
            // A zone without an SOA cannot be transferred (RFC 5936 needs it at both ends); this
            // should not happen.
            yield return await SignedErrorResponseAsync(
                request,
                DnsResponseCodeEnum.ServerFailure,
                tsigResult.Key.Name,
                stoppingToken
            );
            yield break;
        }

        DnsResourceRecord soaWireRecord = RecordEntityCodec.ToWireRecord(
            soaRecords[0],
            zone.ApexName
        );

        List<DnsResourceRecord> otherRecords =
        [
            .. zone.GetAllRecords()
                .Where(record =>
                    record.Type != DnsResourceRecordTypeEnum.Soa && !record.ExcludeFromSync
                )
                .Select(record =>
                    RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name))
                ),
        ];

        // DNSSEC records are not in AuthoritativeZoneCache, so add them from SignedZoneCache or
        // the secondary would get only unsigned data.
        if (
            zone.Zone.IsDnssecSigned
            && signedZoneCache.GetSignedData(zone.Zone.Id) is { } signedData
        )
            otherRecords.AddRange(FlattenSignedZoneContent(signedData));

        // An RRset holds no duplicate records (RFC 2181 §5). A signed secondary's cache already
        // holds the transferred DNSSEC rows, and a stored DS is also in the signed content, so
        // the first occurrence of each (owner, type, class, RDATA) is kept.
        otherRecords = DeduplicateRecords(otherRecords);

        List<List<DnsResourceRecord>> chunks = Chunk(
            otherRecords,
            Math.Max(1, options.CurrentValue.RecordsPerMessage)
        );

        if (chunks.Count == 0)
            chunks.Add([]);

        chunks[0].Insert(0, soaWireRecord);

        // The closing SOA is added only when the answer spans several messages; a single-message
        // answer ends with the connection. RFC 5936 §2.2 expects the closing SOA in every AXFR.
        if (chunks.Count > 1)
            chunks[^1].Add(soaWireRecord);

        // RFC 8945 §5.3.1: the first message chains from the request MAC and each later one from
        // the previous message's MAC. Every message is signed, so no unsigned bytes are ever added.
        byte[] priorMac = null;

        foreach (List<DnsResourceRecord> chunk in chunks)
        {
            stoppingToken.ThrowIfCancellationRequested();

            DnsMessage signed = await SignSequenceMessageAsync(
                BuildMessage(request, question, chunk),
                tsigResult.Key.Name,
                request.Id,
                request.Tsig.Mac,
                priorMac,
                stoppingToken
            );
            priorMac = signed.Tsig.Mac;

            yield return signed;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
        DnsQueryContext context,
        [EnumeratorCancellation] CancellationToken stoppingToken = default
    )
    {
        DnsMessage request = context.Request;

        if (request.Questions.Count == 0)
        {
            yield return ErrorResponse(request, DnsResponseCodeEnum.FormatError);
            yield break;
        }

        DnsQuestion question = request.Questions[0];
        (CachedZone zone, TsigVerificationResult tsigResult, DnsMessage authorizationError) =
            await AuthorizeAsync(request, context, question, stoppingToken);

        if (authorizationError is not null)
        {
            yield return authorizationError;
            yield break;
        }

        // RFC 1995 §3: the client's serial arrives as an SOA in the Authority section.
        DnsResourceRecord clientSoaWireRecord = request.AuthorityRecords.FirstOrDefault(record =>
            record.Type == DnsResourceRecordTypeEnum.Soa
        );

        if (
            clientSoaWireRecord is null
            || DnsRecordDataCodecRegistry.Decode(
                DnsResourceRecordTypeEnum.Soa,
                clientSoaWireRecord.Data
            )
                is not SoaRecordData clientSoaData
        )
        {
            yield return await SignedErrorResponseAsync(
                request,
                DnsResponseCodeEnum.FormatError,
                tsigResult.Key.Name,
                stoppingToken
            );
            yield break;
        }

        IReadOnlyList<RecordEntity> currentSoaRecords = zone.GetRecords(
            zone.ApexName,
            DnsResourceRecordTypeEnum.Soa
        );

        if (
            currentSoaRecords.Count == 0
            || RecordEntityCodec.DecodeTypedData(currentSoaRecords[0])
                is not SoaRecordData currentSoaData
        )
        {
            yield return await SignedErrorResponseAsync(
                request,
                DnsResponseCodeEnum.ServerFailure,
                tsigResult.Key.Name,
                stoppingToken
            );
            yield break;
        }

        RecordEntity currentSoaRecord = currentSoaRecords[0];
        DnsResourceRecord currentSoaWireRecord = RecordEntityCodec.ToWireRecord(
            currentSoaRecord,
            zone.ApexName
        );

        long clientSerial = clientSoaData.Serial;
        long serverSerial = currentSoaData.Serial;

        if (clientSerial >= serverSerial)
        {
            // The client is current (or claims a newer serial): answer with the current SOA alone, a
            // single-message answer signed from the request MAC. The comparison is a plain integer
            // comparison, not RFC 1982 serial arithmetic.
            yield return await tsigService.SignResponseAsync(
                BuildMessage(request, question, [currentSoaWireRecord]),
                tsigResult.Key.Name,
                request.Id,
                request.Tsig.Mac,
                stoppingToken
            );
            yield break;
        }

        // Read the signed content once; it decides whether IXFR can be incremental and is added to
        // a full answer.
        SignedZoneData signedData = zone.Zone.IsDnssecSigned
            ? signedZoneCache.GetSignedData(zone.Zone.Id)
            : null;

        // A signed zone never gets an incremental answer, so skip the journal.
        IReadOnlyList<ZoneChangeJournalEntryEntity> journalEntries = signedData is null
            ? await journalRepository.ListSinceSerialAsync(
                zone.Zone.Id,
                clientSerial,
                stoppingToken
            )
            : [];

        bool journalCoversRequestedRange =
            // Signed zones always get a full transfer: every signing pass recomputes all RRSIGs, so no
            // signed diff is journaled. RFC 1995 §4 allows answering IXFR with a full transfer.
            // Otherwise the journal must cover the client's serial to the current one without gaps.
            signedData is null
            && journalEntries.Count > 0
            && journalEntries[0].OldSerial == clientSerial
            && journalEntries[^1].NewSerial == serverSerial
            && IsContiguousChain(journalEntries);

        List<DnsResourceRecord> recordSequence = [currentSoaWireRecord];

        if (journalCoversRequestedRange)
        {
            foreach (ZoneChangeJournalEntryEntity entry in journalEntries)
            {
                // The other SOA fields are taken from the current SOA. ZoneUpdateService only changes the
                // serial, and SOA edits are not journaled (see ZoneUpdateService.ApplyRecordEditAsync).
                recordSequence.Add(
                    BuildHistoricalSoaWireRecord(
                        currentSoaData,
                        zone.ApexName,
                        currentSoaRecord.TimeToLiveSeconds,
                        (uint)entry.OldSerial
                    )
                );
                recordSequence.AddRange(DecodeRecordSnapshot(entry.RemovedRecordsJson));
                recordSequence.Add(
                    BuildHistoricalSoaWireRecord(
                        currentSoaData,
                        zone.ApexName,
                        currentSoaRecord.TimeToLiveSeconds,
                        (uint)entry.NewSerial
                    )
                );
                recordSequence.AddRange(DecodeRecordSnapshot(entry.AddedRecordsJson));
            }
        }
        else
        {
            // No usable history (too old, not journaled, or a gap): send a full transfer. The client
            // recognizes it because the record after the first SOA is not an older SOA.
            recordSequence.AddRange(
                zone.GetAllRecords()
                    .Where(record =>
                        record.Type != DnsResourceRecordTypeEnum.Soa && !record.ExcludeFromSync
                    )
                    .Select(record =>
                        RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name))
                    )
            );
        }

        // Only the full-transfer branch can run for a signed zone, so this adds the signed records
        // to a full answer.
        if (signedData is not null)
            recordSequence.AddRange(FlattenSignedZoneContent(signedData));

        List<List<DnsResourceRecord>> chunks = Chunk(
            recordSequence,
            Math.Max(1, options.CurrentValue.RecordsPerMessage)
        );

        if (chunks.Count > 1)
            chunks[^1].Add(currentSoaWireRecord);

        // Chained as in CreateAxfrResponseAsync.
        byte[] priorMac = null;

        foreach (List<DnsResourceRecord> chunk in chunks)
        {
            stoppingToken.ThrowIfCancellationRequested();

            DnsMessage signed = await SignSequenceMessageAsync(
                BuildMessage(request, question, chunk),
                tsigResult.Key.Name,
                request.Id,
                request.Tsig.Mac,
                priorMac,
                stoppingToken
            );
            priorMac = signed.Tsig.Mac;

            yield return signed;
        }
    }

    /// <summary>
    ///     This method signs one message of an answer sequence (RFC 8945 §5.3.1): the first
    ///     (<paramref name="priorMac" /> is <see langword="null" />) with
    ///     <see cref="ITsigService.SignResponseAsync" /> from the request MAC, later ones with
    ///     <see cref="ITsigService.SignContinuationAsync" /> from the previous message's MAC.
    /// </summary>
    private Task<DnsMessage> SignSequenceMessageAsync(
        DnsMessage message,
        string keyName,
        ushort originalId,
        byte[] requestMac,
        byte[] priorMac,
        CancellationToken stoppingToken
    ) =>
        priorMac is null
            ? tsigService.SignResponseAsync(message, keyName, originalId, requestMac, stoppingToken)
            : tsigService.SignContinuationAsync(
                message,
                keyName,
                originalId,
                priorMac,
                stoppingToken
            );

    /// <summary>
    ///     This method runs the checks AXFR and IXFR share: the request came over TCP, TLS or QUIC,
    ///     carries a TSIG that verifies, names a hosted zone's apex and uses a key listed in the
    ///     zone's <see cref="ZoneEntity.TransferTsigKeyNames" />.
    /// </summary>
    /// <returns>
    ///     On success, the zone and TSIG result with a <see langword="null" /> error; on failure,
    ///     <see langword="null" /> zone and result and the one message to yield.
    /// </returns>
    private async Task<(
        CachedZone Zone,
        TsigVerificationResult TsigResult,
        DnsMessage Error
    )> AuthorizeAsync(
        DnsMessage request,
        DnsQueryContext context,
        DnsQuestion question,
        CancellationToken stoppingToken
    )
    {
        // RFC 5936 §4.2 limits AXFR to TCP. IXFR is limited too, so its UDP truncate-and-retry
        // (RFC 1995 §3) need not be implemented. TLS (RFC 9103) and QUIC are stream transports
        // and are accepted; only UDP is refused.
        if (
            context.Protocol
            is not (
                DnsTransportProtocolEnum.Tcp
                or DnsTransportProtocolEnum.Tls
                or DnsTransportProtocolEnum.Quic
            )
        )
            return (null, null, ErrorResponse(request, DnsResponseCodeEnum.Refused));

        // Zone transfers always require TSIG; there is no address-based allowance. A request
        // without a signature gets a plain REFUSED: there is no key to sign with.
        if (request.Tsig is null)
            return (null, null, ErrorResponse(request, DnsResponseCodeEnum.Refused));

        // Stream transfers bypass the query pipeline (ZoneTransferRequestRunner), so its
        // transaction-signature gate has not run and this is the only verification.
        TsigVerificationResult tsigResult = await tsigService.VerifyAsync(
            context.RawMessage,
            context.TsigRecordStartOffset,
            request,
            stoppingToken
        );

        if (!tsigResult.IsValid)
            return (
                null,
                null,
                await tsigService.SignErrorResponseAsync(
                    ErrorResponse(request, DnsResponseCodeEnum.NotAuthoritative),
                    request.Tsig,
                    tsigResult,
                    stoppingToken
                )
            );

        // A transfer must name the zone apex, not a name inside the zone.
        CachedZone zone = zoneCache.FindZone(question.Name);

        if (
            zone is null
            || zone.ApexName != question.Name
            // The key must also be bound to this zone (ZoneEntity.TransferTsigKeyNames); an empty
            // list accepts no key. Both refusals are signed, since the signature verified.
            || !ZoneTsigKeyBinding.IsAllowed(zone.Zone.TransferTsigKeyNames, tsigResult.Key.Name)
        )
            return (
                null,
                null,
                await SignedErrorResponseAsync(
                    request,
                    DnsResponseCodeEnum.Refused,
                    tsigResult.Key.Name,
                    stoppingToken
                )
            );

        return (zone, tsigResult, null);
    }

    /// <summary>
    ///     This method builds an error reply to a request whose TSIG verified and signs it from
    ///     the request MAC (RFC 8945 §5.3), so the client can tell the reply came from the primary.
    /// </summary>
    private async Task<DnsMessage> SignedErrorResponseAsync(
        DnsMessage request,
        DnsResponseCodeEnum responseCode,
        string keyName,
        CancellationToken stoppingToken
    ) =>
        await tsigService.SignResponseAsync(
            ErrorResponse(request, responseCode),
            keyName,
            request.Id,
            request.Tsig.Mac,
            stoppingToken
        );

    /// <summary>
    ///     This method builds one message in a transfer response sequence.
    /// </summary>
    private static DnsMessage BuildMessage(
        DnsMessage request,
        DnsQuestion question,
        IReadOnlyList<DnsResourceRecord> records
    ) =>
        new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            IsAuthoritativeAnswer = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions = { question },
            Answers = [.. records],
        };

    /// <summary>
    ///     This method builds the unsigned reply to a rejected transfer, echoing the question. The
    ///     caller signs it when the request's TSIG verified (<see cref="SignedErrorResponseAsync" />)
    ///     and leaves it unsigned or error-marked otherwise (RFC 8945 §5.3.2).
    /// </summary>
    private static DnsMessage ErrorResponse(DnsMessage request, DnsResponseCodeEnum responseCode) =>
        new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            ResponseCode = responseCode,
            Questions = [.. request.Questions],
        };

    /// <summary>
    ///     This method builds the SOA at an earlier serial for the bookends RFC 1995 §4 puts around
    ///     each difference sequence, from the current SOA's other fields.
    /// </summary>
    private static DnsResourceRecord BuildHistoricalSoaWireRecord(
        SoaRecordData currentSoaData,
        DnsName apexName,
        uint timeToLiveSeconds,
        uint serial
    ) =>
        new(
            apexName,
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            timeToLiveSeconds,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                currentSoaData with
                {
                    Serial = serial,
                }
            )
        );

    /// <summary>
    ///     This method decodes a journal entry's stored JSON snapshot of the records it added or
    ///     removed back into their wire form.
    /// </summary>
    private static List<DnsResourceRecord> DecodeRecordSnapshot(string json) =>
        [
            .. (Ninlil.Common.Serializer.JsonSerializer.Deserialize<List<RecordEntity>>(json) ?? [])
                // The journal can hold ExcludeFromSync records (they are local writes), but they are never
                // sent to an IXFR client.
                .Where(record => !record.ExcludeFromSync)
                .Select(record =>
                    RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name))
                ),
        ];

    /// <summary>
    ///     This method checks that journal entries form an unbroken chain, each old serial equal to
    ///     the previous new serial, with no gap from a pruned entry.
    /// </summary>
    private static bool IsContiguousChain(IReadOnlyList<ZoneChangeJournalEntryEntity> entries)
    {
        for (int i = 1; i < entries.Count; i++)
            if (entries[i].OldSerial != entries[i - 1].NewSerial)
                return false;

        return true;
    }

    /// <summary>
    ///     This method lists a zone's signed content from <see cref="SignedZoneCache" /> for a full
    ///     transfer: the DNSKEY records, the NSEC or NSEC3 chain and NSEC3PARAM, the DS records of
    ///     the delegations, and the RRSIGs over all of them. The caller removes records that the
    ///     zone cache already holds as ordinary rows.
    /// </summary>
    private static List<DnsResourceRecord> FlattenSignedZoneContent(SignedZoneData signedData)
    {
        List<DnsResourceRecord> records =
        [
            .. signedData.DnsKeyRecords,
            .. signedData.DenialOfExistenceRecords,
        ];

        if (signedData.Nsec3ParamRecord is not null)
            records.Add(signedData.Nsec3ParamRecord);

        foreach (
            IReadOnlyList<DnsResourceRecord> dsRecords in signedData
                .DsRecordsByDelegationName
                .Values
        )
            records.AddRange(dsRecords);

        foreach (IReadOnlyList<DnsResourceRecord> rrsigs in signedData.RrsigsByCoveredRrset.Values)
            records.AddRange(rrsigs);

        return records;
    }

    /// <summary>
    ///     This method removes repeated records from a transfer's record list, keeping the first
    ///     occurrence and the original order. Records are equal when owner, type, class and RDATA
    ///     match; the TTL is not compared, since an RRset has one TTL (RFC 2181 §5.2).
    /// </summary>
    private static List<DnsResourceRecord> DeduplicateRecords(
        IReadOnlyList<DnsResourceRecord> records
    )
    {
        HashSet<(
            DnsName Name,
            DnsResourceRecordTypeEnum Type,
            DnsClassEnum Class,
            string Data
        )> seen = [];
        List<DnsResourceRecord> result = [];

        foreach (DnsResourceRecord record in records)
            if (
                seen.Add((record.Name, record.Type, record.Class, Convert.ToHexString(record.Data)))
            )
                result.Add(record);

        return result;
    }

    /// <summary>
    ///     This method splits a list into fixed-size chunks, the last of which may be smaller.
    /// </summary>
    private static List<List<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        List<List<T>> chunks = [];

        for (int i = 0; i < items.Count; i += size)
            chunks.Add([.. items.Skip(i).Take(size)]);

        return chunks;
    }
}

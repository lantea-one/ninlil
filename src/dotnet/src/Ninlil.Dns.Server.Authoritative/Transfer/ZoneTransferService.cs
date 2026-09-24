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
///     This class implements <see cref="IZoneTransferService" />, serving full (AXFR) and
///     incremental (IXFR) zone transfers straight out of <see cref="AuthoritativeZoneCache" />'s
///     own in-memory copy and <see cref="IZoneChangeJournalRepository" />'s change history. A
///     DNSSEC-signed zone's own RRSIG/NSEC/NSEC3/NSEC3PARAM/DNSKEY/generated-DS content is served
///     alongside it too, read fresh from <see cref="SignedZoneCache" /> every time -- the same
///     already-computed signed view <see cref="Resolution.AuthoritativeQueryResolver" /> reads for
///     an ordinary DNSSEC-OK query answer, never computed or signed here, matching this project's
///     deliberate stance against ever signing live, per query or per transfer.
/// </summary>
/// <param name="zoneCache">The cache this service transfers zones out of.</param>
/// <param name="signedZoneCache">
///     The cache this service reads a DNSSEC-signed zone's own RRSIG/NSEC/NSEC3/NSEC3PARAM/DNSKEY/
///     generated-DS content out of.
/// </param>
/// <param name="journalRepository">The repository IXFR's incremental diffs are read from.</param>
/// <param name="tsigService">The service used to verify the request and sign each response.</param>
/// <param name="options">
///     This service's own configuration, such as message chunk size -- bound via
///     <see cref="IOptionsMonitor{TOptions}" /> and read fresh, via <c>options.CurrentValue</c>,
///     on every single transfer this (scoped) service serves, so a configuration write reaches
///     the very next AXFR/IXFR with no restart.
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
            // A properly-provisioned zone always has exactly one SOA record at its apex; this
            // should never actually happen, but a zone transfer cannot be meaningfully served
            // without one, so fail safely rather than serve a response that violates RFC 5936's
            // own structure.
            yield return ErrorResponse(request, DnsResponseCodeEnum.ServerFailure);
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

        // A DNSSEC-signed zone's own RRSIG/NSEC/NSEC3/NSEC3PARAM/DNSKEY/generated-DS content
        // never lives in AuthoritativeZoneCache at all -- see SignedZoneCache's own doc comment
        // -- so an AXFR that never looked here would otherwise serve a secondary only the
        // unsigned base records, exactly the gap this method exists to close.
        if (
            zone.Zone.IsDnssecSigned
            && signedZoneCache.GetSignedData(zone.Zone.Id) is { } signedData
        )
            otherRecords.AddRange(FlattenSignedZoneContent(signedData));

        List<List<DnsResourceRecord>> chunks = Chunk(
            otherRecords,
            Math.Max(1, options.CurrentValue.RecordsPerMessage)
        );

        if (chunks.Count == 0)
            chunks.Add([]);

        chunks[0].Insert(0, soaWireRecord);

        // Per RFC 5936 §2.2, a transfer spanning more than one message must end with the same
        // SOA record it began with, so the client can tell the sequence is complete; a
        // single-message transfer already conveys that just by the TCP response ending.
        if (chunks.Count > 1)
            chunks[^1].Add(soaWireRecord);

        // RFC 8945 §5.3.1: the first message in a multi-message answer sequence chains from the
        // original request's own MAC (already verified above, via AuthorizeAsync); every later
        // message chains from whichever message before it most recently carried a TSIG. This
        // project always signs every message in the sequence -- never skipping one, the more
        // thorough of the two RFC-permitted choices -- so priorMac alone (with no accompanying
        // unsigned-message bytes) is always the right "prior MAC" input for the next message.
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

        // RFC 1995 §3: the client's own current serial travels as an SOA record in the request's
        // Authority section, not anywhere the query's Question section can carry a number.
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
            yield return ErrorResponse(request, DnsResponseCodeEnum.FormatError);
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
            yield return ErrorResponse(request, DnsResponseCodeEnum.ServerFailure);
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
            // The client is already current -- or, per RFC 1995, claims a serial we've never
            // reached ourselves, in which case there is equally nothing newer to send. Either
            // way, a single message carrying just the current SOA says so -- an ordinary,
            // single-message answer, which RFC 8945 §5.3.1 signs identically to a sequence's own
            // first message (chained from the request's own MAC per §5.3).
            yield return await tsigService.SignResponseAsync(
                BuildMessage(request, question, [currentSoaWireRecord]),
                tsigResult.Key.Name,
                request.Id,
                request.Tsig.Mac,
                stoppingToken
            );
            yield break;
        }

        // A DNSSEC-signed zone's own signed content is read fresh here, once, rather than inside
        // either branch below -- both need to know whether the zone is signed at all before
        // deciding whether a true incremental replay is even attempted (see
        // journalCoversRequestedRange's own remarks immediately below), and both branches append
        // this exact same content once that decision is made.
        SignedZoneData signedData = zone.Zone.IsDnssecSigned
            ? signedZoneCache.GetSignedData(zone.Zone.Id)
            : null;

        // A signed zone's journal history is never consulted for a true incremental replay at
        // all -- only an unsigned zone's ever is -- skipping the lookup entirely for a signed one.
        IReadOnlyList<ZoneChangeJournalEntryEntity> journalEntries = signedData is null
            ? await journalRepository.ListSinceSerialAsync(
                zone.Zone.Id,
                clientSerial,
                stoppingToken
            )
            : [];

        bool journalCoversRequestedRange =
            // A DNSSEC-signed zone's own RRSIG/NSEC/NSEC3/NSEC3PARAM/DNSKEY content is never
            // journaled at the byte level at all: this project's signing design recomputes every
            // signature from scratch on every pass -- a fresh inception/expiration window even
            // for an RRset whose own data never changed, on an ordinary record edit exactly as
            // much as on IZoneUpdateService.BumpSerialForDnssecResignAsync's own proactive
            // re-sign -- so there is no historical "old vs new" signed content ever kept to
            // replay incrementally in the first place. A signed zone's IXFR is therefore always
            // served as a full transfer instead (the else branch immediately below), exactly the
            // same "AXFR in IXFR's clothing" fallback RFC 1995 §4 already permits any conformant
            // server to take unconditionally -- the only genuinely correct choice this project's
            // own architecture leaves for a signed zone specifically.
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
                // The other SOA fields (primary name server, mailbox, refresh/retry/expire, and
                // the negative-caching minimum) are assumed unchanged across this history -- a
                // reasonable, deliberate simplification, since ZoneUpdateService never touches
                // them; only Serial itself ever moves between journal entries it produces.
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
            // No usable history covers the client's claimed serial (too old, never journaled, or
            // a broken chain); fall back to a full transfer, in AXFR shape. The client tells this
            // isn't an incremental diff because the record right after the leading SOA here is
            // not itself another SOA with an older serial.
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

        // Appended once, after either branch above, rather than duplicated inside each -- a true
        // incremental replay above never runs at all for a signed zone (journalCoversRequestedRange
        // is unconditionally false whenever signedData is not null), so this only ever actually
        // adds anything in the full-transfer branch, but reads identically regardless of which one
        // ran.
        if (signedData is not null)
            recordSequence.AddRange(FlattenSignedZoneContent(signedData));

        List<List<DnsResourceRecord>> chunks = Chunk(
            recordSequence,
            Math.Max(1, options.CurrentValue.RecordsPerMessage)
        );

        if (chunks.Count > 1)
            chunks[^1].Add(currentSoaWireRecord);

        // See the identical comment in CreateAxfrResponseAsync -- the same RFC 8945 §5.3.1
        // chaining applies here.
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
    ///     This method signs one message within a multi-message answer sequence, per RFC 8945
    ///     §5.3.1: the sequence's first message (<paramref name="priorMac" /> still
    ///     <see langword="null" />) chains from the original request's own MAC via
    ///     <see cref="ITsigService.SignResponseAsync" />; every later message chains from the
    ///     message immediately before it via <see cref="ITsigService.SignContinuationAsync" />.
    ///     Shared by <see cref="CreateAxfrResponseAsync" /> and <see cref="CreateIxfrResponseAsync" />,
    ///     which otherwise differ only in how <paramref name="message" />'s own record content was
    ///     assembled.
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
    ///     This method performs the checks both <see cref="CreateAxfrResponseAsync" /> and
    ///     <see cref="CreateIxfrResponseAsync" /> share before serving anything: the request
    ///     arrived over TCP, carries a TSIG record that verifies, and names a zone this server
    ///     hosts exactly.
    /// </summary>
    /// <returns>
    ///     The matched zone and TSIG result on success, with a <see langword="null" /> error
    ///     message; or a <see langword="null" /> zone and TSIG result alongside the single
    ///     message the caller should yield and stop on failure.
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
        // RFC 5936 §4.3.5 restricts AXFR to TCP; RFC 1995 does not say the same of IXFR in as
        // many words, but this project applies the same restriction to both, deliberately, to
        // avoid also having to implement IXFR-over-UDP's own truncate-and-retry-over-TCP
        // semantics (RFC 1995 §3) for what would otherwise be an uncommon path in practice. Tls
        // and Quic are also accepted here: RFC 9103 XFR-over-TLS and this project's own QUIC
        // analogue both still carry the exact same reliable, ordered, TCP-shaped stream framing
        // underneath (a TLS session over TCP; a dedicated QUIC stream) -- only Udp is actually
        // excluded.
        if (
            context.Protocol
            is not (
                DnsTransportProtocolEnum.Tcp
                or DnsTransportProtocolEnum.Tls
                or DnsTransportProtocolEnum.Quic
            )
        )
            return (null, null, ErrorResponse(request, DnsResponseCodeEnum.Refused));

        // A zone transfer always requires TSIG, per this project's design decision -- there is
        // no IP-allowlist-only fallback.
        if (request.Tsig is null)
            return (null, null, ErrorResponse(request, DnsResponseCodeEnum.Refused));

        TsigVerificationResult tsigResult = await tsigService.VerifyAsync(
            context.RawMessage,
            context.TsigRecordStartOffset,
            request,
            stoppingToken
        );

        if (!tsigResult.IsValid)
            return (null, null, ErrorResponse(request, tsigResult.Error));

        // A zone transfer is always requested against a zone's own apex name exactly -- unlike
        // an ordinary query, which may ask about any name under a hosted zone, transferring a
        // name that merely falls under a zone rather than naming it outright makes no sense.
        CachedZone zone = zoneCache.FindZone(question.Name);

        return zone is null || zone.ApexName != question.Name
            ? (null, null, ErrorResponse(request, DnsResponseCodeEnum.Refused))
            : (zone, tsigResult, null);
    }

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
    ///     This method builds the single message a rejected transfer request is answered with.
    ///     Deliberately left unsigned even when a valid key was already found (for example, a
    ///     wrong zone name) -- RFC 8945 §5.2.2 disallows signing a response back at all when the
    ///     key itself is unrecognized, and simply never signing an error response is a safe,
    ///     conservative default that can never fall on the wrong side of that rule. The question
    ///     section is always echoed back too, matching <see cref="BuildMessage" />'s own
    ///     already-correct behavior.
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
    ///     This method reconstructs an SOA record as it existed at an earlier serial, for the
    ///     "old" and "new" bookends RFC 1995 §4 requires around each difference sequence, given
    ///     only the current SOA's other fields and the historical serial itself.
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
                // A journal entry can legitimately carry an ExcludeFromSync record (it was a
                // real local write, and BootstrapZoneAsync/ApplyChangesAsync must still journal
                // it), but it must never be replayed to an external IXFR client.
                .Where(record => !record.ExcludeFromSync)
                .Select(record =>
                    RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name))
                ),
        ];

    /// <summary>
    ///     This method checks that a sequence of journal entries forms one unbroken chain --
    ///     each entry's old serial matching the previous entry's new serial -- rather than
    ///     skipping over a gap a pruned or missing entry left behind.
    /// </summary>
    private static bool IsContiguousChain(IReadOnlyList<ZoneChangeJournalEntryEntity> entries)
    {
        for (int i = 1; i < entries.Count; i++)
            if (entries[i].OldSerial != entries[i - 1].NewSerial)
                return false;

        return true;
    }

    /// <summary>
    ///     This method flattens a zone's complete, currently cached signed content -- see
    ///     <see cref="SignedZoneCache" /> -- into one list suitable for appending to a full zone
    ///     transfer (an AXFR, or an IXFR that falls back to one): every active key's own DNSKEY
    ///     record, the complete NSEC or NSEC3 denial-of-existence chain (plus its own NSEC3PARAM
    ///     record, when applicable), every delegation's generated DS record, and every one of
    ///     those RRsets' own covering RRSIG(s) -- exactly the same signed content
    ///     <see cref="Resolution.AuthoritativeQueryResolver" /> already reads for an ordinary
    ///     DNSSEC-OK query answer, never computed or signed here.
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
                .GeneratedDsRecordsByDelegationName
                .Values
        )
            records.AddRange(dsRecords);

        foreach (IReadOnlyList<DnsResourceRecord> rrsigs in signedData.RrsigsByCoveredRrset.Values)
            records.AddRange(rrsigs);

        return records;
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

using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Resolution;

/// <summary>
///     This class is the authoritative engine, the <see cref="IDnsQueryResolver" /> that answers
///     from hosted zones. It finds the zone for the query name in
///     <see cref="AuthoritativeZoneCache" /> and answers fully: an exact match, a CNAME (followed
///     one hop inside the zone), a wildcard, a referral, or a negative answer with the SOA. A
///     name in no hosted zone is declined (<see cref="DnsQueryResolution.NotHandled" />) so the
///     dispatcher offers it to the forwarding and recursive resolvers. It also answers CHAOS
///     <c>version.bind</c>-style queries when enabled.
///     <para>
///         When the client sets the EDNS DNSSEC OK bit and the zone is signed, the RRSIG, NSEC,
///         NSEC3, DNSKEY and DS records are taken from <see cref="Dnssec.SignedZoneCache" />.
///         Nothing is signed per query; signing happens ahead of time in
///         <see cref="ZoneSigningService" />.
///     </para>
/// </summary>
/// <param name="zoneCache">The in-memory zone cache this resolver answers from.</param>
/// <param name="signedZoneCache">The in-memory signed-content cache this resolver answers DNSSEC data from.</param>
/// <param name="options">
///     The CHAOS query settings. Read through <see cref="IOptionsMonitor{TOptions}" /> on each
///     query, so a change made through <c>BrandingSettingsController</c> applies without a
///     restart; <see cref="IOptions{TOptions}" /> would keep the startup value in this
///     singleton.
/// </param>
public class AuthoritativeQueryResolver(
    AuthoritativeZoneCache zoneCache,
    SignedZoneCache signedZoneCache,
    IOptionsMonitor<AuthoritativeServerOptions> options
) : IDnsQueryResolver
{
    /// <inheritdoc />
    public ValueTask<DnsQueryResolution> TryResolveAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    )
    {
        DnsMessage request = context.Request;
        DnsQuestion question = request.Questions[0];

        if (question.Class == DnsClassEnum.Chaos)
            return ValueTask.FromResult(TryResolveChaosQuery(request, question));

        if (question.Class != DnsClassEnum.Internet)
            return ValueTask.FromResult(DnsQueryResolution.NotHandled);

        CachedZone zone = zoneCache.FindZone(question.Name, question.Type);

        // A Forwarder or SecondaryForwarder zone holds only a synthetic SOA and TXT records at
        // its apex. They exist for zone transfer (ZoneTransferService reads the cache directly;
        // the TCP, TLS and QUIC listeners route AXFR/IXFR there without the dispatcher) and for
        // a secondary's SOA serial check (ZoneTransferPullCodec.QuerySoaSerialOverUdpAsync).
        // Only the apex SOA is answered here. Every other query goes on to
        // ForwarderQueryResolver: answering the TXT records would reveal the upstream forwarder
        // addresses to anyone, while zone transfers require TSIG. The SOA reveals nothing
        // sensitive.
        if (
            zone is { Zone.Type: ZoneTypeEnum.Forwarder or ZoneTypeEnum.SecondaryForwarder }
            && (question.Type != DnsResourceRecordTypeEnum.Soa || question.Name != zone.ApexName)
        )
            return ValueTask.FromResult(DnsQueryResolution.NotHandled);

        return ValueTask.FromResult(
            zone is null
                ? DnsQueryResolution.NotHandled
                : DnsQueryResolution.Handled(ResolveWithinZone(request, question, zone))
        );
    }

    /// <summary>
    ///     This method answers the CHAOS-class TXT queries <c>version.bind</c>,
    ///     <c>version.server</c>, <c>hostname.bind</c> and <c>id.server</c> when
    ///     <see cref="AuthoritativeServerOptions.EnableChaosVersionQueries" /> is on. Other CHAOS
    ///     queries are declined.
    /// </summary>
    private DnsQueryResolution TryResolveChaosQuery(DnsMessage request, DnsQuestion question)
    {
        if (
            !options.CurrentValue.EnableChaosVersionQueries
            || question.Type != DnsResourceRecordTypeEnum.Txt
        )
            return DnsQueryResolution.NotHandled;

        string queryName = question.Name.ToString().TrimEnd('.').ToLowerInvariant();

        string value = queryName switch
        {
            "version.bind" or "version.server" => options.CurrentValue.VersionString,
            "hostname.bind" or "id.server" => options.CurrentValue.HostnameString,
            _ => null,
        };

        if (value is null)
            return DnsQueryResolution.NotHandled;

        DnsMessage response = CreateAnswerHeader(request, isAuthoritative: true);
        response.Answers.Add(
            new DnsResourceRecord(
                question.Name,
                DnsResourceRecordTypeEnum.Txt,
                DnsClassEnum.Chaos,
                0,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Txt,
                    new TxtRecordData([value])
                )
            )
        );

        return DnsQueryResolution.Handled(response);
    }

    /// <summary>
    ///     This method answers a query inside a zone already found: a referral, a DS or DNSKEY
    ///     answer, an ANY answer, an exact match, a CNAME followed one hop within the zone, a
    ///     wildcard match, or a negative answer.
    /// </summary>
    private DnsMessage ResolveWithinZone(DnsMessage request, DnsQuestion question, CachedZone zone)
    {
        bool dnssecOk = request.Edns?.DnssecOk == true;
        SignedZoneData signedData = zone.Zone.IsDnssecSigned
            ? signedZoneCache.GetSignedData(zone.Zone.Id)
            : null;

        // At or below a delegation this zone is not authoritative, so refer the client to the
        // child's servers. FindEnclosingDelegation returns the topmost cut, so names under a
        // nested cut are referred to the outer one. Two exceptions, only at a cut that is
        // itself reached: an NS query (those NS records are in this zone), and a DS query, since
        // the DS RRset belongs to the parent (RFC 4035 §2.7).
        if (zone.FindEnclosingDelegation(question.Name) is { } delegation)
        {
            if (delegation.DelegationPoint == question.Name)
            {
                if (question.Type == DnsResourceRecordTypeEnum.NS)
                    return BuildAnswerResponse(
                        request,
                        question.Name,
                        delegation.NameServers,
                        zone,
                        isAuthoritative: true,
                        dnssecOk,
                        signedData
                    );

                if (question.Type == DnsResourceRecordTypeEnum.DS)
                    return BuildDsResponse(request, zone, question.Name, dnssecOk, signedData);
            }

            return BuildDelegationResponse(
                request,
                zone,
                delegation.DelegationPoint,
                delegation.NameServers,
                dnssecOk,
                signedData
            );
        }

        // DNSKEY records are not RecordEntity rows; they exist only in SignedZoneCache.
        if (
            question.Type == DnsResourceRecordTypeEnum.DnsKey
            && question.Name == zone.ApexName
            && signedData is not null
        )
            return BuildDnsKeyResponse(request, zone, dnssecOk, signedData);

        if (question.Type == DnsResourceRecordTypeEnum.Any)
            return BuildAnyQueryResponse(request, question, zone, dnssecOk, signedData);

        IReadOnlyList<RecordEntity> exactMatches = zone.GetRecords(question.Name, question.Type);

        if (exactMatches.Count > 0)
            return BuildAnswerResponse(
                request,
                question.Name,
                exactMatches,
                zone,
                isAuthoritative: true,
                dnssecOk,
                signedData
            );

        // No record of the requested type here; answer with a CNAME if there is one, followed
        // one hop when the target is in this zone.
        IReadOnlyList<RecordEntity> cnameMatches = zone.GetRecords(
            question.Name,
            DnsResourceRecordTypeEnum.Cname
        );

        if (cnameMatches.Count > 0)
            return BuildCnameChaseResponse(
                request,
                question,
                cnameMatches[0],
                zone,
                dnssecOk,
                signedData
            );

        // The name exists (records of another type, or an empty non-terminal), so this is
        // NODATA. A wildcard only covers names that do not exist.
        if (zone.NameExistsOrIsEmptyNonTerminal(question.Name))
            return BuildNegativeResponse(
                request,
                zone,
                isNxDomain: false,
                question.Name,
                dnssecOk,
                signedData
            );

        IReadOnlyList<RecordEntity> wildcardMatches = zone.GetWildcardRecords(
            question.Name,
            question.Type
        );

        if (wildcardMatches.Count > 0)
            return BuildWildcardAnswerResponse(
                request,
                question.Name,
                wildcardMatches,
                zone,
                dnssecOk,
                signedData
            );

        // A wildcard with other types still makes the name exist: NODATA, not NXDOMAIN.
        bool nameCoveredByAnyWildcard = zone.GetWildcardRecords(question.Name).Count > 0;

        return BuildNegativeResponse(
            request,
            zone,
            isNxDomain: !nameCoveredByAnyWildcard,
            question.Name,
            dnssecOk,
            signedData
        );
    }

    /// <summary>
    ///     This method builds a referral: no answer, the child's NS records in the Authority
    ///     section, and A/AAAA glue from this zone for name servers inside it. With DNSSEC, the
    ///     Authority section also gets the signed DS RRset (secure delegation) or the NSEC/NSEC3
    ///     proof that there is no DS (insecure delegation), per RFC 4035 §3.1.4.
    /// </summary>
    private DnsMessage BuildDelegationResponse(
        DnsMessage request,
        CachedZone zone,
        DnsName delegationPoint,
        IReadOnlyList<RecordEntity> nameServers,
        bool dnssecOk,
        SignedZoneData signedData
    )
    {
        DnsMessage response = CreateAnswerHeader(request, isAuthoritative: false);
        response.AuthorityRecords.AddRange(ToWireRecords(nameServers, delegationPoint));

        if (dnssecOk && signedData is not null)
        {
            IReadOnlyList<DnsResourceRecord> dsRecords =
                signedData.GeneratedDsRecordsByDelegationName.GetValueOrDefault(
                    delegationPoint,
                    []
                );

            if (dsRecords.Count > 0)
            {
                response.AuthorityRecords.AddRange(dsRecords);
                response.AuthorityRecords.AddRange(
                    signedData.GetSignatures(delegationPoint, DnsResourceRecordTypeEnum.DS)
                );
            }
            else
            {
                response.AuthorityRecords.AddRange(
                    DenialOfExistenceProofBuilder.BuildInsecureDelegationProof(
                        signedData,
                        zone.Zone,
                        delegationPoint
                    )
                );
            }
        }

        foreach (RecordEntity nameServerRecord in nameServers)
        {
            if (DecodeTypedData(nameServerRecord) is not NsRecordData nsData)
                continue;

            if (!IsWithinZone(nsData.NameServer, zone))
                continue;

            response.AdditionalRecords.AddRange(
                ToWireRecords(
                    zone.GetRecords(nsData.NameServer, DnsResourceRecordTypeEnum.A),
                    nsData.NameServer
                )
            );
            response.AdditionalRecords.AddRange(
                ToWireRecords(
                    zone.GetRecords(nsData.NameServer, DnsResourceRecordTypeEnum.Aaaa),
                    nsData.NameServer
                )
            );
        }

        return response;
    }

    /// <summary>
    ///     This method builds a positive answer from matched records, with their RRSIGs when
    ///     DNSSEC applies.
    /// </summary>
    private DnsMessage BuildAnswerResponse(
        DnsMessage request,
        DnsName ownerName,
        IReadOnlyList<RecordEntity> records,
        CachedZone zone,
        bool isAuthoritative,
        bool dnssecOk,
        SignedZoneData signedData
    )
    {
        DnsMessage response = CreateAnswerHeader(request, isAuthoritative);
        response.Answers.AddRange(ToWireRecords(records, ownerName));

        if (dnssecOk && signedData is not null && records.Count > 0)
            AppendRewrittenSignatures(
                response.Answers,
                signedData,
                DnsName.Parse(records[0].Name),
                records[0].Type,
                ownerName
            );

        return response;
    }

    /// <summary>
    ///     This method builds an answer synthesized from a wildcard (RFC 1035 §4.3.3): the records
    ///     are served under the queried name, not the <c>*</c> name they are stored under. With
    ///     DNSSEC, the wildcard's RRSIGs are served under the queried name too (RFC 4035 §3.1.3).
    ///     Their Labels field still counts the wildcard name, which is how a validator detects
    ///     the expansion. The proof that no closer match exists is added to the Authority section.
    /// </summary>
    private DnsMessage BuildWildcardAnswerResponse(
        DnsMessage request,
        DnsName queryName,
        IReadOnlyList<RecordEntity> records,
        CachedZone zone,
        bool dnssecOk,
        SignedZoneData signedData
    )
    {
        DnsMessage response = CreateAnswerHeader(request, isAuthoritative: true);
        response.Answers.AddRange(ToWireRecords(records, queryName));

        if (dnssecOk && signedData is not null && records.Count > 0)
        {
            AppendRewrittenSignatures(
                response.Answers,
                signedData,
                DnsName.Parse(records[0].Name),
                records[0].Type,
                queryName
            );
            response.AuthorityRecords.AddRange(
                DenialOfExistenceProofBuilder.BuildWildcardProof(
                    signedData,
                    zone.Zone,
                    zone.ApexName,
                    queryName
                )
            );
        }

        return response;
    }

    /// <summary>
    ///     This method builds the answer for the apex DNSKEY RRset from
    ///     <see cref="SignedZoneCache" />, since DNSKEY records are not stored as
    ///     <see cref="RecordEntity" /> rows.
    /// </summary>
    private DnsMessage BuildDnsKeyResponse(
        DnsMessage request,
        CachedZone zone,
        bool dnssecOk,
        SignedZoneData signedData
    )
    {
        DnsMessage response = CreateAnswerHeader(request, isAuthoritative: true);
        response.Answers.AddRange(signedData.DnsKeyRecords);

        if (dnssecOk)
            response.Answers.AddRange(
                signedData.GetSignatures(zone.ApexName, DnsResourceRecordTypeEnum.DnsKey)
            );

        return response;
    }

    /// <summary>
    ///     This method answers a DS query at a delegation point. DS is the one type at that name
    ///     the parent is authoritative for (RFC 4035 §2.7), so it is answered instead of referred.
    ///     Only DS records generated for child zones this server signs are known; otherwise the
    ///     answer is NODATA.
    /// </summary>
    private DnsMessage BuildDsResponse(
        DnsMessage request,
        CachedZone zone,
        DnsName delegationPoint,
        bool dnssecOk,
        SignedZoneData signedData
    )
    {
        IReadOnlyList<DnsResourceRecord> dsRecords =
            signedData?.GeneratedDsRecordsByDelegationName.GetValueOrDefault(delegationPoint, [])
            ?? [];

        if (dsRecords.Count == 0)
            return BuildNegativeResponse(
                request,
                zone,
                isNxDomain: false,
                delegationPoint,
                dnssecOk,
                signedData
            );

        DnsMessage response = CreateAnswerHeader(request, isAuthoritative: true);
        response.Answers.AddRange(dsRecords);

        if (dnssecOk)
            response.Answers.AddRange(
                signedData.GetSignatures(delegationPoint, DnsResourceRecordTypeEnum.DS)
            );

        return response;
    }

    /// <summary>
    ///     This method builds the answer for a name that has a CNAME, adding the target's records
    ///     (one hop only) when the target is in the same zone, with RRSIGs when DNSSEC applies.
    /// </summary>
    private DnsMessage BuildCnameChaseResponse(
        DnsMessage request,
        DnsQuestion question,
        RecordEntity cnameRecord,
        CachedZone zone,
        bool dnssecOk,
        SignedZoneData signedData
    )
    {
        DnsMessage response = CreateAnswerHeader(request, isAuthoritative: true);
        response.Answers.AddRange(ToWireRecords([cnameRecord], question.Name));

        if (dnssecOk && signedData is not null)
            AppendRewrittenSignatures(
                response.Answers,
                signedData,
                question.Name,
                DnsResourceRecordTypeEnum.Cname,
                question.Name
            );

        if (
            DecodeTypedData(cnameRecord) is CnameRecordData cnameData
            && IsWithinZone(cnameData.Target, zone)
            // A target at or below a cut is the child's data, not authoritative here. The client
            // follows the CNAME itself (RFC 1034 §4.3.2 step 3a).
            && zone.FindEnclosingDelegation(cnameData.Target) is null
        )
        {
            IReadOnlyList<RecordEntity> targetRecords = zone.GetRecords(
                cnameData.Target,
                question.Type
            );
            response.Answers.AddRange(ToWireRecords(targetRecords, cnameData.Target));

            if (dnssecOk && signedData is not null && targetRecords.Count > 0)
                AppendRewrittenSignatures(
                    response.Answers,
                    signedData,
                    cnameData.Target,
                    question.Type,
                    cnameData.Target
                );
        }

        return response;
    }

    /// <summary>
    ///     This method builds the minimal RFC 8482 answer to an ANY query, a synthesized HINFO
    ///     record, instead of every record at the name, which would make ANY useful for
    ///     amplification. The HINFO record is not in the zone and has no signature, so DNSSEC only
    ///     applies to the NXDOMAIN case.
    /// </summary>
    private DnsMessage BuildAnyQueryResponse(
        DnsMessage request,
        DnsQuestion question,
        CachedZone zone,
        bool dnssecOk,
        SignedZoneData signedData
    )
    {
        bool nameExists =
            zone.NameExistsOrIsEmptyNonTerminal(question.Name)
            || zone.GetWildcardRecords(question.Name).Count > 0;

        if (!nameExists)
            return BuildNegativeResponse(
                request,
                zone,
                isNxDomain: true,
                question.Name,
                dnssecOk,
                signedData
            );

        DnsMessage response = CreateAnswerHeader(request, isAuthoritative: true);
        response.Answers.Add(
            new DnsResourceRecord(
                question.Name,
                DnsResourceRecordTypeEnum.Hinfo,
                DnsClassEnum.Internet,
                0,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Hinfo,
                    new HinfoRecordData("RFC8482", string.Empty)
                )
            )
        );

        return response;
    }

    /// <summary>
    ///     This method builds an RFC 2308 negative response: no answer, and the zone's SOA in the
    ///     Authority section with its TTL capped at the SOA minimum, which tells resolvers how
    ///     long to cache the negative answer. With DNSSEC, the SOA's RRSIG and the denial of
    ///     existence proof (RFC 4035 §3.1.3) are added: the NODATA proof, or the closest encloser
    ///     and wildcard proof for NXDOMAIN.
    /// </summary>
    private DnsMessage BuildNegativeResponse(
        DnsMessage request,
        CachedZone zone,
        bool isNxDomain,
        DnsName queryName,
        bool dnssecOk,
        SignedZoneData signedData
    )
    {
        DnsMessage response = CreateAnswerHeader(request, isAuthoritative: true);
        response.ResponseCode = isNxDomain
            ? DnsResponseCodeEnum.NxDomain
            : DnsResponseCodeEnum.NoError;

        IReadOnlyList<RecordEntity> soaRecords = zone.GetRecords(
            zone.ApexName,
            DnsResourceRecordTypeEnum.Soa
        );

        if (soaRecords.Count == 0)
            return response;

        RecordEntity soaRecord = soaRecords[0];

        if (DecodeTypedData(soaRecord) is not SoaRecordData soaData)
            return response;

        uint negativeCacheTtl = Math.Min(soaRecord.TimeToLiveSeconds, soaData.MinimumTtlSeconds);

        response.AuthorityRecords.Add(
            new DnsResourceRecord(
                zone.ApexName,
                DnsResourceRecordTypeEnum.Soa,
                DnsClassEnum.Internet,
                negativeCacheTtl,
                DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Soa, soaData)
            )
        );

        if (dnssecOk && signedData is not null)
        {
            response.AuthorityRecords.AddRange(
                signedData.GetSignatures(zone.ApexName, DnsResourceRecordTypeEnum.Soa)
            );
            response.AuthorityRecords.AddRange(
                isNxDomain
                    ? DenialOfExistenceProofBuilder.BuildNxDomainProof(
                        signedData,
                        zone.Zone,
                        zone.ApexName,
                        queryName
                    )
                    : DenialOfExistenceProofBuilder.BuildNoDataProof(
                        signedData,
                        zone.Zone,
                        queryName
                    )
            );
        }

        return response;
    }

    /// <summary>
    ///     This method builds the response header: the query's ID, opcode, RD and CD flags and
    ///     question section, with AA as given. RA is left unset. A request with EDNS gets an OPT
    ///     record back with its DNSSEC OK bit mirrored, so a validating client can tell the
    ///     server understood it. The question is echoed because clients use it to match the
    ///     response to their query.
    /// </summary>
    private static DnsMessage CreateAnswerHeader(DnsMessage request, bool isAuthoritative)
    {
        DnsMessage response = new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            IsAuthoritativeAnswer = isAuthoritative,
            RecursionDesired = request.RecursionDesired,
            CheckingDisabled = request.CheckingDisabled,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions = [.. request.Questions],
        };

        if (request.Edns is not null)
            response.Edns = new DnsEdnsOptions(
                0,
                Server.Core.Resolution.DnsQueryDispatcher.DefaultUdpPayloadSizeOctets,
                request.Edns.DnssecOk,
                []
            );

        return response;
    }

    /// <summary>
    ///     This method checks whether a name is the zone's apex or below it, to decide whether a
    ///     CNAME target or a name server can be looked up in this zone's data.
    /// </summary>
    private static bool IsWithinZone(DnsName name, CachedZone zone)
    {
        if (name.Labels.Count < zone.ApexName.Labels.Count)
            return false;

        DnsName suffix = new([.. name.Labels.Skip(name.Labels.Count - zone.ApexName.Labels.Count)]);
        return suffix == zone.ApexName;
    }

    /// <summary>
    ///     This method decodes each of a set of stored records into its wire form.
    /// </summary>
    private static List<DnsResourceRecord> ToWireRecords(
        IReadOnlyList<RecordEntity> records,
        DnsName ownerName
    ) => RecordEntityCodec.ToWireRecords(records, ownerName);

    /// <summary>
    ///     This method decodes a stored record's data into its typed model, for when the resolver
    ///     needs a value from it (an NS name, a CNAME target, the SOA minimum). It returns
    ///     <see langword="null" /> for a type with no registered codec.
    /// </summary>
    private static IDnsRecordData DecodeTypedData(RecordEntity record) =>
        RecordEntityCodec.DecodeTypedData(record);

    /// <summary>
    ///     This method appends the RRSIGs covering an RRset to a response section, with their owner
    ///     name set to the name the RRset is served under. That equals
    ///     <paramref name="signatureLookupName" /> for an exact match; for a wildcard answer the
    ///     RRSIG is stored under the <c>*</c> name but served under the queried name.
    /// </summary>
    private static void AppendRewrittenSignatures(
        List<DnsResourceRecord> target,
        SignedZoneData signedData,
        DnsName signatureLookupName,
        DnsResourceRecordTypeEnum type,
        DnsName servedOwnerName
    )
    {
        foreach (DnsResourceRecord rrsig in signedData.GetSignatures(signatureLookupName, type))
            target.Add(
                new DnsResourceRecord(
                    servedOwnerName,
                    rrsig.Type,
                    rrsig.Class,
                    rrsig.TimeToLive,
                    rrsig.Data
                )
            );
    }
}

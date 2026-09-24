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
///     This class is Ninlil's authoritative engine: it implements
///     <see cref="IDnsQueryResolver" /> by checking whether the query falls under a zone this
///     server hosts (via <see cref="AuthoritativeZoneCache" />) and, if so, fully answering it --
///     an exact match, a wildcard, a delegation referral, or a properly-formed negative response
///     -- rather than declining. A query for a name this server does not host at all is declined
///     (<see cref="DnsQueryResolution.NotHandled" />), letting <see cref="IDnsQueryDispatcher" />
///     offer it to the recursive engine instead.
///     <para>
///         When the querying client set EDNS(0)'s "DNSSEC OK" bit and the zone is signed, this
///         class also attaches whichever RRSIG/NSEC/NSEC3/DNSKEY/DS content
///         <see cref="Dnssec.SignedZoneCache" /> already holds for it -- never computing or
///         signing anything live, matching this project's deliberate stance against ever signing
///         per query (see <see cref="ZoneSigningService" />).
///     </para>
/// </summary>
/// <param name="zoneCache">The in-memory zone cache this resolver answers from.</param>
/// <param name="signedZoneCache">The in-memory signed-content cache this resolver answers DNSSEC data from.</param>
/// <param name="options">
///     This engine's own configuration, such as CHAOS query support. Bound via
///     <see cref="IOptionsMonitor{TOptions}" />, not the older, snapshotted
///     <see cref="IOptions{TOptions}" /> -- this resolver is registered as a singleton, so a write
///     <c>Ninlil.Web.Controllers.BrandingSettingsController.Patch</c> merges into the
///     configuration file must be read fresh on every CHAOS-class query this resolver answers, or
///     it would never be observed by this already-running instance at all.
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

        // A ZoneTypeEnum.Forwarder/SecondaryForwarder zone's own cache-resident content is its
        // synthetic SOA and per-address TXT records at its apex -- real, versioned wire content
        // that exists purely so Transfer.ZoneTransferService's own AXFR/IXFR path (which reads
        // straight out of AuthoritativeZoneCache, bypassing this resolver's own dispatch entirely
        // -- see TcpDnsListener/DnsOverTlsListener/DnsOverQuicListener, which all intercept an
        // Axfr/Ixfr question type before it ever reaches IDnsQueryDispatcher) has something real
        // to serve, and so a secondary's own freshness pre-check (an ordinary, unsigned SOA query
        // Refresh.SecondaryZoneRefreshService always sends before attempting a real, TSIG-gated
        // AXFR/IXFR pull -- see ZoneTransferPullCodec.QuerySoaSerialOverUdpAsync) has a real
        // answer to check the zone's own current serial against. Every *other* ordinary query for
        // a name under a Forwarder zone -- including its own apex TXT records, and any name other
        // than the exact apex -- must still fall through to Server.Forwarding's own
        // ForwarderQueryResolver exactly as it always has: answering an ordinary query for this
        // zone's own TXT content here would expose its configured upstream forwarder addresses to
        // any unauthenticated querying client, unlike AXFR/IXFR, which this project's own
        // ZoneTransferService always requires a valid TSIG signature for. An ordinary SOA answer
        // carries no such sensitive content -- a bare serial and generic-per-zone timing/contact
        // fields, the same information a public DNS zone's own SOA record always exposes by
        // design -- so this one narrow exception is safe.
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
    ///     This method answers the traditional CHAOS-class <c>version.bind</c>/<c>version.server</c>/
    ///     <c>hostname.bind</c>/<c>id.server</c> TXT queries used to fingerprint a name server,
    ///     when <see cref="AuthoritativeServerOptions.EnableChaosVersionQueries" /> allows it.
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
    ///     This method answers a query once a hosting zone has already been found, covering every
    ///     shape of authoritative answer: a delegation referral, an exact or wildcard match, a
    ///     CNAME chased one hop within the same zone, or a properly-formed negative response.
    /// </summary>
    private DnsMessage ResolveWithinZone(DnsMessage request, DnsQuestion question, CachedZone zone)
    {
        bool dnssecOk = request.Edns?.DnssecOk == true;
        SignedZoneData signedData = zone.Zone.IsDnssecSigned
            ? signedZoneCache.GetSignedData(zone.Zone.Id)
            : null;

        // A delegation between the zone's apex and the queried name means this zone is not
        // authoritative below that point; refer the client to the child zone's own servers
        // instead of answering (or failing to answer) from this zone's own data. Two exceptions:
        // an NS query landing exactly on the delegation point (those NS records genuinely live
        // in this zone), and a DS query landing exactly there too -- RFC 4035 §2.7/RFC 3658 both
        // treat a DS RRset as belonging to the *parent*, not something a referral can answer.
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

        // DNSKEY is never stored as an ordinary RecordEntity -- it only ever exists as
        // SignedZoneCache's own generated content -- so it needs its own path rather than
        // falling through to the ordinary exact-match lookup below, which would otherwise
        // (correctly, but for the wrong reason) treat every DNSKEY query as NODATA.
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

        // No record of the requested type exists at this exact name; if a CNAME exists there
        // instead, this server answers with the alias and, if the target stays within this same
        // zone, chases it one hop further before handing the response back.
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

        // The name itself exists -- either it owns records of some other type, or it is an
        // "empty non-terminal" that exists only because a name below it has records -- so this
        // is NODATA, not NXDOMAIN, and no wildcard may be synthesized for it: a wildcard only
        // ever covers a name that does not exist at all.
        if (zone.NameExists(question.Name) || zone.HasDescendantRecords(question.Name))
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

        // Even without an exact type match, a wildcard covering this name for a *different* type
        // still means the name itself exists (NODATA), not NXDOMAIN.
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
    ///     This method builds a delegation referral: no answer, the delegated subzone's NS
    ///     records in the Authority section, and, for any of those name servers that live inside
    ///     this same zone, their addresses as glue in the Additional section. When DNSSEC applies,
    ///     the Authority section also gets either the delegation's signed DS RRset (a secure
    ///     delegation) or the NSEC/NSEC3 proof that no DS exists at all (an insecure one) --
    ///     never both, per RFC 4035 §3.1.4.
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
    ///     This method builds a normal, positive answer from a set of matched records, attaching
    ///     each record's own covering RRSIG(s) when DNSSEC applies.
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
    ///     This method builds the answer for a name matched only by a wildcard, per RFC 1035
    ///     §4.3.3's expansion rule: the served owner name is the name actually queried for, not
    ///     the literal wildcard the data is stored under. When DNSSEC applies, this also attaches
    ///     the RRSIG RFC 4035 §3.1.3 describes -- itself rewritten to that same served owner name
    ///     even though its Labels field (computed at signing time, unchanged here) still reflects
    ///     the shorter, literal wildcard name, exactly the mismatch a validator uses to detect
    ///     wildcard synthesis occurred -- plus the proof that no closer, non-wildcard match exists.
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
    ///     This method builds the answer for a zone apex's own DNSKEY RRset, served straight out
    ///     of <see cref="SignedZoneCache" /> since it is never stored as an ordinary
    ///     <see cref="RecordEntity" /> at all.
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
    ///     This method builds the answer for a DS query landing exactly on a delegation point --
    ///     the one record type at that name this zone itself is authoritative for, per RFC 4035
    ///     §2.7, answered directly rather than referred the way every other type there is.
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
    ///     This method builds the answer for a name whose only match is a CNAME, following it one
    ///     hop further when the target stays inside the same zone, attaching each hop's own
    ///     covering RRSIG(s) when DNSSEC applies.
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
    ///     This method builds the modern, minimal response RFC 8482 recommends for an ANY query,
    ///     rather than the full, and far more abuse-prone, record set a naive implementation
    ///     would return. The synthesized HINFO record has no real signature of its own to attach
    ///     -- it does not exist in the zone at all -- so DNSSEC only applies here to the negative
    ///     response fallback below, not to the minimal answer itself.
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
            zone.NameExists(question.Name)
            || zone.HasDescendantRecords(question.Name)
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
    ///     This method builds an RFC 2308-compliant negative response: no answer, and the zone's
    ///     own SOA record in the Authority section (its time-to-live capped at the SOA's own
    ///     negative-caching minimum), so a resolver knows how long it may cache the non-existence
    ///     it just learned about. When DNSSEC applies, the Authority section also gets the SOA's
    ///     own RRSIG and the RFC 4035 §3.1.3.1/§3.1.3.2 denial-of-existence proof -- a single
    ///     record for NODATA, or the full closest-encloser-plus-wildcard proof for NXDOMAIN.
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
    ///     This method builds the header every response from this resolver shares: the query's
    ///     identifier and recursion-desired flag copied across, marked as a response, this
    ///     server's own recursive-availability left for the dispatcher to decide. A request that
    ///     carried EDNS(0) gets an OPT pseudo-record echoed back, its own DNSSEC OK bit mirrored,
    ///     so a validating client can confirm this server actually understood the request rather
    ///     than silently ignoring it. The question section is always echoed back too -- RFC 1035
    ///     never technically mandates it, but every real resolver relies on it to correlate a
    ///     response with the query that produced it, and its absence is exactly the kind of thing
    ///     that goes unnoticed by a check that only looks at the answer content rather than the
    ///     full wire response.
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
    ///     This method checks whether a name falls inside a zone -- either the zone's own apex,
    ///     or a descendant of it -- used to decide whether a CNAME target or an NS record's
    ///     nameserver can be resolved further using this same zone's own data.
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
    ///     This method decodes a stored record's own JSON data into its strongly-typed model,
    ///     used where a resolver needs to inspect a record's *meaning* -- an NS record's
    ///     nameserver, a CNAME's target, a SOA's negative-caching minimum -- rather than just
    ///     serve it as-is. Returns <see langword="null" /> for a record type with no registered
    ///     codec.
    /// </summary>
    private static IDnsRecordData DecodeTypedData(RecordEntity record) =>
        RecordEntityCodec.DecodeTypedData(record);

    /// <summary>
    ///     This method appends a signed RRset's own covering RRSIG(s) to a response section,
    ///     rewriting each one's owner name to whatever name the RRset itself is actually being
    ///     served under -- identical to <paramref name="signatureLookupName" /> for an ordinary
    ///     exact match, but different for a wildcard-synthesized answer, where the RRSIG was
    ///     computed under the literal wildcard name yet must be served under the name the client
    ///     actually queried for.
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

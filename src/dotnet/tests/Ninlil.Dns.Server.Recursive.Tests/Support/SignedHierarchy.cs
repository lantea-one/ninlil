using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests.Support;

/// <summary>
///     This class builds a small signed DNS hierarchy for chain-of-trust tests and serves it
///     through a <see cref="FakeUpstreamDnsClient" />: every zone has its own server address, its
///     records, delegations (with a DS, or an NSEC proof of no DS), and, when signed, an Ed25519
///     key, RRSIGs over every authoritative RRset and an NSEC chain. A real
///     <see cref="Resolution.IterativeResolutionEngine" /> and
///     <see cref="Caching.RecursiveCache" /> can then walk it exactly as they walk the internet.
///     <para>
///         <see cref="CreateStandard" /> builds the fixture the B8-5 tests share: signed root,
///         <c>com</c> and <c>example.com</c>; an unsigned <c>insecure.com</c>; a signed
///         <c>sub.example.com</c> and an unsigned <c>plain.example.com</c>, both served by the
///         same server as <c>example.com</c> (so no referral is seen); and
///         <c>alias.example.com</c>, a CNAME to <c>missing.example.com</c>, which does not exist.
///     </para>
///     <para>
///         The responder is a minimal authoritative server: it answers from the deepest zone it
///         serves for a name (a DS query uses the zone above the name), refers a name below a
///         delegation, and otherwise answers positively, NODATA or NXDOMAIN with the NSEC records
///         that prove it. The only wildcard is the A record at <c>*.wild.example.com</c>. A test replaces single answers with
///         <see cref="FakeUpstreamDnsClient.AddResponse" />, usually after fetching the genuine one
///         with <see cref="Respond" />.
///     </para>
/// </summary>
public sealed class SignedHierarchy
{
    /// <summary>This field is the address of the root server, also the only root hint.</summary>
    public static readonly IPAddress RootAddress = IPAddress.Parse("192.0.2.1");

    /// <summary>This field is the address of the <c>com</c> server.</summary>
    public static readonly IPAddress ComAddress = IPAddress.Parse("192.0.2.2");

    /// <summary>
    ///     This field is the address of the server for <c>example.com</c>, <c>sub.example.com</c>
    ///     and <c>plain.example.com</c>.
    /// </summary>
    public static readonly IPAddress ExampleAddress = IPAddress.Parse("192.0.2.3");

    /// <summary>This field is the address of the <c>insecure.com</c> server.</summary>
    public static readonly IPAddress InsecureAddress = IPAddress.Parse("192.0.2.4");

    private const uint RecordTtl = 300;
    private readonly List<Zone> _zones = [];
    private bool _finished;

    /// <summary>
    ///     This constructor creates an empty hierarchy.
    /// </summary>
    /// <param name="clock">
    ///     The clock RRSIG validity windows are built around: they open a day before it and close
    ///     thirty days after it, unless a test passes its own times.
    /// </param>
    public SignedHierarchy(TimeProvider clock)
    {
        Clock = clock;
        Client.Responder = Respond;
    }

    /// <summary>This property contains the fake upstream that serves the hierarchy.</summary>
    public FakeUpstreamDnsClient Client { get; } = new();

    /// <summary>This property contains the clock the signatures are dated around.</summary>
    public TimeProvider Clock { get; }

    /// <summary>This property contains the key and signature builder.</summary>
    public DnssecTestFixture Fixture { get; } = new();

    /// <summary>This property contains the root zone.</summary>
    public Zone Root { get; private set; }

    /// <summary>This property contains every zone of the hierarchy.</summary>
    public IReadOnlyList<Zone> Zones => _zones;

    /// <summary>
    ///     This property contains the trust anchor of the root key, as a trust anchor store would
    ///     hold it.
    /// </summary>
    public IReadOnlyList<TrustAnchor> Anchors =>
        Root.IsSigned ? [Fixture.BuildTrustAnchor(DnsName.Root, Root.Key)] : [];

    /// <summary>
    ///     This property contains the recursive options matching the hierarchy: one root hint at
    ///     <see cref="RootAddress" /> and QNAME minimization off (a test turns it on).
    /// </summary>
    public RecursiveServerOptions CreateOptions(bool minimize = false) =>
        new()
        {
            EnableQnameMinimization = minimize,
            RootHintServersOverride =
            [
                new RootHintServerOption
                {
                    Name = "a.root.test",
                    Addresses = [RootAddress.ToString()],
                },
            ],
        };

    /// <summary>
    ///     This method builds the hierarchy described in the class summary.
    /// </summary>
    /// <param name="clock">The clock the signatures are dated around.</param>
    /// <param name="exampleSignatureLifetime">
    ///     How long the signatures of <c>example.com</c> stay valid; thirty days when omitted. A
    ///     short value lets a test move the clock past them.
    /// </param>
    public static SignedHierarchy CreateStandard(
        TimeProvider clock,
        TimeSpan? exampleSignatureLifetime = null
    )
    {
        SignedHierarchy hierarchy = new(clock);
        Zone root = hierarchy.AddZone(".", RootAddress, true, "a.root.test");
        Zone com = hierarchy.AddZone("com", ComAddress, true);
        Zone example = hierarchy.AddZone("example.com", ExampleAddress, true);
        Zone insecure = hierarchy.AddZone("insecure.com", InsecureAddress, false);
        Zone sub = hierarchy.AddZone("sub.example.com", ExampleAddress, true, "ns.example.com");
        Zone plain = hierarchy.AddZone(
            "plain.example.com",
            ExampleAddress,
            false,
            "ns.example.com"
        );

        hierarchy.Delegate(root, com);
        hierarchy.Delegate(com, example);
        hierarchy.Delegate(com, insecure);
        hierarchy.Delegate(example, sub);
        hierarchy.Delegate(example, plain);

        if (exampleSignatureLifetime is { } lifetime)
            example.SignatureLifetime = lifetime;

        example.AddA("www.example.com", "192.0.2.10");
        example.AddA("*.wild.example.com", "192.0.2.20");
        example.AddCname("web.example.com", "www.example.com");
        example.AddCname("toinsecure.example.com", "www.insecure.com");
        example.AddCname("alias.example.com", "missing.example.com");
        sub.AddA("www.sub.example.com", "192.0.2.11");
        plain.AddA("www.plain.example.com", "192.0.2.12");
        insecure.AddA("www.insecure.com", "192.0.2.13");

        hierarchy.Finish();

        return hierarchy;
    }

    /// <summary>
    ///     This method adds a zone with its SOA, an NS record for <paramref name="nameServer" />
    ///     and, when signed, a key.
    /// </summary>
    /// <param name="apex">The zone apex in presentation form.</param>
    /// <param name="server">The address of the server that holds the zone.</param>
    /// <param name="signed">Whether the zone has a key and signatures.</param>
    /// <param name="nameServer">The name server name; <c>ns.&lt;apex&gt;</c> when omitted.</param>
    public Zone AddZone(string apex, IPAddress server, bool signed, string nameServer = null)
    {
        DnsName apexName = DnsName.Parse(apex);
        Zone zone = new(
            this,
            apexName,
            server,
            signed ? Fixture.GenerateZoneKey(apexName, 3600, isSecureEntryPoint: true) : null,
            DnsName.Parse(nameServer ?? (apexName.IsRoot ? "ns.root.test" : $"ns.{apex}"))
        );

        zone.Add(
            new DnsResourceRecord(
                apexName,
                DnsResourceRecordTypeEnum.Soa,
                DnsClassEnum.Internet,
                RecordTtl,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Soa,
                    new SoaRecordData(
                        zone.NameServer,
                        DnsName.Parse("hostmaster.test"),
                        1,
                        3600,
                        600,
                        86400,
                        RecordTtl
                    )
                )
            )
        );
        zone.Add(
            new DnsResourceRecord(
                apexName,
                DnsResourceRecordTypeEnum.NS,
                DnsClassEnum.Internet,
                3600,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.NS,
                    new NsRecordData(zone.NameServer)
                )
            )
        );

        if (signed)
            zone.Add(zone.Key.DnsKeyRecord);

        if (apexName.IsRoot)
            Root = zone;

        _zones.Add(zone);

        return zone;
    }

    /// <summary>
    ///     This method returns the zone with this apex.
    /// </summary>
    public Zone ZoneByName(string apex) => _zones.Single(zone => zone.Apex == DnsName.Parse(apex));

    /// <summary>
    ///     This method delegates <paramref name="child" /> from <paramref name="parent" />: the
    ///     child's NS record becomes a delegation in the parent, with glue; the parent publishes a
    ///     DS for a signed child.
    /// </summary>
    /// <param name="parent">The delegating zone.</param>
    /// <param name="child">The delegated zone.</param>
    /// <param name="unsupportedDsDigest">
    ///     Whether the DS uses digest type 3 (GOST R 34.11-94), which the validator cannot compute,
    ///     instead of a SHA-256 digest of the child's key.
    /// </param>
    public void Delegate(Zone parent, Zone child, bool unsupportedDsDigest = false)
    {
        parent.Delegations.Add(child.Apex);
        parent.Add(
            new DnsResourceRecord(
                child.Apex,
                DnsResourceRecordTypeEnum.NS,
                DnsClassEnum.Internet,
                3600,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.NS,
                    new NsRecordData(child.NameServer)
                )
            )
        );
        parent.Glue[child.NameServer] =
        [
            new DnsResourceRecord(
                child.NameServer,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                3600,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(child.Server)
                )
            ),
        ];

        if (child.IsSigned && unsupportedDsDigest)
            parent.Add(
                new DnsResourceRecord(
                    child.Apex,
                    DnsResourceRecordTypeEnum.DS,
                    DnsClassEnum.Internet,
                    3600,
                    DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.DS,
                        new DsRecordData(
                            child.Key.KeyTag,
                            DnssecAlgorithmEnum.Ed25519,
                            3,
                            new byte[32]
                        )
                    )
                )
            );
        else if (child.IsSigned)
            parent.Add(Fixture.BuildDs(child.Apex, child.Key));
    }

    /// <summary>
    ///     This method signs every authoritative RRset and builds the NSEC chains. Call it after
    ///     the last record is added; the hierarchy cannot change afterwards.
    /// </summary>
    public void Finish()
    {
        DateTimeOffset now = Clock.GetUtcNow();

        foreach (Zone zone in _zones.Where(zone => zone.IsSigned))
        {
            DateTimeOffset inception = now.AddDays(-1);
            DateTimeOffset expiration = now.Add(zone.SignatureLifetime);

            // The NS RRset at a delegation is the parent's glue-like data and stays unsigned
            // (RFC 4035 §2.2).
            foreach (
                (
                    (DnsName name, DnsResourceRecordTypeEnum type),
                    List<DnsResourceRecord> rrset
                ) in zone
                    .Rrsets.Where(pair =>
                        !(
                            pair.Key.Type == DnsResourceRecordTypeEnum.NS
                            && zone.Delegations.Contains(pair.Key.Name)
                        )
                    )
                    .ToList()
            )
                zone.Signatures[(name, type)] = Fixture.Sign(
                    zone.Key,
                    zone.Apex,
                    rrset,
                    inception,
                    expiration,
                    // RFC 4034 §3.1.3: Labels does not count a leading "*", which is how a
                    // validator learns the RRset can be a wildcard expansion.
                    labels: (byte)(name.Labels.Count - (IsWildcardOwner(name) ? 1 : 0))
                );

            List<DnsName> owners = [.. zone.Rrsets.Keys.Select(key => key.Name).Distinct()];
            owners.Sort(DnssecCanonicalizationComparer);

            for (int i = 0; i < owners.Count; i++)
            {
                DnsName owner = owners[i];
                List<DnsResourceRecordTypeEnum> types =
                [
                    .. zone.Rrsets.Keys.Where(key => key.Name == owner).Select(key => key.Type),
                    DnsResourceRecordTypeEnum.RrSig,
                    DnsResourceRecordTypeEnum.Nsec,
                ];
                DnsResourceRecord nsec = new(
                    owner,
                    DnsResourceRecordTypeEnum.Nsec,
                    DnsClassEnum.Internet,
                    RecordTtl,
                    DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.Nsec,
                        new NsecRecordData(owners[(i + 1) % owners.Count], types)
                    )
                );
                zone.Nsecs.Add(nsec);
                zone.Signatures[(owner, DnsResourceRecordTypeEnum.Nsec)] = Fixture.Sign(
                    zone.Key,
                    zone.Apex,
                    [nsec],
                    inception,
                    expiration,
                    labels: (byte)(owner.Labels.Count - (IsWildcardOwner(owner) ? 1 : 0))
                );
            }
        }

        _finished = true;
    }

    /// <summary>
    ///     This method answers one query the way the zone's server would, or returns
    ///     <see langword="null" /> for an address no zone uses.
    /// </summary>
    public DnsMessage Respond(IPAddress server, DnsName name, DnsResourceRecordTypeEnum type)
    {
        if (!_finished)
            throw new InvalidOperationException("Call Finish before querying the hierarchy.");

        Zone zone = _zones
            .Where(candidate =>
                candidate.Server.Equals(server)
                && IsAtOrBelow(name, candidate.Apex)
                && (type != DnsResourceRecordTypeEnum.DS || candidate.Apex != name)
            )
            .OrderByDescending(candidate => candidate.Apex.Labels.Count)
            .FirstOrDefault();

        if (zone is null)
            return new DnsMessage { IsResponse = true, ResponseCode = DnsResponseCodeEnum.Refused };

        // A name at or below a delegation is a referral, except the DS record at the delegation
        // itself, which the parent answers (RFC 4035 §3.1.4.1).
        DnsName cut = zone
            .Delegations.Where(delegation =>
                IsAtOrBelow(name, delegation)
                && !(delegation == name && type == DnsResourceRecordTypeEnum.DS)
            )
            .OrderBy(delegation => delegation.Labels.Count)
            .FirstOrDefault();

        if (cut is not null)
            return BuildReferral(zone, cut);

        DnsMessage response = new()
        {
            IsResponse = true,
            IsAuthoritativeAnswer = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };

        if (zone.Rrsets.TryGetValue((name, type), out List<DnsResourceRecord> exact))
        {
            AddWithSignature(response.Answers, zone, name, type, exact);

            return response;
        }

        if (
            type != DnsResourceRecordTypeEnum.Cname
            && zone.Rrsets.TryGetValue(
                (name, DnsResourceRecordTypeEnum.Cname),
                out List<DnsResourceRecord> cname
            )
        )
        {
            AddWithSignature(response.Answers, zone, name, DnsResourceRecordTypeEnum.Cname, cname);

            return response;
        }

        DnsMessage wildcardAnswer = TryBuildWildcardAnswer(zone, name, type);

        if (wildcardAnswer is not null)
            return wildcardAnswer;

        DnsResourceRecord soa = zone.Rrsets[(zone.Apex, DnsResourceRecordTypeEnum.Soa)][0];
        AddWithSignature(
            response.AuthorityRecords,
            zone,
            zone.Apex,
            DnsResourceRecordTypeEnum.Soa,
            [soa]
        );

        bool nameExists = zone.ExistingNames().Contains(name);

        if (!nameExists)
            response.ResponseCode = DnsResponseCodeEnum.NxDomain;

        if (!zone.IsSigned)
            return response;

        List<DnsResourceRecord> proofs = [];

        if (zone.Nsecs.Any(nsec => nsec.Name == name))
            proofs.Add(zone.Nsecs.Single(nsec => nsec.Name == name));
        else
        {
            proofs.Add(zone.CoveringNsec(name));

            // An NXDOMAIN also needs the wildcard at the closest encloser to be covered
            // (RFC 4035 §3.1.3.2).
            if (!nameExists)
            {
                DnsName closestEncloser = zone.ClosestEncloser(name);
                proofs.Add(zone.CoveringNsec(closestEncloser.PrependLabels("*")));
            }
        }

        foreach (DnsResourceRecord nsec in proofs.DistinctBy(record => record.Name))
            AddWithSignature(
                response.AuthorityRecords,
                zone,
                nsec.Name,
                DnsResourceRecordTypeEnum.Nsec,
                [nsec]
            );

        return response;
    }

    /// <summary>
    ///     This method builds the answer a server gives for a name that does not exist when the
    ///     closest encloser has a wildcard record of the type (RFC 4035 §3.1.3.3): the records and
    ///     the wildcard's RRSIG under the queried name (the RRSIG keeps the wildcard's Labels), and
    ///     the NSEC that covers the next closer name in the authority section.
    /// </summary>
    /// <returns>The answer, or <see langword="null" /> when no wildcard applies.</returns>
    private static DnsMessage TryBuildWildcardAnswer(
        Zone zone,
        DnsName name,
        DnsResourceRecordTypeEnum type
    )
    {
        if (zone.ExistingNames().Contains(name))
            return null;

        DnsName closestEncloser = zone.ClosestEncloser(name);
        DnsName wildcard = closestEncloser.PrependLabels("*");

        if (!zone.Rrsets.TryGetValue((wildcard, type), out List<DnsResourceRecord> rrset))
            return null;

        DnsMessage response = new()
        {
            IsResponse = true,
            IsAuthoritativeAnswer = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };
        response.Answers.AddRange(
            rrset.Select(record => new DnsResourceRecord(
                name,
                record.Type,
                record.Class,
                record.TimeToLive,
                record.Data
            ))
        );

        if (zone.Signatures.TryGetValue((wildcard, type), out DnsResourceRecord signature))
            response.Answers.Add(
                new DnsResourceRecord(
                    name,
                    signature.Type,
                    signature.Class,
                    signature.TimeToLive,
                    signature.Data
                )
            );

        if (zone.IsSigned)
        {
            DnsName nextCloser = new([
                .. name.Labels.Skip(name.Labels.Count - closestEncloser.Labels.Count - 1),
            ]);
            DnsResourceRecord covering = zone.CoveringNsec(nextCloser);
            AddWithSignature(
                response.AuthorityRecords,
                zone,
                covering.Name,
                DnsResourceRecordTypeEnum.Nsec,
                [covering]
            );
        }

        return response;
    }

    private static bool IsWildcardOwner(DnsName name) =>
        name.Labels.Count > 0 && name.Labels[0].Length == 1 && name.Labels[0][0] == (byte)'*';

    /// <summary>
    ///     This method builds a referral to <paramref name="cut" />: the delegation NS records,
    ///     the DS (or the NSEC proof that there is none) in the authority section, and glue.
    /// </summary>
    private static DnsMessage BuildReferral(Zone zone, DnsName cut)
    {
        DnsMessage response = new()
        {
            IsResponse = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };
        List<DnsResourceRecord> nsRecords = zone.Rrsets[(cut, DnsResourceRecordTypeEnum.NS)];
        response.AuthorityRecords.AddRange(nsRecords);

        if (zone.IsSigned)
        {
            if (zone.Rrsets.TryGetValue((cut, DnsResourceRecordTypeEnum.DS), out var ds))
                AddWithSignature(
                    response.AuthorityRecords,
                    zone,
                    cut,
                    DnsResourceRecordTypeEnum.DS,
                    ds
                );
            else
                AddWithSignature(
                    response.AuthorityRecords,
                    zone,
                    cut,
                    DnsResourceRecordTypeEnum.Nsec,
                    [zone.Nsecs.Single(nsec => nsec.Name == cut)]
                );
        }

        foreach (DnsResourceRecord ns in nsRecords)
        {
            DnsName nameServer = (
                (NsRecordData)
                    DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.NS, ns.Data)
            ).NameServer;

            if (zone.Glue.TryGetValue(nameServer, out List<DnsResourceRecord> glue))
                response.AdditionalRecords.AddRange(glue);
        }

        return response;
    }

    private static void AddWithSignature(
        List<DnsResourceRecord> section,
        Zone zone,
        DnsName name,
        DnsResourceRecordTypeEnum type,
        List<DnsResourceRecord> rrset
    )
    {
        section.AddRange(rrset);

        if (zone.Signatures.TryGetValue((name, type), out DnsResourceRecord signature))
            section.Add(signature);
    }

    private static bool IsAtOrBelow(DnsName name, DnsName ancestor)
    {
        int skip = name.Labels.Count - ancestor.Labels.Count;

        return skip >= 0 && new DnsName([.. name.Labels.Skip(skip)]) == ancestor;
    }

    private static int DnssecCanonicalizationComparer(DnsName left, DnsName right) =>
        Ninlil.Dns.Common.Wire.DnssecCanonicalization.CompareCanonicalOrder(left, right);

    /// <summary>
    ///     This class is one zone of a <see cref="SignedHierarchy" />: records, delegations, key,
    ///     signatures and NSEC chain.
    /// </summary>
    public sealed class Zone
    {
        private readonly SignedHierarchy _owner;

        internal Zone(
            SignedHierarchy owner,
            DnsName apex,
            IPAddress server,
            DnssecTestFixture.ZoneKey key,
            DnsName nameServer
        )
        {
            _owner = owner;
            Apex = apex;
            Server = server;
            Key = key;
            NameServer = nameServer;
        }

        /// <summary>This property contains the zone apex.</summary>
        public DnsName Apex { get; }

        /// <summary>This property contains the address of the server that holds the zone.</summary>
        public IPAddress Server { get; }

        /// <summary>This property contains the zone's key, or <see langword="null" /> if unsigned.</summary>
        public DnssecTestFixture.ZoneKey Key { get; }

        /// <summary>This property contains the zone's name server name.</summary>
        public DnsName NameServer { get; }

        /// <summary>
        ///     This property contains how long the zone's signatures stay valid after the clock's
        ///     current time when <see cref="SignedHierarchy.Finish" /> signs them.
        /// </summary>
        public TimeSpan SignatureLifetime { get; set; } = TimeSpan.FromDays(30);

        /// <summary>This property tells whether the zone has a key.</summary>
        public bool IsSigned => Key is not null;

        internal Dictionary<
            (DnsName Name, DnsResourceRecordTypeEnum Type),
            List<DnsResourceRecord>
        > Rrsets { get; } = [];

        internal HashSet<DnsName> Delegations { get; } = [];

        internal Dictionary<DnsName, List<DnsResourceRecord>> Glue { get; } = [];

        internal Dictionary<
            (DnsName Name, DnsResourceRecordTypeEnum Type),
            DnsResourceRecord
        > Signatures { get; } = [];

        internal List<DnsResourceRecord> Nsecs { get; } = [];

        /// <summary>
        ///     This method adds a record to the zone. Call it before
        ///     <see cref="SignedHierarchy.Finish" />.
        /// </summary>
        public void Add(DnsResourceRecord record)
        {
            if (_owner._finished)
                throw new InvalidOperationException("The hierarchy is finished.");

            if (!Rrsets.TryGetValue((record.Name, record.Type), out List<DnsResourceRecord> rrset))
                Rrsets[(record.Name, record.Type)] = rrset = [];

            rrset.Add(record);
        }

        /// <summary>This method adds an A record.</summary>
        public void AddA(string name, string address) =>
            Add(
                new DnsResourceRecord(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    RecordTtl,
                    DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.A,
                        new ARecordData(IPAddress.Parse(address))
                    )
                )
            );

        /// <summary>This method adds a CNAME record.</summary>
        public void AddCname(string name, string target) =>
            Add(
                new DnsResourceRecord(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.Cname,
                    DnsClassEnum.Internet,
                    RecordTtl,
                    DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.Cname,
                        new CnameRecordData(DnsName.Parse(target))
                    )
                )
            );

        /// <summary>
        ///     This method returns every name that exists in the zone: owners and the empty
        ///     non-terminals above them.
        /// </summary>
        internal HashSet<DnsName> ExistingNames()
        {
            HashSet<DnsName> names = [];

            foreach (DnsName owner in Rrsets.Keys.Select(key => key.Name))
                for (int skip = 0; owner.Labels.Count - skip >= Apex.Labels.Count; skip++)
                    names.Add(new DnsName([.. owner.Labels.Skip(skip)]));

            return names;
        }

        /// <summary>
        ///     This method returns the longest ancestor of a name that exists in the zone.
        /// </summary>
        internal DnsName ClosestEncloser(DnsName name)
        {
            HashSet<DnsName> existing = ExistingNames();

            for (int skip = 1; skip <= name.Labels.Count; skip++)
            {
                DnsName candidate = new([.. name.Labels.Skip(skip)]);

                if (existing.Contains(candidate))
                    return candidate;
            }

            return Apex;
        }

        /// <summary>
        ///     This method returns the NSEC whose span covers a name that has no NSEC of its own.
        /// </summary>
        internal DnsResourceRecord CoveringNsec(DnsName name) =>
            Nsecs
                .Where(nsec => DnssecCanonicalizationComparer(nsec.Name, name) < 0)
                .OrderBy(
                    nsec => nsec.Name,
                    Comparer<DnsName>.Create(DnssecCanonicalizationComparer)
                )
                .LastOrDefault()
            ?? Nsecs
                .OrderBy(
                    nsec => nsec.Name,
                    Comparer<DnsName>.Create(DnssecCanonicalizationComparer)
                )
                .Last();
    }
}

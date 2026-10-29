using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.CloudSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CloudSync;

/// <summary>
///     This class tests <see cref="Route53DnsProvider" /> against <see cref="FakeRoute53Server" />, a
///     stateful Kestrel fake of the Route 53 API on a loopback port. The
///     <see cref="Amazon.Route53.AmazonRoute53Client" /> still signs requests with SigV4, but the
///     fake does not check the signatures. It covers listing (several values per set, escaped names,
///     paging), set-level create and delete, and the skipped alias and routing-policy sets. There is
///     no test against a live AWS account.
/// </summary>
public sealed class Route53DnsProviderTests : IAsyncLifetime
{
    private const string ZoneId = "Z12345";

    private readonly FakeRoute53Server _fake = new();
    private readonly CapturingLogger _logger = new();

    /// <summary>
    ///     This class is an <see cref="ILogger" /> that keeps every formatted message with its level, so
    ///     a test can assert that a skipped alias set is reported as a Warning.
    /// </summary>
    private sealed class CapturingLogger : ILogger<Route53DnsProvider>
    {
        /// <summary>The captured messages, guarded by a lock on the list itself.</summary>
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            lock (Entries)
                Entries.Add((logLevel, formatter(state, exception)));
        }

        /// <summary>This method returns how many messages were logged at one level.</summary>
        public int CountAt(LogLevel level)
        {
            lock (Entries)
                return Entries.Count(entry => entry.Level == level);
        }
    }

    public async Task InitializeAsync()
    {
        await _fake.StartAsync();

        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "www.example.com.",
                Type = "A",
                Values = ["192.0.2.50"],
            }
        );
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "_sip._tcp.example.com.",
                Type = "SRV",
                Values = ["10 5 5060 sipserver.example.com"],
            }
        );
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "example.com.",
                Type = "CAA",
                Values = ["0 issue \"letsencrypt.org\""],
            }
        );
    }

    public async Task DisposeAsync() => await _fake.DisposeAsync();

    private Dictionary<string, string> Credentials =>
        new()
        {
            ["AccessKeyId"] = "AKIAFAKE",
            ["SecretAccessKey"] = "fake-secret",
            ["ServiceUrl"] = _fake.Url,
        };

    /// <summary>The body of the last change request the fake received.</summary>
    private string LastChangeRequestBody => _fake.ChangeRequests().LastOrDefault();

    /// <summary>This method builds a provider that logs to <see cref="_logger" />.</summary>
    private Route53DnsProvider CreateProvider() => new(_logger);

    /// <summary>This method builds a record of one of the single-value types.</summary>
    private static DnsResourceRecord Record(
        DnsResourceRecordTypeEnum type,
        string name,
        string content,
        uint ttl = 300
    ) => CloudDnsRecordMapper.ToWireRecord(type, name, content, ttl);

    [Fact]
    public async Task ListRecordsParsesTheFakeXmlResponse()
    {
        Route53DnsProvider provider = CreateProvider();

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            ZoneId
        );

        Assert.Equal(3, records.Count);
        CloudDnsProviderRecord aRecord = Assert.Single(
            records,
            r => r.Record.Type == DnsResourceRecordTypeEnum.A
        );
        Assert.Equal("www.example.com.|A", aRecord.ProviderRecordId);
    }

    [Fact]
    public async Task ListRecordsParsesTheSpaceSeparatedSrvValue()
    {
        Route53DnsProvider provider = CreateProvider();

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            ZoneId
        );

        CloudDnsProviderRecord srvRecord = Assert.Single(
            records,
            r => r.Record.Type == DnsResourceRecordTypeEnum.Srv
        );
        (ushort priority, ushort weight, ushort port, string target) =
            CloudDnsRecordMapper.FromSrvWireRecord(srvRecord.Record);

        Assert.Equal("_sip._tcp.example.com.|SRV", srvRecord.ProviderRecordId);
        Assert.Equal((ushort)10, priority);
        Assert.Equal((ushort)5, weight);
        Assert.Equal((ushort)5060, port);
        Assert.Equal("sipserver.example.com", target);
    }

    [Fact]
    public async Task ListRecordsParsesTheQuotedCaaValue()
    {
        Route53DnsProvider provider = CreateProvider();

        IReadOnlyList<CloudDnsProviderRecord> records = await provider.ListRecordsAsync(
            Credentials,
            ZoneId
        );

        CloudDnsProviderRecord caaRecord = Assert.Single(
            records,
            r => r.Record.Type == DnsResourceRecordTypeEnum.Caa
        );
        (bool isCritical, string tag, string value) = CloudDnsRecordMapper.FromCaaWireRecord(
            caaRecord.Record
        );

        Assert.Equal("example.com.|CAA", caaRecord.ProviderRecordId);
        Assert.False(isCritical);
        Assert.Equal("issue", tag);
        Assert.Equal("letsencrypt.org", value);
    }

    [Fact]
    public async Task CreateRecordForSrvBuildsTheSpaceSeparatedValue()
    {
        Route53DnsProvider provider = CreateProvider();
        DnsResourceRecord record = CloudDnsRecordMapper.ToSrvWireRecord(
            "_sips._tcp.example.com",
            10,
            5,
            5060,
            "sipserver.example.com",
            300
        );

        await provider.CreateRecordAsync(Credentials, ZoneId, record);

        Assert.NotNull(LastChangeRequestBody);
        Assert.Contains("10 5 5060 sipserver.example.com.", LastChangeRequestBody);
    }

    [Fact]
    public async Task CreateRecordForCaaBuildsTheQuotedValueWithTheCriticalFlagBitSet()
    {
        Route53DnsProvider provider = CreateProvider();
        DnsResourceRecord record = CloudDnsRecordMapper.ToCaaWireRecord(
            "example.com",
            isCritical: true,
            "issue",
            "letsencrypt.org",
            300
        );

        await provider.CreateRecordAsync(Credentials, ZoneId, record);

        Assert.NotNull(LastChangeRequestBody);
        Assert.Contains("128 issue \"letsencrypt.org\"", LastChangeRequestBody);
    }

    [Fact]
    public async Task CreateRecordForMxPacksPriorityIntoTheSingleValueString()
    {
        Route53DnsProvider provider = CreateProvider();
        DnsResourceRecord record = CloudDnsRecordMapper.ToWireRecord(
            DnsResourceRecordTypeEnum.MX,
            "example.com",
            "mail.example.com",
            300,
            10
        );

        await provider.CreateRecordAsync(Credentials, ZoneId, record);

        Assert.NotNull(LastChangeRequestBody);
        Assert.Contains("10 mail.example.com.", LastChangeRequestBody);
    }

    [Fact]
    public async Task CreateRecordForTxtWrapsTheValueInDoubleQuotes()
    {
        Route53DnsProvider provider = CreateProvider();
        DnsResourceRecord record = CloudDnsRecordMapper.ToWireRecord(
            DnsResourceRecordTypeEnum.Txt,
            "example.com",
            "hello world",
            300
        );

        await provider.CreateRecordAsync(Credentials, ZoneId, record);

        Assert.Contains("\"hello world\"", LastChangeRequestBody);
    }

    [Fact]
    public async Task DeleteRecordSendsADeleteChangeAction()
    {
        Route53DnsProvider provider = CreateProvider();

        await provider.DeleteRecordAsync(
            Credentials,
            ZoneId,
            new CloudDnsProviderRecord(
                "www.example.com.|A",
                CloudDnsRecordMapper.ToWireRecord(
                    DnsResourceRecordTypeEnum.A,
                    "www.example.com",
                    "192.0.2.50",
                    300
                )
            )
        );

        Assert.Contains("DELETE", LastChangeRequestBody);
    }

    /// <summary>
    ///     This test checks that every value of a record set becomes one record with the set's TTL
    ///     and the set's ID, instead of only the first value being read.
    /// </summary>
    [Fact]
    public async Task ListRecords_ForASetWithThreeValues_ReturnsThreeRecordsWithTheSetsTtl()
    {
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "multi.example.com.",
                Type = "A",
                Ttl = 120,
                Values = ["192.0.2.1", "192.0.2.2", "192.0.2.3"],
            }
        );

        IReadOnlyList<CloudDnsProviderRecord> records = await CreateProvider()
            .ListRecordsAsync(Credentials, ZoneId);

        List<CloudDnsProviderRecord> multi =
        [
            .. records.Where(r => r.ProviderRecordId == "multi.example.com.|A"),
        ];
        Assert.Equal(3, multi.Count);
        Assert.All(multi, r => Assert.Equal(120u, r.Record.TimeToLive));
        Assert.Equal(
            3,
            multi
                .Select(r => CloudDnsRecordMapper.FromWireRecord(r.Record).Content)
                .Distinct()
                .Count()
        );
    }

    /// <summary>
    ///     This test checks that a set listed across several pages is read completely.
    /// </summary>
    [Fact]
    public async Task ListRecords_ForMoreSetsThanOnePage_FollowsTheNextRecordName()
    {
        for (int index = 0; index < 120; index++)
            _fake.AddSet(
                ZoneId,
                new FakeRoute53Server.FakeRecordSet
                {
                    Name = $"host{index}.example.com.",
                    Type = "A",
                    Values = ["192.0.2.9"],
                }
            );

        IReadOnlyList<CloudDnsProviderRecord> records = await CreateProvider()
            .ListRecordsAsync(Credentials, ZoneId);

        Assert.Equal(123, records.Count);
    }

    /// <summary>
    ///     This test checks that Route 53's octal escape for the asterisk is read as a wildcard
    ///     owner, not as the decimal escape <c>\052</c> (the character with value 52, "4").
    /// </summary>
    [Fact]
    public async Task ListRecords_ForAnOctalEscapedWildcard_ReturnsTheWildcardOwner()
    {
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "\\052.example.com.",
                Type = "A",
                Values = ["192.0.2.7"],
            }
        );

        IReadOnlyList<CloudDnsProviderRecord> records = await CreateProvider()
            .ListRecordsAsync(Credentials, ZoneId);

        CloudDnsProviderRecord wildcard = Assert.Single(
            records,
            r => r.ProviderRecordId == "\\052.example.com.|A"
        );
        Assert.Equal("*.example.com.", wildcard.Record.Name.ToString());
    }

    /// <summary>
    ///     This test checks the other octal escapes: <c>\100</c> is "@" and stays inside its label,
    ///     and <c>\056</c> is a literal dot, written back as <c>\.</c> so it does not split the label.
    /// </summary>
    [Fact]
    public async Task ListRecords_ForOctalEscapedAtSignAndDot_KeepsThemInsideTheLabel()
    {
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "a\\100b.example.com.",
                Type = "A",
                Values = ["192.0.2.8"],
            }
        );
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "c\\056d.example.com.",
                Type = "A",
                Values = ["192.0.2.9"],
            }
        );

        IReadOnlyList<CloudDnsProviderRecord> records = await CreateProvider()
            .ListRecordsAsync(Credentials, ZoneId);

        Assert.Contains(records, r => r.Record.Name.ToString() == "a@b.example.com.");
        DnsResourceRecord dotted = Assert
            .Single(
                records,
                r => r.ProviderRecordId.StartsWith("c\\056d", StringComparison.Ordinal)
            )
            .Record;
        Assert.Equal(3, dotted.Name.Labels.Count);
        Assert.Equal("c.d", System.Text.Encoding.ASCII.GetString(dotted.Name.Labels[0]));
    }

    /// <summary>
    ///     This test checks that an alias set and a routing-policy set are not listed (OD-B16-d).
    /// </summary>
    [Fact]
    public async Task ListRecords_ForAliasAndRoutingPolicySets_LeavesThemOut()
    {
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "alias.example.com.",
                Type = "A",
                AliasDnsName = "lb.elb.amazonaws.com.",
            }
        );
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "weighted.example.com.",
                Type = "A",
                SetIdentifier = "blue",
                Values = ["192.0.2.30"],
            }
        );

        IReadOnlyList<CloudDnsProviderRecord> records = await CreateProvider()
            .ListRecordsAsync(Credentials, ZoneId);

        Assert.Equal(3, records.Count);
        Assert.DoesNotContain(
            records,
            r =>
                r.ProviderRecordId.StartsWith("alias.")
                || r.ProviderRecordId.StartsWith("weighted.")
        );
    }

    /// <summary>
    ///     This test checks that a second value at an existing set is added with one UPSERT of the
    ///     whole set; a CREATE would be rejected by Route 53.
    /// </summary>
    [Fact]
    public async Task CreateRecord_AtAnExistingSet_AddsTheValueWithOneUpsert()
    {
        await CreateProvider()
            .CreateRecordAsync(
                Credentials,
                ZoneId,
                Record(DnsResourceRecordTypeEnum.A, "www.example.com", "192.0.2.51")
            );

        FakeRoute53Server.FakeRecordSet set = Assert.Single(
            _fake.Sets(ZoneId),
            s => s.Name == "www.example.com." && s.Type == "A"
        );
        Assert.Equal(["192.0.2.50", "192.0.2.51"], set.Values.Order().ToArray());
        string request = Assert.Single(_fake.ChangeRequests());
        Assert.Contains("UPSERT", request);
        Assert.DoesNotContain("CREATE", request);
    }

    /// <summary>
    ///     This test checks that a value the set already holds, even when Route 53 quotes it
    ///     differently, causes no request.
    /// </summary>
    [Fact]
    public async Task CreateRecord_ForAValueTheSetAlreadyHolds_SendsNothing()
    {
        await CreateProvider()
            .CreateRecordAsync(
                Credentials,
                ZoneId,
                Record(DnsResourceRecordTypeEnum.A, "www.example.com", "192.0.2.50")
            );

        Assert.Empty(_fake.ChangeRequests());
    }

    /// <summary>
    ///     This test checks that the wildcard owner is sent in Route 53's octal form and the set is
    ///     found again under it (no second CREATE, which Route 53 would reject).
    /// </summary>
    [Fact]
    public async Task CreateRecord_ForAWildcardOwner_SendsTheOctalEscapeAndFindsTheSetAgain()
    {
        Route53DnsProvider provider = CreateProvider();
        DnsResourceRecord first = Record(
            DnsResourceRecordTypeEnum.A,
            "*.example.com",
            "192.0.2.60"
        );
        DnsResourceRecord second = Record(
            DnsResourceRecordTypeEnum.A,
            "*.example.com",
            "192.0.2.61"
        );

        await provider.CreateRecordAsync(Credentials, ZoneId, first);
        await provider.CreateRecordAsync(Credentials, ZoneId, second);

        FakeRoute53Server.FakeRecordSet set = Assert.Single(
            _fake.Sets(ZoneId),
            s => s.Name == "\\052.example.com."
        );
        Assert.Equal(2, set.Values.Count);
        Assert.Contains("\\052.example.com.", _fake.ChangeRequests()[0]);
    }

    /// <summary>
    ///     This test checks that deleting one of two values leaves the other under one UPSERT with the
    ///     set's TTL; a DELETE of one value would be rejected.
    /// </summary>
    [Fact]
    public async Task DeleteRecord_OfOneOfTwoValues_LeavesTheOtherWithOneUpsert()
    {
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "two.example.com.",
                Type = "A",
                Ttl = 90,
                Values = ["192.0.2.1", "192.0.2.2"],
            }
        );

        await CreateProvider()
            .DeleteRecordAsync(
                Credentials,
                ZoneId,
                new CloudDnsProviderRecord(
                    "two.example.com.|A",
                    Record(DnsResourceRecordTypeEnum.A, "two.example.com", "192.0.2.1", 90)
                )
            );

        FakeRoute53Server.FakeRecordSet set = Assert.Single(
            _fake.Sets(ZoneId),
            s => s.Name == "two.example.com."
        );
        Assert.Equal(["192.0.2.2"], set.Values);
        Assert.Equal(90, set.Ttl);
        string request = Assert.Single(_fake.ChangeRequests());
        Assert.Contains("UPSERT", request);
    }

    /// <summary>
    ///     This test checks that deleting the last value removes the set with a DELETE that repeats
    ///     the set exactly as listed, including a TTL that differs from the record's.
    /// </summary>
    [Fact]
    public async Task DeleteRecord_OfTheLastValue_DeletesTheSetWithItsListedTtl()
    {
        await CreateProvider()
            .DeleteRecordAsync(
                Credentials,
                ZoneId,
                new CloudDnsProviderRecord(
                    "www.example.com.|A",
                    Record(DnsResourceRecordTypeEnum.A, "www.example.com", "192.0.2.50", 77)
                )
            );

        Assert.DoesNotContain(_fake.Sets(ZoneId), s => s.Name == "www.example.com.");
        Assert.Contains("DELETE", Assert.Single(_fake.ChangeRequests()));
    }

    /// <summary>
    ///     This test checks that deleting a value that is not in the set, or a set that does not
    ///     exist, sends no request.
    /// </summary>
    [Fact]
    public async Task DeleteRecord_OfAValueThatIsNotThere_SendsNothing()
    {
        Route53DnsProvider provider = CreateProvider();

        await provider.DeleteRecordAsync(
            Credentials,
            ZoneId,
            new CloudDnsProviderRecord(
                "www.example.com.|A",
                Record(DnsResourceRecordTypeEnum.A, "www.example.com", "192.0.2.99")
            )
        );
        await provider.DeleteRecordAsync(
            Credentials,
            ZoneId,
            new CloudDnsProviderRecord(
                "nothing.example.com.|A",
                Record(DnsResourceRecordTypeEnum.A, "nothing.example.com", "192.0.2.99")
            )
        );

        Assert.Empty(_fake.ChangeRequests());
    }

    /// <summary>
    ///     This test checks that creating a record where an alias set or a routing-policy set has the
    ///     same name and type sends nothing and logs a Warning (OD-B16-d).
    /// </summary>
    [Fact]
    public async Task CreateRecord_WhereAnAliasOrRoutingPolicySetExists_SendsNothingAndWarns()
    {
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "alias.example.com.",
                Type = "A",
                AliasDnsName = "lb.elb.amazonaws.com.",
            }
        );
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "weighted.example.com.",
                Type = "A",
                SetIdentifier = "blue",
                Values = ["192.0.2.30"],
            }
        );
        Route53DnsProvider provider = CreateProvider();

        await provider.CreateRecordAsync(
            Credentials,
            ZoneId,
            Record(DnsResourceRecordTypeEnum.A, "alias.example.com", "192.0.2.1")
        );
        await provider.CreateRecordAsync(
            Credentials,
            ZoneId,
            Record(DnsResourceRecordTypeEnum.A, "weighted.example.com", "192.0.2.2")
        );

        Assert.Empty(_fake.ChangeRequests());
        Assert.Equal(2, _logger.CountAt(LogLevel.Warning));
        Assert.Equal(
            ["192.0.2.30"],
            _fake.Sets(ZoneId).Single(s => s.SetIdentifier == "blue").Values
        );
    }

    /// <summary>
    ///     This test checks that deleting a record where a routing-policy set has the same name and
    ///     type sends nothing and logs a Warning.
    /// </summary>
    [Fact]
    public async Task DeleteRecord_WhereARoutingPolicySetExists_SendsNothingAndWarns()
    {
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "weighted.example.com.",
                Type = "A",
                SetIdentifier = "blue",
                Values = ["192.0.2.30"],
            }
        );

        await CreateProvider()
            .DeleteRecordAsync(
                Credentials,
                ZoneId,
                new CloudDnsProviderRecord(
                    "weighted.example.com.|A",
                    Record(DnsResourceRecordTypeEnum.A, "weighted.example.com", "192.0.2.30")
                )
            );

        Assert.Empty(_fake.ChangeRequests());
        Assert.Equal(1, _logger.CountAt(LogLevel.Warning));
    }

    /// <summary>
    ///     This method adds a set whose single value is <paramref name="value" />, exactly as Route 53
    ///     would return it, and lists the zone.
    /// </summary>
    private async Task<CloudDnsProviderRecord> ListSingleValueSetAsync(
        string name,
        string type,
        string value
    )
    {
        _fake.AddSet(
            ZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = name,
                Type = type,
                Values = [value],
            }
        );

        IReadOnlyList<CloudDnsProviderRecord> records = await CreateProvider()
            .ListRecordsAsync(Credentials, ZoneId);

        return Assert.Single(records, r => r.ProviderRecordId == $"{name}|{type}");
    }

    /// <summary>
    ///     This method builds a TXT record whose single character-string holds exactly the given
    ///     octets, which a UTF-8 string cannot express for octets such as 0xE4.
    /// </summary>
    private static DnsResourceRecord RawTxtRecord(string name, byte[] octets) =>
        new(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.Txt,
            DnsClassEnum.Internet,
            300,
            [(byte)octets.Length, .. octets]
        );

    /// <summary>
    ///     This test checks that a CNAME target Route 53 returns in its octal form (<c>\052</c> for
    ///     the asterisk) is imported as the wildcard name, not as the decimal escape.
    /// </summary>
    [Fact]
    public async Task ListRecords_ForACnameTargetWithAnOctalEscape_ReturnsTheWildcardTarget()
    {
        CloudDnsProviderRecord record = await ListSingleValueSetAsync(
            "alias.example.com.",
            "CNAME",
            "\\052.target.example."
        );

        Assert.Equal(
            "*.target.example",
            CloudDnsRecordMapper.FromWireRecord(record.Record).Content
        );
    }

    /// <summary>
    ///     This test checks the same octal rule for the MX exchange and the SRV target.
    /// </summary>
    [Fact]
    public async Task ListRecords_ForMxAndSrvTargetsWithAnOctalEscape_ReturnsTheWildcardTargets()
    {
        CloudDnsProviderRecord mx = await ListSingleValueSetAsync(
            "mx.example.com.",
            "MX",
            "10 \\052.mail.example."
        );
        CloudDnsProviderRecord srv = await ListSingleValueSetAsync(
            "_x._tcp.example.com.",
            "SRV",
            "1 2 3 \\052.srv.example."
        );

        Assert.Equal("*.mail.example", CloudDnsRecordMapper.FromWireRecord(mx.Record).Content);
        Assert.Equal("*.srv.example", CloudDnsRecordMapper.FromSrvWireRecord(srv.Record).Target);
    }

    /// <summary>
    ///     This test checks that an octal escape in a TXT value becomes that octet: <c>\344</c> is
    ///     0xE4, the octet the Route 53 guide's own example writes for the character in "exämple".
    /// </summary>
    [Fact]
    public async Task ListRecords_ForATxtValueWithAnOctalEscape_ImportsThatOctet()
    {
        CloudDnsProviderRecord record = await ListSingleValueSetAsync(
            "txt.example.com.",
            "TXT",
            "\"ex\\344mple.com\""
        );

        Assert.Equal([(byte)11, .. "ex"u8, 0xE4, .. "mple.com"u8], record.Record.Data.ToArray());
    }

    /// <summary>
    ///     This test checks that an escaped quote and an escaped backslash in a TXT value are read as
    ///     the plain characters.
    /// </summary>
    [Fact]
    public async Task ListRecords_ForATxtValueWithEscapedQuoteAndBackslash_ImportsThePlainCharacters()
    {
        CloudDnsProviderRecord record = await ListSingleValueSetAsync(
            "quote.example.com.",
            "TXT",
            "\"a\\\"b\\\\c\""
        );

        Assert.Equal("a\"b\\c", CloudDnsRecordMapper.FromWireRecord(record.Record).Content);
    }

    /// <summary>
    ///     This test checks that an octal escape in a CAA value is read as the character it names.
    /// </summary>
    [Fact]
    public async Task ListRecords_ForACaaValueWithAnOctalEscape_ImportsTheCharacter()
    {
        CloudDnsProviderRecord record = await ListSingleValueSetAsync(
            "caa.example.com.",
            "CAA",
            "0 issue \"ca\\145.example.net\""
        );

        Assert.Equal(
            "ca" + "e" + ".example.net",
            CloudDnsRecordMapper.FromCaaWireRecord(record.Record).Value
        );
    }

    /// <summary>
    ///     This test checks that a plain TXT value, with no backslash, is imported unchanged.
    /// </summary>
    [Fact]
    public async Task ListRecords_ForAPlainTxtValue_ImportsItUnchanged()
    {
        CloudDnsProviderRecord record = await ListSingleValueSetAsync(
            "plain.example.com.",
            "TXT",
            "\"v=spf1 -all\""
        );

        Assert.Equal("v=spf1 -all", CloudDnsRecordMapper.FromWireRecord(record.Record).Content);
    }

    /// <summary>
    ///     This test checks that a TXT octet outside printable ASCII is sent as a backslash and three
    ///     octal digits, which Route 53 reads as octal.
    /// </summary>
    [Fact]
    public async Task CreateRecord_ForATxtWithTheOctet0xE4_SendsItAsAnOctalEscape()
    {
        await CreateProvider()
            .CreateRecordAsync(
                Credentials,
                ZoneId,
                RawTxtRecord("octet.example.com", [.. "ex"u8, 0xE4, .. "mple.com"u8])
            );

        FakeRoute53Server.FakeRecordSet set = Assert.Single(
            _fake.Sets(ZoneId),
            s => s.Name == "octet.example.com." && s.Type == "TXT"
        );
        Assert.Equal(["\"ex\\344mple.com\""], set.Values);
    }

    /// <summary>
    ///     This test checks that a quote and a backslash in a TXT value are escaped when sent.
    /// </summary>
    [Fact]
    public async Task CreateRecord_ForATxtWithQuoteAndBackslash_EscapesBoth()
    {
        await CreateProvider()
            .CreateRecordAsync(
                Credentials,
                ZoneId,
                Record(DnsResourceRecordTypeEnum.Txt, "esc.example.com", "a\"b\\c")
            );

        FakeRoute53Server.FakeRecordSet set = Assert.Single(
            _fake.Sets(ZoneId),
            s => s.Name == "esc.example.com." && s.Type == "TXT"
        );
        Assert.Equal(["\"a\\\"b\\\\c\""], set.Values);
    }

    /// <summary>
    ///     This test checks that a wildcard name inside a CNAME value is sent in Route 53's octal
    ///     form and that the value then round-trips back to the wildcard.
    /// </summary>
    [Fact]
    public async Task CreateRecord_ForACnameToAWildcardTarget_SendsTheOctalFormAndRoundTrips()
    {
        await CreateProvider()
            .CreateRecordAsync(
                Credentials,
                ZoneId,
                Record(DnsResourceRecordTypeEnum.Cname, "rt.example.com", "*.target.example.")
            );

        FakeRoute53Server.FakeRecordSet set = Assert.Single(
            _fake.Sets(ZoneId),
            s => s.Name == "rt.example.com." && s.Type == "CNAME"
        );
        Assert.Equal(["\\052.target.example."], set.Values);

        IReadOnlyList<CloudDnsProviderRecord> records = await CreateProvider()
            .ListRecordsAsync(Credentials, ZoneId);
        CloudDnsProviderRecord listed = Assert.Single(
            records,
            r => r.ProviderRecordId == "rt.example.com.|CNAME"
        );
        Assert.Equal(
            "*.target.example",
            CloudDnsRecordMapper.FromWireRecord(listed.Record).Content
        );
    }

    /// <summary>
    ///     This test checks that the MX exchange and the SRV target are sent in the octal form too.
    /// </summary>
    [Fact]
    public async Task CreateRecord_ForMxAndSrvWithWildcardTargets_SendsTheOctalForm()
    {
        await CreateProvider()
            .CreateRecordAsync(
                Credentials,
                ZoneId,
                CloudDnsRecordMapper.ToWireRecord(
                    DnsResourceRecordTypeEnum.MX,
                    "mxo.example.com",
                    "*.mail.example.",
                    300,
                    10
                )
            );
        await CreateProvider()
            .CreateRecordAsync(
                Credentials,
                ZoneId,
                CloudDnsRecordMapper.ToSrvWireRecord(
                    "_o._tcp.example.com",
                    1,
                    2,
                    3,
                    "*.srv.example.",
                    300
                )
            );

        Assert.Equal(
            ["10 \\052.mail.example."],
            Assert.Single(_fake.Sets(ZoneId), s => s.Name == "mxo.example.com.").Values
        );
        Assert.Equal(
            ["1 2 3 \\052.srv.example."],
            Assert.Single(_fake.Sets(ZoneId), s => s.Name == "_o._tcp.example.com.").Values
        );
    }

    /// <summary>
    ///     This test checks that a TXT value of two quoted strings becomes two character-strings, not
    ///     one string holding the inner quotes.
    /// </summary>
    [Fact]
    public async Task ListRecords_ForATxtValueWithTwoQuotedStrings_ImportsTwoCharacterStrings()
    {
        CloudDnsProviderRecord record = await ListSingleValueSetAsync(
            "multi-txt.example.com.",
            "TXT",
            "\"String 1\" \"String 2\""
        );

        Assert.Equal(
            ["String 1", "String 2"],
            (
                (TxtRecordData)
                    DnsRecordDataCodecRegistry.Decode(record.Record.Type, record.Record.Data)
            ).Values
        );
    }

    /// <summary>
    ///     This test checks that a 300-octet TXT record, held as strings of 255 and 45 octets, is
    ///     sent as two quoted strings, since Route 53 rejects a quoted string over 255 characters.
    /// </summary>
    [Fact]
    public async Task CreateRecord_ForATxtOfTwoCharacterStrings_SendsTwoQuotedStrings()
    {
        string first = new('a', 255);
        string second = new('b', 45);
        DnsResourceRecord record = new(
            DnsName.Parse("long.example.com"),
            DnsResourceRecordTypeEnum.Txt,
            DnsClassEnum.Internet,
            300,
            [
                255,
                .. System.Text.Encoding.ASCII.GetBytes(first),
                45,
                .. System.Text.Encoding.ASCII.GetBytes(second),
            ]
        );

        await CreateProvider().CreateRecordAsync(Credentials, ZoneId, record);

        FakeRoute53Server.FakeRecordSet set = Assert.Single(
            _fake.Sets(ZoneId),
            s => s.Name == "long.example.com." && s.Type == "TXT"
        );
        Assert.Equal([$"\"{first}\" \"{second}\""], set.Values);
    }

    /// <summary>
    ///     This test checks that a two-string TXT record keeps its string boundaries through a push
    ///     and a listing.
    /// </summary>
    [Fact]
    public async Task CreateRecord_ForATwoStringTxt_RoundTripsUnchanged()
    {
        DnsResourceRecord record = new(
            DnsName.Parse("rt-txt.example.com"),
            DnsResourceRecordTypeEnum.Txt,
            DnsClassEnum.Internet,
            300,
            [.. "\u0003abc"u8, .. "\u0002de"u8]
        );

        await CreateProvider().CreateRecordAsync(Credentials, ZoneId, record);
        IReadOnlyList<CloudDnsProviderRecord> records = await CreateProvider()
            .ListRecordsAsync(Credentials, ZoneId);

        CloudDnsProviderRecord listed = Assert.Single(
            records,
            r => r.ProviderRecordId == "rt-txt.example.com.|TXT"
        );
        Assert.Equal(record.Data.ToArray(), listed.Record.Data.ToArray());
    }
}

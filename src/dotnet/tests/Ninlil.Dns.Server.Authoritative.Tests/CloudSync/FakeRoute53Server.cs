using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CloudSync;

/// <summary>
///     This class is a stateful, in-memory fake of the Route 53 REST-XML API, hosted on Kestrel at an
///     OS-assigned loopback port. It implements what the Route 53 API Reference documents for
///     ListResourceRecordSets (paging with <c>name</c>, <c>type</c> and <c>maxitems</c>, names
///     returned with octal escapes) and ChangeResourceRecordSets (<c>CREATE</c> fails for an existing
///     set, <c>DELETE</c> must match the set exactly, <c>UPSERT</c> replaces or creates, a failed
///     batch changes nothing and answers <c>InvalidChangeBatch</c>). It does not check signatures.
///     <see cref="Route53DnsProviderTests" /> and <see cref="Route53ZoneSyncRoundTripTests" /> use it
///     through the <c>ServiceUrl</c> credential field.
/// </summary>
public sealed class FakeRoute53Server : IAsyncDisposable
{
    private const string Namespace = "https://route53.amazonaws.com/doc/2013-04-01/";

    /// <summary>The lock that guards every hosted zone and the request logs.</summary>
    private readonly object _gate = new();

    private readonly Dictionary<string, List<FakeRecordSet>> _zones = [];
    private readonly List<(string ZoneId, string Body)> _changeRequests = [];
    private readonly Dictionary<string, int> _listRequestCounts = [];
    private WebApplication _app;

    /// <summary>
    ///     This class is one record set in the fake: the fields of Route 53's ResourceRecordSet that
    ///     the tests need.
    /// </summary>
    public sealed class FakeRecordSet
    {
        /// <summary>This property contains the name in the escaped form Route 53 returns.</summary>
        public string Name { get; set; }

        /// <summary>This property contains the record type, in upper case.</summary>
        public string Type { get; set; }

        /// <summary>This property contains the one TTL of the whole set, in seconds.</summary>
        public long Ttl { get; set; } = 300;

        /// <summary>This property contains the value strings, one per record.</summary>
        public List<string> Values { get; set; } = [];

        /// <summary>This property contains the alias target's DNS name; set for an alias set.</summary>
        public string AliasDnsName { get; set; }

        /// <summary>This property contains the routing-policy set identifier; set for a routed set.</summary>
        public string SetIdentifier { get; set; }

        /// <summary>This method copies the set so a failed batch can be rolled back.</summary>
        public FakeRecordSet Clone() =>
            new()
            {
                Name = Name,
                Type = Type,
                Ttl = Ttl,
                Values = [.. Values],
                AliasDnsName = AliasDnsName,
                SetIdentifier = SetIdentifier,
            };
    }

    /// <summary>This property contains the base URL, for the <c>ServiceUrl</c> credential field.</summary>
    public string Url => _app.Urls.First();

    /// <summary>This method starts the server on a free loopback port.</summary>
    public async Task StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _app = builder.Build();

        _app.MapGet(
            "/2013-04-01/hostedzone/{zoneId}/rrset",
            (string zoneId, HttpRequest request) => List(zoneId, request)
        );
        _app.MapPost(
            "/2013-04-01/hostedzone/{zoneId}/rrset",
            async (string zoneId, HttpContext context) =>
            {
                using StreamReader reader = new(context.Request.Body);
                return Change(zoneId, await reader.ReadToEndAsync());
            }
        );

        await _app.StartAsync();
    }

    /// <summary>This method stops the server.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_app is null)
            return;

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    ///     This method adds a record set to a hosted zone, creating the zone on first use. The name
    ///     is stored in Route 53's canonical escaped form.
    /// </summary>
    public void AddSet(string zoneId, FakeRecordSet set)
    {
        set.Name = Canonical(set.Name);

        lock (_gate)
            ZoneSets(zoneId).Add(set);
    }

    /// <summary>This method returns a snapshot of a hosted zone's record sets.</summary>
    public List<FakeRecordSet> Sets(string zoneId)
    {
        lock (_gate)
            return [.. ZoneSets(zoneId).Select(set => set.Clone())];
    }

    /// <summary>
    ///     This method returns the bodies of every ChangeResourceRecordSets request, in order,
    ///     for one hosted zone or all of them.
    /// </summary>
    public List<string> ChangeRequests(string zoneId = null)
    {
        lock (_gate)
            return
            [
                .. _changeRequests
                    .Where(request => zoneId is null || request.ZoneId == zoneId)
                    .Select(request => request.Body),
            ];
    }

    /// <summary>This method returns how many ListResourceRecordSets requests a hosted zone served.</summary>
    public int ListRequestCount(string zoneId)
    {
        lock (_gate)
            return _listRequestCounts.GetValueOrDefault(zoneId);
    }

    /// <summary>This method returns a zone's list; the caller holds the lock.</summary>
    private List<FakeRecordSet> ZoneSets(string zoneId)
    {
        if (!_zones.TryGetValue(zoneId, out List<FakeRecordSet> sets))
            _zones[zoneId] = sets = [];

        return sets;
    }

    /// <summary>
    ///     This method writes a name the way Route 53 does: lower case, trailing dot, and every octet
    ///     outside <c>a-z 0-9 - _</c> as a backslash and three octal digits. A name sent with an
    ///     octal escape or with the plain character becomes the same string, so both address the same
    ///     set. A literal dot inside a label stays escaped.
    /// </summary>
    private static string Canonical(string name)
    {
        StringBuilder builder = new();

        for (int i = 0; i < name.Length; i++)
        {
            bool escaped = false;
            int octet = char.ToLowerInvariant(name[i]);

            if (
                name[i] == '\\'
                && i + 3 < name.Length
                && name[(i + 1)..(i + 4)].All(c => c is >= '0' and <= '7')
            )
            {
                octet = Convert.ToInt32(name.Substring(i + 1, 3), 8);
                escaped = true;
                i += 3;
            }

            if (
                octet is >= 'a' and <= 'z'
                || octet is >= '0' and <= '9'
                || octet is '-' or '_'
                || (octet == '.' && !escaped)
            )
                builder.Append((char)octet);
            else
                builder.Append('\\').Append(Convert.ToString(octet, 8).PadLeft(3, '0'));
        }

        if (builder.Length == 0 || builder[^1] != '.')
            builder.Append('.');

        return builder.ToString();
    }

    /// <summary>
    ///     This method builds the ordering key Route 53 documents for listings: the labels from the
    ///     right, so a name sorts next to its parent.
    /// </summary>
    private static string NameKey(string canonicalName) =>
        string.Join('.', canonicalName.TrimEnd('.').Split('.').Reverse());

    /// <summary>
    ///     This method answers ListResourceRecordSets: sets ordered by <see cref="NameKey" />, starting
    ///     at <c>name</c> and <c>type</c> when given, at most <c>maxitems</c> (default 100), with the
    ///     next name and type when the answer is truncated.
    /// </summary>
    private IResult List(string zoneId, HttpRequest request)
    {
        lock (_gate)
        {
            _listRequestCounts[zoneId] = _listRequestCounts.GetValueOrDefault(zoneId) + 1;

            int maxItems = int.TryParse(request.Query["maxitems"], out int parsed) ? parsed : 100;
            List<FakeRecordSet> ordered =
            [
                .. ZoneSets(zoneId)
                    .OrderBy(set => NameKey(set.Name), StringComparer.Ordinal)
                    .ThenBy(set => set.Type, StringComparer.Ordinal),
            ];

            // The listing starts at the given name and, when present, type (inclusive).
            if (request.Query.TryGetValue("name", out StringValues name))
            {
                string startKey = NameKey(Canonical(name.ToString()));
                string startType = request.Query["type"].ToString();
                ordered =
                [
                    .. ordered.Where(set =>
                    {
                        int byName = string.CompareOrdinal(NameKey(set.Name), startKey);
                        return byName != 0 || startType.Length == 0
                            ? byName >= 0
                            : string.CompareOrdinal(set.Type, startType) >= 0;
                    }),
                ];
            }

            List<FakeRecordSet> page = [.. ordered.Take(maxItems)];
            FakeRecordSet next = ordered.Count > maxItems ? ordered[maxItems] : null;
            XNamespace ns = Namespace;

            XElement sets = new(ns + "ResourceRecordSets");

            foreach (FakeRecordSet set in page)
            {
                XElement element = new(
                    ns + "ResourceRecordSet",
                    new XElement(ns + "Name", set.Name),
                    new XElement(ns + "Type", set.Type)
                );

                if (set.SetIdentifier is not null)
                    element.Add(new XElement(ns + "SetIdentifier", set.SetIdentifier));

                if (set.AliasDnsName is not null)
                    element.Add(
                        new XElement(
                            ns + "AliasTarget",
                            new XElement(ns + "HostedZoneId", "Z2FDTNDATAQYW2"),
                            new XElement(ns + "DNSName", set.AliasDnsName),
                            new XElement(ns + "EvaluateTargetHealth", "false")
                        )
                    );
                else
                    element.Add(
                        new XElement(ns + "TTL", set.Ttl),
                        new XElement(
                            ns + "ResourceRecords",
                            set.Values.Select(value => new XElement(
                                ns + "ResourceRecord",
                                new XElement(ns + "Value", value)
                            ))
                        )
                    );

                sets.Add(element);
            }

            XElement root = new(
                ns + "ListResourceRecordSetsResponse",
                sets,
                new XElement(ns + "IsTruncated", next is not null ? "true" : "false"),
                new XElement(ns + "MaxItems", maxItems)
            );

            if (next is not null)
            {
                root.Add(new XElement(ns + "NextRecordName", next.Name));
                root.Add(new XElement(ns + "NextRecordType", next.Type));
            }

            return Results.Content(new XDocument(root).ToString(), "text/xml");
        }
    }

    /// <summary>
    ///     This method answers ChangeResourceRecordSets. The batch applies to a copy and replaces the
    ///     zone only when every change succeeds.
    /// </summary>
    private IResult Change(string zoneId, string body)
    {
        lock (_gate)
        {
            _changeRequests.Add((zoneId, body));

            XNamespace ns = Namespace;
            XDocument document = XDocument.Parse(body);
            List<FakeRecordSet> working = [.. ZoneSets(zoneId).Select(set => set.Clone())];

            foreach (XElement change in document.Descendants(ns + "Change"))
            {
                string action = change.Element(ns + "Action")?.Value;
                XElement element = change.Element(ns + "ResourceRecordSet");
                FakeRecordSet incoming = new()
                {
                    Name = Canonical(element.Element(ns + "Name")?.Value ?? string.Empty),
                    Type = element.Element(ns + "Type")?.Value,
                    Ttl = long.Parse(element.Element(ns + "TTL")?.Value ?? "0"),
                    SetIdentifier = element.Element(ns + "SetIdentifier")?.Value,
                    Values =
                    [
                        .. element
                            .Descendants(ns + "ResourceRecord")
                            .Select(value => value.Element(ns + "Value")?.Value),
                    ],
                };

                FakeRecordSet current = working.FirstOrDefault(set =>
                    set.Name == incoming.Name
                    && set.Type == incoming.Type
                    && set.SetIdentifier == incoming.SetIdentifier
                );

                string error = null;

                if (action == "CREATE")
                {
                    if (current is not null)
                        error =
                            $"Tried to create resource record set [name='{incoming.Name}', type='{incoming.Type}'] but it already exists";
                    else
                        working.Add(incoming);
                }
                else if (action == "UPSERT")
                {
                    if (current is not null)
                        working.Remove(current);

                    working.Add(incoming);
                }
                else if (action == "DELETE")
                {
                    if (current is null)
                        error =
                            $"Tried to delete resource record set [name='{incoming.Name}', type='{incoming.Type}'] but it was not found";
                    else if (
                        current.Ttl != incoming.Ttl
                        || !current
                            .Values.OrderBy(v => v)
                            .SequenceEqual(incoming.Values.OrderBy(v => v))
                    )
                        error =
                            $"Tried to delete resource record set [name='{incoming.Name}', type='{incoming.Type}'] but the values provided do not match the current values";
                    else
                        working.Remove(current);
                }
                else
                {
                    error = $"Unknown action '{action}'";
                }

                if (error is not null)
                    return Results.Content(
                        new XDocument(
                            new XElement(
                                ns + "InvalidChangeBatch",
                                new XElement(ns + "Messages", new XElement(ns + "Message", error)),
                                new XElement(ns + "RequestId", "fake-request")
                            )
                        ).ToString(),
                        "text/xml",
                        statusCode: 400
                    );
            }

            _zones[zoneId] = working;

            return Results.Content(
                new XDocument(
                    new XElement(
                        ns + "ChangeResourceRecordSetsResponse",
                        new XElement(
                            ns + "ChangeInfo",
                            new XElement(ns + "Id", "/change/C1"),
                            new XElement(ns + "Status", "PENDING"),
                            new XElement(ns + "SubmittedAt", "2026-01-01T00:00:00Z")
                        )
                    )
                ).ToString(),
                "text/xml"
            );
        }
    }
}

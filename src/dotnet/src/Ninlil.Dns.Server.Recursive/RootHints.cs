using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive;

/// <summary>
///     This record carries one root name server's name and addresses (or an alternate root's,
///     from <see cref="RecursiveServerOptions.RootHintServersOverride" />).
/// </summary>
/// <param name="Name">The name server's name.</param>
/// <param name="Addresses">The name server's IPv4 and IPv6 addresses.</param>
public sealed record RootHintServer(string Name, IReadOnlyList<IPAddress> Addresses);

/// <summary>
///     This class holds the built-in root server list that
///     <see cref="Resolution.RootHintsPrimingService" /> seeds the cache with at startup, before
///     its RFC 8109 priming query replaces it with the root's answer (the list stays in use
///     if priming fails). <see cref="Resolution.IterativeResolutionEngine" /> seeds it again
///     whenever the cache holds no root entry. It is the IANA root server set; stale addresses
///     could stop the resolver bootstrapping, so check any change against IANA.
/// </summary>
public static class RootHints
{
    /// <summary>
    ///     This property contains the 13 root servers, a through m.
    /// </summary>
    public static IReadOnlyList<RootHintServer> Servers { get; } =
    [
        new(
            "a.root-servers.net",
            [IPAddress.Parse("198.41.0.4"), IPAddress.Parse("2001:503:ba3e::2:30")]
        ),
        new(
            "b.root-servers.net",
            [IPAddress.Parse("170.247.170.2"), IPAddress.Parse("2801:1b8:10::b")]
        ),
        new(
            "c.root-servers.net",
            [IPAddress.Parse("192.33.4.12"), IPAddress.Parse("2001:500:2::c")]
        ),
        new(
            "d.root-servers.net",
            [IPAddress.Parse("199.7.91.13"), IPAddress.Parse("2001:500:2d::d")]
        ),
        new(
            "e.root-servers.net",
            [IPAddress.Parse("192.203.230.10"), IPAddress.Parse("2001:500:a8::e")]
        ),
        new(
            "f.root-servers.net",
            [IPAddress.Parse("192.5.5.241"), IPAddress.Parse("2001:500:2f::f")]
        ),
        new(
            "g.root-servers.net",
            [IPAddress.Parse("192.112.36.4"), IPAddress.Parse("2001:500:12::d0d")]
        ),
        new(
            "h.root-servers.net",
            [IPAddress.Parse("198.97.190.53"), IPAddress.Parse("2001:500:1::53")]
        ),
        new(
            "i.root-servers.net",
            [IPAddress.Parse("192.36.148.17"), IPAddress.Parse("2001:7fe::53")]
        ),
        new(
            "j.root-servers.net",
            [IPAddress.Parse("192.58.128.30"), IPAddress.Parse("2001:503:c27::2:30")]
        ),
        new(
            "k.root-servers.net",
            [IPAddress.Parse("193.0.14.129"), IPAddress.Parse("2001:7fd::1")]
        ),
        new(
            "l.root-servers.net",
            [IPAddress.Parse("199.7.83.42"), IPAddress.Parse("2001:500:9f::42")]
        ),
        new(
            "m.root-servers.net",
            [IPAddress.Parse("202.12.27.33"), IPAddress.Parse("2001:dc3::35")]
        ),
    ];

    /// <summary>
    ///     This method resolves the effective root hints list: <see cref="RecursiveServerOptions.RootHintServersOverride" />
    ///     when the operator configured one, or the built-in <see cref="Servers" />
    ///     otherwise.
    /// </summary>
    /// <param name="options">The options to read an override from, if any.</param>
    /// <returns>The effective root hints list.</returns>
    public static IReadOnlyList<RootHintServer> Resolve(RecursiveServerOptions options) =>
        options.RootHintServersOverride.Count > 0
            ?
            [
                .. options.RootHintServersOverride.Select(server => new RootHintServer(
                    server.Name,
                    [.. server.Addresses.Select(IPAddress.Parse)]
                )),
            ]
            : Servers;
}

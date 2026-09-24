using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Web.Examples.DnsCache;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller shows and trims this node's in-memory DNS answer caches: the
///     forwarded-response cache and the recursive resolver's cache, every
///     <see cref="IDnsCache" /> the resolver projects register. One endpoint covers both, so an
///     operator never needs to know which path answered a name to flush it.
///     <para>
///         Reading requires <c>recursivesettings:read</c> and changing requires
///         <c>recursivesettings:write</c> -- the permissions that already guard the cache's size
///         and serve-stale settings and the live cache-eviction feed (see
///         <c>DnsEventsHub.CacheEventsPermission</c>), so whoever may tune the cache may also
///         inspect and flush it, and nobody gains a new permission to hand out.
///     </para>
///     <para>
///         Every action affects only the node that serves the request. Caches are per process
///         and never replicated, so on a cluster each node is flushed through its own address.
///     </para>
/// </summary>
/// <param name="caches">Every registered cache.</param>
[ApiController]
[Route("api/dns-cache")]
public class DnsCacheController(IEnumerable<IDnsCache> caches) : ControllerBase
{
    private readonly IReadOnlyList<IDnsCache> _caches =
    [
        .. caches.OrderBy(cache => cache.Name, StringComparer.Ordinal),
    ];

    /// <summary>
    ///     This action reports every cache's size and hit rate.
    /// </summary>
    [HttpGet]
    [RequirePermission("recursivesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsCacheDetailListExample))]
    public ActionResult<IReadOnlyList<DnsCacheDetail>> List() =>
        Ok(_caches.Select(cache => ToDetail(cache.GetStatistics())).ToList());

    /// <summary>
    ///     This action empties every cache, or only the one named by <paramref name="cache" />,
    ///     and resets its hit and miss counters. The next query for any name goes upstream.
    /// </summary>
    /// <param name="cache">A cache name (<c>forwarded</c> or <c>recursive</c>) to flush alone; omit to flush every cache.</param>
    [HttpDelete]
    [RequirePermission("recursivesettings:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsCacheRemovalResultExample))]
    public ActionResult<DnsCacheRemovalResult> Flush([FromQuery] string cache = null) =>
        Ok(new DnsCacheRemovalResult(SelectCaches(cache).Sum(selected => selected.Flush())));

    /// <summary>
    ///     This action forgets every cached answer for exactly one name -- every record type,
    ///     and in the forwarded cache every upstream set -- so the next query for it goes
    ///     upstream. Names below it are left alone. Removing a name that is not cached is not an
    ///     error; it simply removes nothing.
    /// </summary>
    /// <param name="name">The name to forget, for example <c>www.example.com</c>; a trailing dot is optional.</param>
    /// <param name="cache">A cache name to remove it from alone; omit to remove it from every cache.</param>
    [HttpDelete("entries/{name}")]
    [RequirePermission("recursivesettings:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsCacheRemovalResultExample))]
    public ActionResult<DnsCacheRemovalResult> RemoveName(
        string name,
        [FromQuery] string cache = null
    )
    {
        DnsName dnsName = ParseName(name);

        return Ok(
            new DnsCacheRemovalResult(SelectCaches(cache).Sum(selected => selected.Remove(dnsName)))
        );
    }

    /// <summary>
    ///     This method returns every cache, or the one named, failing with 404 for a name no
    ///     cache has.
    /// </summary>
    private IEnumerable<IDnsCache> SelectCaches(string cacheName)
    {
        if (string.IsNullOrWhiteSpace(cacheName))
            return _caches;

        IDnsCache selected = _caches.FirstOrDefault(cache =>
            string.Equals(cache.Name, cacheName.Trim(), StringComparison.OrdinalIgnoreCase)
        );

        return selected is null ? throw new NotFoundException("DnsCache", cacheName) : [selected];
    }

    /// <summary>
    ///     This method parses an operator-typed name. An internationalized name is converted to
    ///     its ASCII (punycode) form first, since that is the form the cache is keyed on.
    /// </summary>
    private static DnsName ParseName(string name)
    {
        string trimmed = name?.Trim().TrimEnd('.') ?? string.Empty;

        if (trimmed.Length == 0)
            throw new ValidationException(
                "'name' must be a DNS name, for example www.example.com."
            );

        string ascii;

        try
        {
            ascii = new IdnMapping().GetAscii(trimmed);
        }
        catch (ArgumentException)
        {
            ascii = trimmed;
        }

        if (!DnsName.TryParse(ascii, out DnsName dnsName, out string error))
            throw new ValidationException($"'{name}' is not a valid DNS name: {error}");

        return dnsName;
    }

    private static DnsCacheDetail ToDetail(DnsCacheStatistics statistics) =>
        new(
            statistics.Name,
            statistics.EntryCount,
            statistics.MaxEntries,
            statistics.Hits,
            statistics.Misses
        );
}

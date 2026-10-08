using System.Collections.Generic;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class decides whether a verified TSIG key may act on a zone. The dynamic update
///     resolver checks <see cref="Data.Abstractions.Entity.ZoneEntity.UpdateTsigKeyNames" /> and
///     the zone transfer service checks
///     <see cref="Data.Abstractions.Entity.ZoneEntity.TransferTsigKeyNames" />, both after the
///     pipeline or the service verified the signature. A key that verifies but is not listed
///     gets a signed REFUSED, so one key handed to one secondary cannot touch other zones.
/// </summary>
public static class ZoneTsigKeyBinding
{
    /// <summary>
    ///     This method reports whether a key name is in a zone's binding list. Names compare
    ///     case-insensitively and ignore one trailing dot, as DNS names do.
    /// </summary>
    /// <param name="allowedKeyNames">
    ///     The zone's list. An empty or <see langword="null" /> list accepts no key.
    /// </param>
    /// <param name="keyName">The name of the key that signed the request.</param>
    public static bool IsAllowed(IEnumerable<string> allowedKeyNames, string keyName)
    {
        if (allowedKeyNames is null || string.IsNullOrEmpty(keyName))
            return false;

        string wanted = Normalize(keyName);

        foreach (string allowed in allowedKeyNames)
        {
            if (allowed is not null && Normalize(allowed) == wanted)
                return true;
        }

        return false;
    }

    private static string Normalize(string name) => name.TrimEnd('.').ToLowerInvariant();
}

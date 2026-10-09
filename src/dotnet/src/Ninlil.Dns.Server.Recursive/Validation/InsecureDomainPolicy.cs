using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class answers one question for the DNSSEC validation: is a name at or below one of
///     the Insecure domains (negative trust anchors) in force? The list comes from
///     <see cref="RecursiveServerOptions.GetEffectiveInsecureDomains" /> and is read live through
///     <see cref="IOptionsMonitor{TOptions}" />, so a change saved through
///     <c>RecursiveSettingsController</c> applies to the next query. <see cref="DnssecValidator" />
///     asks it before any chain-of-trust work, so a name on the list never reaches the trust
///     cache; <c>ForwarderQueryResolver</c> compares <see cref="Fingerprint" /> between queries
///     and flushes its response cache when the list changed, because that cache remembers whether
///     an answer was Secure.
///     <para>
///         The parsed set is rebuilt only when the options object changes. An entry that is not a
///         valid domain name, and the root name, are ignored: a root entry would switch validation
///         off for every name, which is what <see cref="RecursiveServerOptions.EnableDnssecValidation" />
///         is for.
///     </para>
/// </summary>
/// <param name="options">The live recursive resolver settings.</param>
public sealed class InsecureDomainPolicy(IOptionsMonitor<RecursiveServerOptions> options)
{
    private sealed record Snapshot(
        RecursiveServerOptions Source,
        HashSet<DnsName> Names,
        string Fingerprint
    );

    private Snapshot _snapshot;

    /// <summary>
    ///     This property contains a text that changes exactly when the set of effective Insecure
    ///     domains changes (case and order ignored).
    /// </summary>
    public string Fingerprint => Current().Fingerprint;

    /// <summary>
    ///     This method tells whether <paramref name="name" /> is at or below an Insecure domain.
    /// </summary>
    /// <param name="name">The name a client asked for, or a name its answer points to.</param>
    public bool IsInsecure(DnsName name)
    {
        HashSet<DnsName> names = Current().Names;

        if (names.Count == 0)
            return false;

        for (int skip = 0; skip < name.Labels.Count; skip++)
            if (names.Contains(new DnsName([.. name.Labels.Skip(skip)])))
                return true;

        return false;
    }

    private Snapshot Current()
    {
        RecursiveServerOptions source = options.CurrentValue;
        Snapshot snapshot = _snapshot;

        if (snapshot is not null && ReferenceEquals(snapshot.Source, source))
            return snapshot;

        HashSet<DnsName> names = [];

        foreach (string entry in source.GetEffectiveInsecureDomains())
            if (DnsName.TryParse(entry, out DnsName parsed) && !parsed.IsRoot)
                names.Add(parsed.ToCanonicalLower());

        string fingerprint = string.Join(
            '\n',
            names.Select(name => name.ToString()).Order(StringComparer.Ordinal)
        );

        // A concurrent rebuild produces the same value, so the last writer may win.
        return _snapshot = new Snapshot(source, names, fingerprint);
    }
}

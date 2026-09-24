using Microsoft.Extensions.Configuration;

// Define our namespace.
namespace Ninlil.Common.Configuration;

/// <summary>
///     This configuration provider is the only environment-variable configuration source this
///     project's host ever registers -- it replaces ASP.NET Core's own built-in
///     <c>EnvironmentVariablesConfigurationProvider</c> (which understands only a double-
///     underscore hierarchy separator and does no PascalCase word-splitting at all) with one that
///     recognizes only this project's own single, consistent <c>NINLIL_</c>-prefixed naming
///     scheme, mapping each recognized variable back onto its own real internal configuration path
///     via an explicit, reflection-built lookup table (<see cref="OptionsEnvironmentVariableMap" />).
///     <para>
///         A <c>NINLIL_</c>-prefixed variable this provider does not recognize (a typo, or a name
///         from a configuration class this project's own composition root never registered) is
///         never silently ignored -- it is recorded in <see cref="UnrecognizedVariableNames" /> so
///         the host can log a real startup warning about it once a real logger exists (this
///         provider itself runs far too early in host startup to log anything on its own).
///     </para>
/// </summary>
public sealed class NinlilEnvironmentVariablesConfigurationProvider : ConfigurationProvider
{
    private readonly IReadOnlyDictionary<
        string,
        string
    > _environmentVariableNameToConfigurationPath;

    /// <summary>
    ///     This property lists every real, currently-set <c>NINLIL_</c>-prefixed environment
    ///     variable this provider's own <see cref="Load" /> found no mapping table entry for.
    ///     Populated once, during <see cref="Load" />; empty until then.
    /// </summary>
    public IReadOnlyList<string> UnrecognizedVariableNames { get; private set; } = [];

    /// <summary>
    ///     Constructs a new provider against an already-built NINLIL_ name -&gt; configuration path
    ///     mapping table (see <see cref="OptionsEnvironmentVariableMap.BuildMappings" />).
    /// </summary>
    public NinlilEnvironmentVariablesConfigurationProvider(
        IReadOnlyDictionary<string, string> environmentVariableNameToConfigurationPath
    )
    {
        _environmentVariableNameToConfigurationPath = environmentVariableNameToConfigurationPath;
    }

    /// <summary>
    ///     This method reads the real process environment once, translating every recognized
    ///     <c>NINLIL_</c>-prefixed variable into this provider's own <see cref="ConfigurationProvider.Data" />
    ///     entry at its real internal configuration path. A variable whose name does not start
    ///     with <see cref="NinlilEnvironmentVariableNaming.Prefix" /> is ignored outright -- this is
    ///     exactly what makes the old double-underscore scheme, and every unrelated environment
    ///     variable a process might happen to have set, genuinely inert once this provider replaces
    ///     ASP.NET Core's own default one.
    /// </summary>
    public override void Load()
    {
        Data.Clear();
        List<string> unrecognized = [];

        // IDictionaryEnumerator (not a generic KeyValuePair) is what
        // Environment.GetEnvironmentVariables() itself actually returns -- both the key and the
        // value are boxed as plain objects, always really strings on every platform this project
        // targets.
        System.Collections.IDictionary realEnvironment = Environment.GetEnvironmentVariables();

        foreach (System.Collections.DictionaryEntry entry in realEnvironment)
        {
            string variableName = (string)entry.Key;

            if (
                !variableName.StartsWith(
                    NinlilEnvironmentVariableNaming.Prefix,
                    StringComparison.Ordinal
                )
            )
                continue;

            if (
                _environmentVariableNameToConfigurationPath.TryGetValue(
                    variableName,
                    out string configurationPath
                )
            )
                Data[configurationPath] = (string)entry.Value;
            else
                unrecognized.Add(variableName);
        }

        UnrecognizedVariableNames = unrecognized;
    }
}

/// <summary>
///     This configuration source produces one <see cref="NinlilEnvironmentVariablesConfigurationProvider" />
///     against an already-built mapping table, and keeps a direct reference to the provider it
///     builds (<see cref="Provider" />) so the composition root can inspect
///     <see cref="NinlilEnvironmentVariablesConfigurationProvider.UnrecognizedVariableNames" />
///     after the host finishes building, once a real logger actually exists.
/// </summary>
public sealed class NinlilEnvironmentVariablesConfigurationSource : IConfigurationSource
{
    private readonly IReadOnlyDictionary<
        string,
        string
    > _environmentVariableNameToConfigurationPath;

    /// <summary>
    ///     This property contains the single provider instance <see cref="Build" /> creates, once
    ///     <see cref="IConfigurationBuilder.Build" /> is actually called on the host's own
    ///     configuration builder -- <see langword="null" /> until then.
    /// </summary>
    public NinlilEnvironmentVariablesConfigurationProvider Provider { get; private set; }

    /// <summary>
    ///     Constructs a new source against an already-built NINLIL_ name -&gt; configuration path
    ///     mapping table.
    /// </summary>
    public NinlilEnvironmentVariablesConfigurationSource(
        IReadOnlyDictionary<string, string> environmentVariableNameToConfigurationPath
    )
    {
        _environmentVariableNameToConfigurationPath = environmentVariableNameToConfigurationPath;
    }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        Provider = new NinlilEnvironmentVariablesConfigurationProvider(
            _environmentVariableNameToConfigurationPath
        );
        return Provider;
    }
}

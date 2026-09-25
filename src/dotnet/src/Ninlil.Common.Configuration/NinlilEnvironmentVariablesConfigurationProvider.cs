using Microsoft.Extensions.Configuration;

// Define our namespace.
namespace Ninlil.Common.Configuration;

/// <summary>
///     This configuration provider is the only environment-variable source the Ninlil host
///     registers. <c>Program.cs</c> removes ASP.NET Core's built-in
///     <c>EnvironmentVariablesConfigurationProvider</c> (which only understands a double-underscore
///     hierarchy separator) and puts this one in its place. It recognizes only the
///     <c>NINLIL_</c>-prefixed names from <see cref="NinlilEnvironmentVariableNaming" /> and maps
///     each one to its configuration path through the table built by
///     <see cref="OptionsEnvironmentVariableMap" />.
///     <para>
///         A <c>NINLIL_</c>-prefixed variable with no table entry (a typo, or a setting the host
///         does not register) is recorded in <see cref="UnrecognizedVariableNames" />. The provider
///         runs too early in host startup to log, so the host logs a warning for each one after it
///         has a logger.
///     </para>
/// </summary>
public sealed class NinlilEnvironmentVariablesConfigurationProvider : ConfigurationProvider
{
    private readonly IReadOnlyDictionary<
        string,
        string
    > _environmentVariableNameToConfigurationPath;

    /// <summary>
    ///     This property lists every <c>NINLIL_</c>-prefixed environment variable that
    ///     <see cref="Load" /> found set but could not map. It is empty until <see cref="Load" />
    ///     runs.
    /// </summary>
    public IReadOnlyList<string> UnrecognizedVariableNames { get; private set; } = [];

    /// <summary>
    ///     Constructs a new provider over a prebuilt NINLIL_ name -&gt; configuration path table
    ///     (see <see cref="OptionsEnvironmentVariableMap.BuildMappings" />).
    /// </summary>
    public NinlilEnvironmentVariablesConfigurationProvider(
        IReadOnlyDictionary<string, string> environmentVariableNameToConfigurationPath
    )
    {
        _environmentVariableNameToConfigurationPath = environmentVariableNameToConfigurationPath;
    }

    /// <summary>
    ///     This method reads the process environment and copies every recognized
    ///     <c>NINLIL_</c>-prefixed variable into <see cref="ConfigurationProvider.Data" /> at its
    ///     configuration path. Variables without the
    ///     <see cref="NinlilEnvironmentVariableNaming.Prefix" /> are ignored, so the stock
    ///     double-underscore names and unrelated variables have no effect on configuration.
    /// </summary>
    public override void Load()
    {
        Data.Clear();
        List<string> unrecognized = [];

        // Environment.GetEnvironmentVariables() returns a non-generic IDictionary whose keys and
        // values are boxed strings.
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
///     This configuration source creates one <see cref="NinlilEnvironmentVariablesConfigurationProvider" />
///     over a prebuilt mapping table. It keeps a reference to that provider in
///     <see cref="Provider" /> so the host can read
///     <see cref="NinlilEnvironmentVariablesConfigurationProvider.UnrecognizedVariableNames" />
///     and log them once logging is available.
/// </summary>
public sealed class NinlilEnvironmentVariablesConfigurationSource : IConfigurationSource
{
    private readonly IReadOnlyDictionary<
        string,
        string
    > _environmentVariableNameToConfigurationPath;

    /// <summary>
    ///     This property contains the provider that <see cref="Build" /> created, or
    ///     <see langword="null" /> until the configuration is built.
    /// </summary>
    public NinlilEnvironmentVariablesConfigurationProvider Provider { get; private set; }

    /// <summary>
    ///     Constructs a new source over a prebuilt NINLIL_ name -&gt; configuration path table.
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

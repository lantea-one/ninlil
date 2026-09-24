// Define our namespace.
namespace Ninlil.Web.EnvironmentVariables;

/// <summary>
///     This static class redirects this project's own <c>NINLIL_</c>-prefixed equivalent of every
///     real, framework-level (not this project's own <c>Options</c>-bound) environment variable
///     that .NET's own generic host / ASP.NET Core WebHost building code reads -- most notably
///     <c>ASPNETCORE_ENVIRONMENT</c>/<c>ASPNETCORE_URLS</c> -- onto that variable's own real name,
///     via <see cref="Environment.SetEnvironmentVariable(string, string)" />, before any of that
///     host-building code ever runs.
///     <para>
///         <b>This must run as the literal first statement of <c>Program.cs</c>, strictly before
///         <c>WebApplication.CreateBuilder(args)</c></b> -- that single call is what actually reads
///         every one of these variables (to pick the hosting environment, the Kestrel listen URLs,
///         and the content root), and it does so before any other code in this project's own
///         composition root gets a chance to run. There is no way to intercept or override what it
///         reads except by making sure the *real* variable already holds the right value before it
///         is called.
///     </para>
///     <para>
///         <b>A real, deliberate scope limit, not an oversight</b>: this bootstrap step can only
///         ever reach a variable that .NET's own *managed* host-building code reads -- it cannot
///         reach a variable the native hosting layer itself consults before any managed code,
///         including this very method, ever executes. The clearest real example is Server GC
///         selection (<c>DOTNET_gcServer</c> and friends): the CLR's garbage collector is already
///         fully initialized, using whatever the process's real environment held at process
///         startup, before the very first line of any C# method -- this one included -- ever runs.
///         No amount of "earlier" managed code can change that, so those settings are handled
///         separately, by <see cref="NativeRuntimeSettingsRelaunch" />, which relaunches the
///         process with them already in its environment. This class therefore covers only the
///         framework variables reachable managed code actually reads -- see
///         <see cref="Mappings" /> for the exact list.
///     </para>
/// </summary>
public static class FrameworkEnvironmentVariableBootstrap
{
    /// <summary>
    ///     This field lists every framework-level environment variable this bootstrap step
    ///     redirects, each paired with its own <c>NINLIL_</c>-prefixed equivalent. Every one of
    ///     these is read by genuinely reachable *managed* host-building code (never by the native
    ///     hosting layer before managed code starts) -- confirmed for real against a running
    ///     instance as part of this phase's own verification, not assumed from documentation alone.
    ///     <list type="bullet">
    ///         <item>
    ///             <c>ASPNETCORE_ENVIRONMENT</c>/<c>DOTNET_ENVIRONMENT</c> -- the hosting
    ///             environment name (<c>Development</c>/<c>Production</c>/...); ASP.NET Core's own
    ///             WebHost-specific variable takes precedence over the generic host's own when both
    ///             are set, exactly the same precedence this project preserves.
    ///         </item>
    ///         <item>
    ///             <c>ASPNETCORE_URLS</c> -- the semicolon-separated list of URLs Kestrel binds to
    ///             by default (this project's own <c>WebListenOptions</c> configures Kestrel
    ///             directly and does not itself rely on this variable, but an operator is still
    ///             free to set it, and this project's own default port fallback logic still
    ///             consults it).
    ///         </item>
    ///         <item>
    ///             <c>ASPNETCORE_HTTP_PORTS</c>/<c>ASPNETCORE_HTTPS_PORTS</c> -- the newer,
    ///             simpler "just the port numbers" alternative to <c>ASPNETCORE_URLS</c>.
    ///         </item>
    ///         <item>
    ///             <c>ASPNETCORE_CONTENTROOT</c>/<c>DOTNET_CONTENTROOT</c> -- where the host looks
    ///             for <c>appsettings.json</c>/<c>wwwroot</c>; the WebHost-specific variable again
    ///             takes precedence over the generic host's own.
    ///         </item>
    ///     </list>
    /// </summary>
    private static readonly (string NinlilName, string RealName)[] Mappings =
    [
        ("NINLIL_ASPNETCORE_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT"),
        ("NINLIL_DOTNET_ENVIRONMENT", "DOTNET_ENVIRONMENT"),
        ("NINLIL_ASPNETCORE_URLS", "ASPNETCORE_URLS"),
        ("NINLIL_ASPNETCORE_HTTP_PORTS", "ASPNETCORE_HTTP_PORTS"),
        ("NINLIL_ASPNETCORE_HTTPS_PORTS", "ASPNETCORE_HTTPS_PORTS"),
        ("NINLIL_ASPNETCORE_CONTENTROOT", "ASPNETCORE_CONTENTROOT"),
        ("NINLIL_DOTNET_CONTENTROOT", "DOTNET_CONTENTROOT"),
    ];

    /// <summary>
    ///     This property lists every <c>NINLIL_</c> name this bootstrap step itself recognizes --
    ///     used only so <c>Program.cs</c> can exclude these names from
    ///     <see cref="Ninlil.Common.Configuration.NinlilEnvironmentVariablesConfigurationProvider.UnrecognizedVariableNames" />'s
    ///     own startup warning; a framework variable is handled entirely by this class, not by that
    ///     provider's own Options-class mapping table, so it would otherwise be misreported as an
    ///     unrecognized/mistyped name despite being spelled and used correctly.
    /// </summary>
    public static IReadOnlyCollection<string> RecognizedVariableNames { get; } =
        Mappings.Select(mapping => mapping.NinlilName).ToArray();

    /// <summary>
    ///     This method performs the actual redirection described above. Safe to call more than
    ///     once (each call simply re-reads the same <c>NINLIL_</c>-prefixed variables and
    ///     re-applies them); a <c>NINLIL_</c>-prefixed variable that is not currently set is
    ///     skipped outright, leaving whatever real variable it would have mapped to completely
    ///     untouched.
    /// </summary>
    public static void Apply()
    {
        foreach ((string ninlilName, string realName) in Mappings)
        {
            string value = Environment.GetEnvironmentVariable(ninlilName);

            if (value is not null)
                Environment.SetEnvironmentVariable(realName, value);
        }
    }
}

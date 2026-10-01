// Define our namespace.
namespace Ninlil.Web.EnvironmentVariables;

/// <summary>
///     This static class copies the <c>NINLIL_</c>-prefixed form of each framework-level
///     environment variable (one read by the .NET generic host or ASP.NET Core host builder, not
///     by an options class), such as <c>NINLIL_ASPNETCORE_ENVIRONMENT</c>, onto the real variable
///     with <see cref="Environment.SetEnvironmentVariable(string, string)" />.
///     <para>
///         <b>It must run before <c>WebApplication.CreateBuilder(args)</c></b>, which reads these
///         variables to choose the hosting environment, listen URLs and content root. The only
///         way to change what it reads is to set the real variables first, so <c>Program.cs</c>
///         calls this right after <see cref="NativeRuntimeSettingsRelaunch" />.
///     </para>
///     <para>
///         <b>Only managed code is reached.</b> Settings the native runtime reads before any C#
///         runs, such as server garbage collection (<c>DOTNET_gcServer</c>), are already fixed
///         by the time this method runs. <see cref="NativeRuntimeSettingsRelaunch" /> handles
///         those by relaunching the process. <see cref="Mappings" /> lists the variables this
///         class covers.
///     </para>
/// </summary>
public static class FrameworkEnvironmentVariableBootstrap
{
    /// <summary>
    ///     This field lists each framework-level variable this class sets, with its
    ///     <c>NINLIL_</c>-prefixed source. All of them are read by managed host-building code, not
    ///     by the native host before managed code starts.
    ///     <list type="bullet">
    ///         <item>
    ///             <c>ASPNETCORE_ENVIRONMENT</c> and <c>DOTNET_ENVIRONMENT</c>: the hosting
    ///             environment name (<c>Development</c>, <c>Production</c> and so on). When both
    ///             are set, ASP.NET Core's variable wins over the generic host's, and copying both
    ///             keeps that order.
    ///         </item>
    ///         <item>
    ///             <c>ASPNETCORE_URLS</c>: the semicolon-separated URLs Kestrel binds by default.
    ///             <c>WebListenOptions</c> configures Kestrel's endpoints explicitly, and explicit
    ///             endpoints take precedence, so this rarely has an effect; it is copied so an
    ///             operator setting it gets the framework's normal behavior.
    ///         </item>
    ///         <item>
    ///             <c>ASPNETCORE_HTTP_PORTS</c> and <c>ASPNETCORE_HTTPS_PORTS</c>: port-only
    ///             alternatives to <c>ASPNETCORE_URLS</c>, with the same precedence.
    ///         </item>
    ///         <item>
    ///             <c>ASPNETCORE_CONTENTROOT</c> and <c>DOTNET_CONTENTROOT</c>: where the host
    ///             looks for <c>appsettings.json</c> and <c>wwwroot</c>; again the ASP.NET Core
    ///             variable wins.
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
    ///     This property lists the <c>NINLIL_</c> names this class handles. <c>Program.cs</c>
    ///     leaves them out of the startup warning built from
    ///     <see cref="Ninlil.Common.Configuration.NinlilEnvironmentVariablesConfigurationProvider.UnrecognizedVariableNames" />:
    ///     that provider maps only options-class variables, so it would otherwise report these as
    ///     unrecognized.
    /// </summary>
    public static IReadOnlyCollection<string> RecognizedVariableNames { get; } =
        Mappings.Select(mapping => mapping.NinlilName).ToArray();

    /// <summary>
    ///     This method copies each set <c>NINLIL_</c> variable onto its real name. A
    ///     <c>NINLIL_</c> variable that is not set leaves the real one unchanged. Calling it again
    ///     repeats the same copy, so it is safe to call more than once.
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

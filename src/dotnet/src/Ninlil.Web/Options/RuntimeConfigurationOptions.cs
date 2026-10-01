// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DnsServer:RuntimeSettings</c> configuration section: the JSON
///     file that preset application and every runtime settings controller (and the settings
///     cluster sync applier) merge writes into. It defaults to <c>appsettings.json</c> in the
///     content root, set in <c>Program.cs</c>, and can be pointed elsewhere, mainly so integration
///     tests write to a throwaway file instead of leaving the shared one changed between runs.
///     <para>
///         A write takes effect at once only for code that reads the setting through
///         <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> each time. Settings
///         read through <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />, or read
///         once when a listener or service starts, need a restart. Each settings
///         <c>PATCH</c> says which applies through its <see cref="Models.RuntimeSettingsWriteResult" />
///         note.
///     </para>
/// </summary>
public sealed class RuntimeConfigurationOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:RuntimeSettings";

    /// <summary>
    ///     This property contains the JSON configuration file a runtime settings write is merged
    ///     into. When <see langword="null" /> or empty, <c>Program.cs</c> sets it to
    ///     <c>appsettings.json</c> in the content root.
    /// </summary>
    public string ConfigurationFilePath { get; set; }
}

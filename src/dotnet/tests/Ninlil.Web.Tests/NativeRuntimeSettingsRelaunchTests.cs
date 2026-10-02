// Define our imports.
using Ninlil.Web.EnvironmentVariables;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers, without a host, how <see cref="NativeRuntimeSettingsRelaunch" /> maps a
///     <c>NINLIL_DOTNET_*</c> variable onto the case-sensitive setting name the .NET runtime
///     reads. That mapping decides whether the process relaunches at all, and under which
///     spelling a setting reaches the runtime.
/// </summary>
public sealed class NativeRuntimeSettingsRelaunchTests
{
    /// <summary>
    ///     Confirms an upper-case, word-split name resolves to the runtime's mixed-case spelling,
    ///     with or without the word-break underscores.
    /// </summary>
    [Theory]
    [InlineData("NINLIL_DOTNET_GC_SERVER", "DOTNET_gcServer")]
    [InlineData("NINLIL_DOTNET_GCSERVER", "DOTNET_gcServer")]
    [InlineData("NINLIL_DOTNET_GC_HEAP_HARD_LIMIT", "DOTNET_GCHeapHardLimit")]
    [InlineData("NINLIL_DOTNET_TIERED_PGO", "DOTNET_TieredPGO")]
    [InlineData("NINLIL_DOTNET_TC_QUICK_JIT_FOR_LOOPS", "DOTNET_TC_QuickJitForLoops")]
    [InlineData(
        "NINLIL_DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",
        "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"
    )]
    public void ResolveRuntimeNameMapsKnownSettings(string name, string expected) =>
        Assert.Equal(expected, NativeRuntimeSettingsRelaunch.ResolveRuntimeName(name));

    /// <summary>
    ///     Confirms the hosting variables handled in-process, unknown settings, and variables
    ///     outside the prefix are never a reason to relaunch.
    /// </summary>
    [Theory]
    [InlineData("NINLIL_DOTNET_ENVIRONMENT")]
    [InlineData("NINLIL_DOTNET_CONTENTROOT")]
    [InlineData("NINLIL_DOTNET_CONTENT_ROOT")]
    [InlineData("NINLIL_DOTNET_NOT_A_REAL_SETTING")]
    [InlineData("DOTNET_gcServer")]
    [InlineData("NINLIL_STORAGE_ENGINE")]
    public void ResolveRuntimeNameIgnoresEverythingElse(string name) =>
        Assert.Null(NativeRuntimeSettingsRelaunch.ResolveRuntimeName(name));
}

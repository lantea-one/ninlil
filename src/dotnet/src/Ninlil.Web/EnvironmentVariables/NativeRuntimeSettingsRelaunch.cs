// Define our imports.
using System.Runtime.InteropServices;

// Define our namespace.
namespace Ninlil.Web.EnvironmentVariables;

/// <summary>
///     This static class applies the <c>NINLIL_DOTNET_*</c> spelling of the .NET runtime settings
///     that the native runtime reads before any managed code runs (server garbage collection, heap
///     limits, tiered compilation, diagnostics, globalization mode). It does so by replacing the
///     process image with a fresh copy of itself whose environment carries each setting under the
///     runtime's exact <c>DOTNET_*</c> name.
///     <para>
///         <see cref="FrameworkEnvironmentVariableBootstrap" /> can only reach variables that
///         managed host-building code reads; by the time the first line of <c>Program.cs</c>
///         executes, the garbage collector and JIT are already configured from the environment the
///         process started with. The only way to change them from inside the process is to start
///         over, so when at least one recognized <c>NINLIL_DOTNET_*</c> setting is present this
///         method calls <c>execve(2)</c> on <c>/proc/self/exe</c> with the original command line
///         and the translated environment. <c>execve</c> keeps the process ID, the open standard
///         streams, and the parent relationship, so a container runtime or service manager that
///         started this process keeps supervising (and signalling) the same PID.
///     </para>
///     <para>
///         Matching ignores case and underscores, because the runtime's names are
///         case-sensitive and mixed-case (<c>DOTNET_gcServer</c> works, <c>DOTNET_GCSERVER</c> is
///         silently ignored) while <c>NINLIL_</c> names are upper-case with word breaks:
///         <c>NINLIL_DOTNET_GC_SERVER</c> and <c>NINLIL_DOTNET_GCSERVER</c> both become
///         <c>DOTNET_gcServer</c>. The translated <c>NINLIL_DOTNET_*</c> variables are dropped
///         from the new environment, which is also what stops the relaunched process from doing
///         this a second time. A <c>NINLIL_DOTNET_*</c> name that matches no known setting is left
///         untouched and reported as unrecognized at startup, rather than being passed through
///         under a guessed spelling the runtime might silently ignore.
///     </para>
///     <para>
///         Linux only: it relies on <c>/proc/self/exe</c> and <c>/proc/self/cmdline</c>. On other
///         platforms, set these settings under their real <c>DOTNET_*</c> names instead.
///     </para>
/// </summary>
public static class NativeRuntimeSettingsRelaunch
{
    /// <summary>
    ///     This constant contains the prefix every translatable variable carries.
    /// </summary>
    private const string VariablePrefix = "NINLIL_DOTNET_";

    /// <summary>
    ///     This field lists the runtime settings this class knows, spelled exactly as the runtime
    ///     reads them after the <c>DOTNET_</c> prefix.
    /// </summary>
    private static readonly string[] KnownSettings =
    [
        "gcServer",
        "gcConcurrent",
        "GCHeapCount",
        "GCHeapAffinitizeMask",
        "GCHeapAffinitizeRanges",
        "GCNoAffinitize",
        "GCHeapHardLimit",
        "GCHeapHardLimitPercent",
        "GCConserveMemory",
        "GCRetainVM",
        "GCgen0size",
        "GCDynamicAdaptationMode",
        "TieredCompilation",
        "TC_QuickJitForLoops",
        "TieredPGO",
        "ReadyToRun",
        "EnableDiagnostics",
        "DbgEnableMiniDump",
        "DbgMiniDumpType",
        "DbgMiniDumpName",
        "SYSTEM_GLOBALIZATION_INVARIANT",
        "SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY",
    ];

    /// <summary>
    ///     This field lists the <c>NINLIL_DOTNET_*</c> suffixes that are hosting variables rather
    ///     than runtime settings; <see cref="FrameworkEnvironmentVariableBootstrap" /> redirects
    ///     those in-process, so they are never a reason to relaunch.
    /// </summary>
    private static readonly string[] HostingVariableSuffixes =
    [
        "ENVIRONMENT",
        "CONTENTROOT",
        "CONTENT_ROOT",
    ];

    /// <summary>
    ///     This method relaunches the current process with every recognized
    ///     <c>NINLIL_DOTNET_*</c> setting translated to its real <c>DOTNET_*</c> name, and returns
    ///     normally (changing nothing) when there is nothing to translate, when not running on
    ///     Linux, or when the relaunch itself fails. It must be the first statement of
    ///     <c>Program.cs</c>, before any other startup work is wasted on a process image that is
    ///     about to be replaced.
    /// </summary>
    public static void Apply()
    {
        if (!OperatingSystem.IsLinux())
            return;

        // Split the current environment into the variables to translate and everything else.
        Dictionary<string, string> environment = [];
        Dictionary<string, string> translated = [];
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            string name = (string)entry.Key;
            string value = (string)entry.Value ?? string.Empty;
            string runtimeName = ResolveRuntimeName(name);
            if (runtimeName is null)
                environment[name] = value;
            else
                translated[runtimeName] = value;
        }

        if (translated.Count == 0)
            return;

        // A translated setting overrides a DOTNET_ variable of the same name that was also set,
        // exactly as an explicit NINLIL_ setting overrides everything else in this application.
        foreach ((string runtimeName, string value) in translated)
            environment[runtimeName] = value;

        string[] arguments;
        try
        {
            // /proc/self/cmdline holds the original argv, NUL-separated, including argv[0] and
            // (under `dotnet Ninlil.Web.dll`) the host's own arguments, which
            // Environment.GetCommandLineArgs() does not report faithfully.
            arguments = File.ReadAllText("/proc/self/cmdline").TrimEnd('\0').Split('\0');
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(
                $"Could not read the command line to apply {string.Join(", ", translated.Keys)}; "
                    + $"continuing without them: {exception.Message}"
            );
            return;
        }

        Console.Error.WriteLine(
            $"Restarting to apply .NET runtime settings: {string.Join(", ", translated.Keys)}"
        );
        Console.Error.Flush();

        // execve only returns on failure; on success this process image is gone.
        Execute(
            "/proc/self/exe",
            arguments,
            environment.Select(pair => $"{pair.Key}={pair.Value}").ToArray()
        );
        Console.Error.WriteLine(
            $"Could not restart to apply .NET runtime settings (errno {Marshal.GetLastPInvokeError()}); "
                + "continuing without them."
        );
    }

    /// <summary>
    ///     This method maps an environment variable name onto the runtime setting it spells, e.g.
    ///     <c>NINLIL_DOTNET_GC_SERVER</c> to <c>DOTNET_gcServer</c>, or returns
    ///     <see langword="null" /> when the name is not a recognized <c>NINLIL_DOTNET_*</c>
    ///     runtime setting.
    /// </summary>
    /// <param name="name">The environment variable name to resolve.</param>
    /// <returns>The runtime's variable name, or <see langword="null" />.</returns>
    public static string ResolveRuntimeName(string name)
    {
        if (!name.StartsWith(VariablePrefix, StringComparison.Ordinal))
            return null;

        string suffix = name[VariablePrefix.Length..];
        if (HostingVariableSuffixes.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            return null;

        // Compare with underscores removed and case ignored, since the runtime's spellings are
        // mixed-case and only sometimes contain underscores of their own.
        string wanted = Normalize(suffix);
        string match = KnownSettings.FirstOrDefault(setting => Normalize(setting) == wanted);
        return match is null ? null : $"DOTNET_{match}";
    }

    /// <summary>
    ///     This method upper-cases a name and removes its underscores, for comparison only.
    /// </summary>
    /// <param name="value">The name to normalize.</param>
    /// <returns>The normalized name.</returns>
    private static string Normalize(string value) =>
        value.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>
    ///     This method marshals the path, argument vector, and environment into NUL-terminated
    ///     UTF-8 strings and NULL-terminated pointer arrays, then calls <c>execve(2)</c>. It only
    ///     returns if <c>execve</c> fails, after freeing everything it allocated.
    /// </summary>
    /// <param name="path">The executable to run.</param>
    /// <param name="arguments">The argument vector, including <c>argv[0]</c>.</param>
    /// <param name="environment">The environment, as <c>NAME=value</c> strings.</param>
    private static void Execute(string path, string[] arguments, string[] environment)
    {
        IntPtr[] argumentPointers = ToPointerArray(arguments);
        IntPtr[] environmentPointers = ToPointerArray(environment);
        try
        {
            _ = ExecuteNative(path, argumentPointers, environmentPointers);
        }
        finally
        {
            foreach (IntPtr pointer in argumentPointers.Concat(environmentPointers))
                Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>
    ///     This method copies each string into unmanaged memory and returns the pointers followed
    ///     by the NULL terminator <c>execve</c> expects.
    /// </summary>
    /// <param name="values">The strings to copy.</param>
    /// <returns>The pointer array, NULL-terminated.</returns>
    private static IntPtr[] ToPointerArray(string[] values)
    {
        IntPtr[] pointers = new IntPtr[values.Length + 1];
        for (int index = 0; index < values.Length; index++)
            pointers[index] = Marshal.StringToCoTaskMemUTF8(values[index]);
        return pointers;
    }

    /// <summary>
    ///     This method is the <c>execve(2)</c> system call wrapper from the C library.
    /// </summary>
    /// <param name="path">The executable to run.</param>
    /// <param name="argv">The NULL-terminated argument pointer array.</param>
    /// <param name="envp">The NULL-terminated environment pointer array.</param>
    /// <returns><c>-1</c> on failure; never returns on success.</returns>
    [DllImport("libc", EntryPoint = "execve", SetLastError = true)]
    private static extern int ExecuteNative(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr[] argv,
        IntPtr[] envp
    );
}

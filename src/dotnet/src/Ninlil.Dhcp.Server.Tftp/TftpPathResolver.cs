// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp;

/// <summary>
///     This class turns a filename from a TFTP Read Request into the real path of a file inside
///     the configured root directory, or refuses it. <see cref="TftpServer" /> calls
///     <see cref="ResolveRealRoot" /> once when it starts and <see cref="TryResolve" /> for each
///     request.
///     <para>
///         <b>Symbolic links.</b> A link is followed only when every step of the walk and the final
///         real path stay inside the real root, so PXE trees with in-root links such as
///         <c>pxelinux.cfg/default</c> work and a link to <c>/etc</c> does not. The walk uses
///         <see cref="FileSystemInfo.LinkTarget" />, which works on every OS without native calls.
///         At most <see cref="MaximumLinkHops" /> links are followed (the Linux limit), which ends
///         loops.
///     </para>
///     <para>
///         <b>Races.</b> The check and the later open are not atomic. Anyone who can write inside
///         the root can swap a file for a link between the two, and the root belongs to the
///         operator.
///     </para>
/// </summary>
internal static class TftpPathResolver
{
    /// <summary>
    ///     This constant is the most symbolic links one resolution follows, matching the Linux
    ///     <c>MAXSYMLINKS</c> limit.
    /// </summary>
    internal const int MaximumLinkHops = 40;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    ///     This method returns the real path of a directory: absolute, with every symbolic link
    ///     on the way resolved, and without a trailing separator (except for a filesystem root).
    ///     The root directory itself may be a link, for example <c>/srv/tftp</c> pointing at
    ///     <c>/data/tftp</c>. A link loop or a missing directory returns the full path unchanged.
    /// </summary>
    internal static string ResolveRealRoot(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        string resolved = Walk(
            Path.GetPathRoot(fullPath),
            SplitComponents(fullPath[Path.GetPathRoot(fullPath).Length..]),
            confineTo: null
        );
        return resolved ?? TrimTrailingSeparator(fullPath);
    }

    /// <summary>
    ///     This method resolves a requested filename against the real root.
    /// </summary>
    /// <param name="realRoot">The directory from <see cref="ResolveRealRoot" />.</param>
    /// <param name="requested">The filename as the client sent it.</param>
    /// <param name="realPath">The real path of the existing file, when the method succeeds.</param>
    /// <param name="reason">A message for the client and the log, when the method fails.</param>
    /// <param name="notFound">
    ///     <see langword="true" /> when the path is allowed but names no file (a missing file, a
    ///     directory or a dangling link), <see langword="false" /> when it is refused.
    /// </param>
    /// <returns><see langword="true" /> when <paramref name="realPath" /> can be opened.</returns>
    internal static bool TryResolve(
        string realRoot,
        string requested,
        out string realPath,
        out string reason,
        out bool notFound
    )
    {
        realPath = null;
        reason = null;
        notFound = false;

        // A leading "/" means "from the root" for TFTP clients, as in other TFTP servers.
        string relative = requested?.TrimStart('/');
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\0'))
        {
            reason = "No filename was given.";
            return false;
        }

        if (Path.IsPathRooted(relative))
        {
            reason = "Absolute paths are not permitted.";
            return false;
        }

        string root = TrimTrailingSeparator(realRoot);
        string fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsUnder(fullPath, root))
        {
            reason = "The requested path is outside this server's configured root directory.";
            return false;
        }

        string resolved = Walk(
            root,
            SplitComponents(Path.GetRelativePath(root, fullPath)),
            confineTo: root
        );
        if (resolved == null)
        {
            reason = "The requested path leads outside this server's configured root directory.";
            return false;
        }

        if (!File.Exists(resolved))
        {
            reason = $"'{requested}' does not exist.";
            notFound = true;
            return false;
        }

        realPath = resolved;
        return true;
    }

    /// <summary>
    ///     This method walks the components from a start directory and returns the real path, or
    ///     <see langword="null" /> when a step leaves <paramref name="confineTo" /> or more than
    ///     <see cref="MaximumLinkHops" /> links are met. A link restarts the walk from the confining
    ///     root (or the filesystem root) with the link target's components in front of the rest.
    /// </summary>
    private static string Walk(string start, string[] components, string confineTo)
    {
        Stack<string> pending = new(components.Reverse());
        string current = start;
        int hops = 0;

        while (pending.Count > 0)
        {
            string candidate = Path.Combine(current, pending.Pop());
            string target = new FileInfo(candidate).LinkTarget;
            if (target == null)
            {
                current = candidate;
                continue;
            }

            if (++hops > MaximumLinkHops)
                return null;

            string targetPath = Path.GetFullPath(target, current);
            if (confineTo != null && !IsUnder(targetPath, confineTo))
                return null;

            string restartRoot = confineTo ?? Path.GetPathRoot(targetPath);
            string[] targetComponents = SplitComponents(
                Path.GetRelativePath(restartRoot, targetPath)
            );
            foreach (string component in targetComponents.Reverse())
                pending.Push(component);

            current = restartRoot;
        }

        return TrimTrailingSeparator(current);
    }

    private static string[] SplitComponents(string relativePath) =>
        relativePath
            .Split(['/', Path.DirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != ".")
            .ToArray();

    private static string TrimTrailingSeparator(string path)
    {
        string trimmed = path.TrimEnd('/', Path.DirectorySeparatorChar);
        return trimmed.Length == 0 || trimmed.Length < Path.GetPathRoot(path).Length
            ? Path.GetPathRoot(path)
            : trimmed;
    }

    private static bool IsUnder(string path, string root)
    {
        if (string.Equals(path, root, PathComparison))
            return true;

        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathComparison);
    }
}

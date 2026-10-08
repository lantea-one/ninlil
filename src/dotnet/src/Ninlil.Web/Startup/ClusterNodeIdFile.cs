using System.Text;
using Ninlil.Dns.Common.IO;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class reads or creates <c>cluster-node-id.txt</c>, the file that keeps this node's
///     cluster identity across restarts. <c>Program.cs</c> calls it at startup when
///     <c>Cluster:NodeId</c> is not configured. Every peer that joins records the id as its
///     <c>RemoteNodeId</c>, so an id that changed silently would orphan those peers. For that
///     reason an existing file that is empty is an error, never a reason to generate a new id.
/// </summary>
public static class ClusterNodeIdFile
{
    /// <summary>
    ///     This method returns the node id stored in <paramref name="path" />. A missing file gets
    ///     a newly generated id, written atomically and readable by its owner only.
    /// </summary>
    /// <param name="path">The full path of <c>cluster-node-id.txt</c>.</param>
    /// <exception cref="InvalidOperationException">
    ///     The file exists but is empty or only whitespace. The message names the file and the fix.
    /// </exception>
    public static string ReadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            string generated = Guid.NewGuid().ToString();

            AtomicFile.WriteAllBytes(
                path,
                Encoding.UTF8.GetBytes(generated),
                AtomicFilePermissions.OwnerOnly
            );

            return generated;
        }

        string existing = File.ReadAllText(path).Trim();

        if (existing.Length == 0)
            throw new InvalidOperationException(
                $"The node id file '{path}' exists but is empty. Ninlil will not generate a new "
                    + "node id, because every cluster peer has recorded the old one. Restore the "
                    + "original file from a backup, or delete the file only if this node has never "
                    + "joined a cluster."
            );

        return existing;
    }
}

using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class lowercases the names of TSIG keys stored with capital letters. The TSIG key API
///     stores every name in lowercase (DNS names compare without case, RFC 4343, and the MAC
///     covers the key name in lowercase canonical wire format, RFC 8945 §4.3.3 and RFC 4034
///     §6.2), so a mixed-case row written earlier would not be the name a signature covers.
///     <c>Program.cs</c> runs this once per startup after the schema is
///     ready; it does nothing when every name is already lowercase.
///     <para>
///         It goes through <see cref="ITsigKeyRepository" />, so one implementation covers
///         SQLite, PostgreSQL, MySQL, SQL Server and MongoDB with no schema change. Two keys whose
///         names differ only in case cannot both be lowercased, so the whole group is left
///         untouched and a warning names it for the operator to resolve. <c>TsigKeyLookupCache</c>
///         still finds such a row by its exact spelling.
///     </para>
/// </summary>
public static class TsigKeyNameNormalizer
{
    /// <summary>
    ///     This constant contains how many keys are read per page while collecting every key.
    /// </summary>
    private const int PageSize = 500;

    /// <summary>
    ///     This method lowercases every key name that can be lowercased without a clash.
    /// </summary>
    /// <param name="tsigKeyRepository">Reads and updates the keys.</param>
    /// <param name="unitOfWork">Commits the renames.</param>
    /// <param name="logger">Receives the collision warnings.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of keys renamed.</returns>
    public static async Task<int> RunAsync(
        ITsigKeyRepository tsigKeyRepository,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken stoppingToken = default
    )
    {
        // Every key is read before any is renamed, because a rename moves a row in the name-ordered
        // paging and would make a later page skip or repeat rows.
        List<TsigKeyEntity> keys = [];
        while (true)
        {
            IReadOnlyList<TsigKeyEntity> page = await tsigKeyRepository.SearchAsync(
                keys.Count,
                PageSize,
                stoppingToken
            );
            keys.AddRange(page);
            if (page.Count < PageSize)
                break;
        }

        int renamed = 0;
        foreach (
            IGrouping<string, TsigKeyEntity> group in keys.GroupBy(
                key => key.Name.ToLowerInvariant(),
                StringComparer.Ordinal
            )
        )
        {
            List<TsigKeyEntity> members = [.. group];
            if (members.Count > 1)
            {
                if (
                    members.Any(key =>
                        !string.Equals(key.Name, group.Key, StringComparison.Ordinal)
                    )
                )
                    logger.LogWarning(
                        "TSIG keys {KeyNames} differ only in letter case, so none was renamed to "
                            + "'{LowercaseName}'. Key names compare without case, so "
                            + "rename or delete the others.",
                        string.Join(", ", members.Select(key => $"'{key.Name}'")),
                        group.Key
                    );

                continue;
            }

            TsigKeyEntity key = members[0];
            if (string.Equals(key.Name, group.Key, StringComparison.Ordinal))
                continue;

            key.Name = group.Key;
            key.UpdatedAtUtc = DateTimeOffset.UtcNow;
            tsigKeyRepository.Update(key);
            renamed++;
        }

        if (renamed > 0)
            await unitOfWork.SaveChangesAsync(stoppingToken);

        return renamed;
    }
}

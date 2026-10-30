using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Backup;

/// <summary>
///     This class supplies the Swagger example for <see cref="BackupRestoreResponse" />, as
///     returned by <see cref="Controllers.BackupController.Restore" />.
/// </summary>
public class BackupRestoreResponseExample : IExamplesProvider<BackupRestoreResponse>
{
    /// <summary>
    ///     This method returns a restore of a small installation. The server lists every entity
    ///     type of <c>BackupEntityCatalog</c> under the name the archive uses (<c>Zones</c>,
    ///     <c>Records</c> and so on), with zero for a type the archive holds no rows of, and
    ///     <c>totalEntitiesImported</c> is the sum of the counts. The response always carries the
    ///     restart note.
    /// </summary>
    public BackupRestoreResponse GetExamples() =>
        new(
            57,
            new Dictionary<string, int>
            {
                ["Roles"] = 2,
                ["Users"] = 3,
                ["Sessions"] = 4,
                ["ApiTokens"] = 0,
                ["Zones"] = 2,
                ["Records"] = 24,
                ["TsigKeys"] = 1,
                ["ClusterNodes"] = 0,
                ["ClusterSyncedSettingsStates"] = 0,
                ["DnssecKeys"] = 0,
                ["CloudDnsProviderCredentials"] = 0,
                ["DnsFilterLists"] = 2,
                ["DnsFilterListEntries"] = 12,
                ["DnsClientGroups"] = 1,
                ["DnsClientGroupNetworks"] = 0,
                ["DnsClientGroupListAssignments"] = 0,
                ["DnsClientGroupSchedules"] = 0,
                ["DnsClients"] = 3,
                ["DnsClientAddresses"] = 0,
                ["DnsClientListAssignments"] = 0,
                ["DnsClientSchedules"] = 0,
                ["DnsClientStaticAddressMappings"] = 0,
                ["DnsAccessControlRules"] = 0,
                ["DnsForwarders"] = 0,
                ["WebhookSubscriptions"] = 0,
                ["DnsRewrites"] = 0,
                ["DhcpScopes"] = 1,
                ["DhcpReservations"] = 2,
                ["DhcpScopeCustomOptions"] = 0,
                ["DhcpTagRules"] = 0,
                ["DhcpTagRuleOptions"] = 0,
                ["DhcpLeases"] = 0,
            },
            BackupRestoreResponse.RestartRequiredNote
        );
}

using Microsoft.EntityFrameworkCore;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDhcpTagRuleOptionRepository" /> against the shared
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DhcpTagRuleOptionRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DhcpTagRuleOptionEntity>(context),
        IDhcpTagRuleOptionRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpTagRuleOptionEntity>> SearchByTagRuleIdAsync(
        Guid tagRuleId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(ruleOption => ruleOption.TagRuleId == tagRuleId)
            .OrderBy(ruleOption => ruleOption.OptionTag)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByTagRuleIdAsync(
        Guid tagRuleId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(ruleOption => ruleOption.TagRuleId == tagRuleId, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpTagRuleOptionEntity>> ListByTagRuleIdAsync(
        Guid tagRuleId,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(ruleOption => ruleOption.TagRuleId == tagRuleId).ToListAsync(stoppingToken);
}

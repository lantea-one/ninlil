using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class turns every successful <c>SaveChanges</c> on <see cref="NinlilDnsContext" />
///     into an <see cref="IEntityChangeNotifier" /> signal that names the CLR types of the
///     entities that were added, modified or deleted. It is registered once (a singleton) by
///     <see cref="Extension.ServiceCollectionEntityFrameworkCoreExtension" /> and attached to
///     every context, so no caller has to announce its own writes.
///     <para>
///         The signal means "committed". A save that fails, or that runs inside an explicit
///         transaction which is later rolled back, notifies nothing. A save inside an explicit
///         transaction (a backup restore, a filter-list refresh) is held until the transaction
///         commits, because a cache that reloaded before the commit would keep the old rows.
///         The class therefore also intercepts the transaction commit and rollback events.
///     </para>
///     <para>
///         Writes that bypass <c>SaveChanges</c> call <see cref="RecordBulkChange" /> themselves
///         (the filter-list entry replace does). Query-log and webhook-attempt retention deletes
///         raise no signal: no cache is built from those tables.
///     </para>
/// </summary>
/// <param name="notifier">The notifier the signal is raised on.</param>
public sealed class EntityChangeSaveChangesInterceptor(IEntityChangeNotifier notifier)
    : SaveChangesInterceptor,
        IDbTransactionInterceptor
{
    /// <summary>
    ///     This field keeps the per-context bookkeeping. A context is used by one thread at a
    ///     time, so the sets need no lock; the weak table lets a finished context be collected.
    /// </summary>
    private readonly ConditionalWeakTable<DbContext, State> _states = new();

    /// <summary>
    ///     This method records that a write which bypasses <c>SaveChanges</c> changed rows of an
    ///     entity type. The signal goes out at once when the context has no explicit transaction
    ///     (the statement was committed on its own), and at the commit of the transaction
    ///     otherwise.
    /// </summary>
    /// <param name="context">The context the statement ran on.</param>
    /// <param name="entityType">The CLR type of the entity whose rows changed.</param>
    public void RecordBulkChange(DbContext context, Type entityType) =>
        Complete(context, [entityType]);

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        Collect(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken stoppingToken = default
    )
    {
        Collect(eventData.Context);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Complete(eventData.Context, TakeSaving(eventData.Context));
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken stoppingToken = default
    )
    {
        Complete(eventData.Context, TakeSaving(eventData.Context));
        return new ValueTask<int>(result);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
        TakeSaving(eventData.Context);

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken stoppingToken = default
    )
    {
        TakeSaving(eventData.Context);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     This method forgets anything held for an earlier transaction that ended without a
    ///     commit or rollback event (disposing an open transaction raises neither).
    /// </summary>
    public DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result
    )
    {
        DropPending(eventData.Context);
        return result;
    }

    /// <inheritdoc cref="TransactionStarted" />
    public ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken stoppingToken = default
    )
    {
        DropPending(eventData.Context);
        return new ValueTask<DbTransaction>(result);
    }

    /// <inheritdoc />
    public void TransactionCommitted(
        DbTransaction transaction,
        TransactionEndEventData eventData
    ) => NotifyPending(eventData.Context);

    /// <inheritdoc />
    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken stoppingToken = default
    )
    {
        NotifyPending(eventData.Context);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void TransactionRolledBack(
        DbTransaction transaction,
        TransactionEndEventData eventData
    ) => DropPending(eventData.Context);

    /// <inheritdoc />
    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken stoppingToken = default
    )
    {
        DropPending(eventData.Context);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        DropPending(eventData.Context);

    /// <inheritdoc />
    public Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken stoppingToken = default
    )
    {
        DropPending(eventData.Context);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     This method remembers the entity types the pending save will write.
    /// </summary>
    private void Collect(DbContext context)
    {
        if (context is null)
            return;

        State state = _states.GetOrCreateValue(context);

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                state.Saving.Add(entry.Metadata.ClrType);
    }

    /// <summary>
    ///     This method returns and clears the types collected for the save that just ended.
    /// </summary>
    private Type[] TakeSaving(DbContext context)
    {
        if (context is null || !_states.TryGetValue(context, out State state))
            return [];

        Type[] types = [.. state.Saving];
        state.Saving.Clear();
        return types;
    }

    /// <summary>
    ///     This method raises the signal now, or holds the types until the explicit transaction
    ///     of the context ends.
    /// </summary>
    private void Complete(DbContext context, Type[] types)
    {
        if (types.Length == 0)
            return;

        ReportWrite(context);

        if (context?.Database.CurrentTransaction is not null)
        {
            State state = _states.GetOrCreateValue(context);

            foreach (Type type in types)
                state.Pending.Add(type);

            return;
        }

        notifier.NotifyChanged(types);
    }

    /// <summary>
    ///     This method tells the principal of the context's scope that a write happened.
    /// </summary>
    private static void ReportWrite(DbContext context)
    {
        if (context is null)
            return;

        context
            .GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()
            ?.ApplicationServiceProvider?.GetService<StoragePrincipal>()
            ?.ReportWrite();
    }

    /// <summary>
    ///     This method raises the signal for the types held until the commit.
    /// </summary>
    private void NotifyPending(DbContext context)
    {
        if (context is null || !_states.TryGetValue(context, out State state))
            return;

        Type[] types = [.. state.Pending];
        state.Pending.Clear();

        if (types.Length > 0)
            notifier.NotifyChanged(types);
    }

    /// <summary>
    ///     This method forgets the types held for a transaction that did not commit.
    /// </summary>
    private void DropPending(DbContext context)
    {
        if (context is not null && _states.TryGetValue(context, out State state))
            state.Pending.Clear();
    }

    /// <summary>
    ///     This class is the bookkeeping of one context.
    /// </summary>
    private sealed class State
    {
        /// <summary>
        ///     This property is the types of the save in progress.
        /// </summary>
        public HashSet<Type> Saving { get; } = [];

        /// <summary>
        ///     This property is the types saved inside the open transaction.
        /// </summary>
        public HashSet<Type> Pending { get; } = [];
    }
}

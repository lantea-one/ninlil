/**
 * This module computes the minimal set of create/update/delete calls a dynamic DHCP options
 * table needs to issue against its own real, per-row REST child collection
 * (`DhcpScopeCustomOptionsController`/`DhcpTagRuleOptionsController`, neither of which has a
 * bulk-replace endpoint) after an operator has freely added, edited, and removed rows in the
 * form -- this project's first inline, embedded-in-the-parent-form child-resource editor (every
 * other nested child collection in this app, e.g. a scope's own reservations, is still a
 * separate page), so this reconciliation step has no existing precedent to lean on.
 */

/** The minimum shape a reconciled row needs: its own persisted id, or `null` for a row never saved. */
export interface ReconcilableRow {
  readonly id: string | null;
}

/** The three REST calls {@link planDhcpOptionReconciliation} says this save needs to issue. */
export interface DhcpOptionReconciliationPlan<TRow extends ReconcilableRow> {
  /** Every row with no `id` yet -- each needs a `POST`. */
  readonly toCreate: readonly TRow[];
  /** Every row that still carries an `id` from the original load -- each needs a `PUT` (a full, unconditional replace, matching this project's own general "no partial PATCH for a simple child resource" convention). */
  readonly toUpdate: readonly TRow[];
  /** Every id present when this table was loaded but no longer present in the current rows -- each needs a `DELETE`. */
  readonly toDeleteIds: readonly string[];
}

/**
 * This function diffs {@link currentRows} (the table's live, operator-edited state) against
 * {@link originallyLoadedIds} (every row's own id as it stood right after this table was last
 * loaded from the server) into the three REST calls a save needs to make.
 */
export function planDhcpOptionReconciliation<TRow extends ReconcilableRow>(
  currentRows: readonly TRow[],
  originallyLoadedIds: readonly string[],
): DhcpOptionReconciliationPlan<TRow> {
  const toCreate = currentRows.filter((row) => row.id === null);
  const toUpdate = currentRows.filter((row) => row.id !== null);

  const currentIds = new Set(
    currentRows.map((row) => row.id).filter((id): id is string => id !== null),
  );
  const toDeleteIds = originallyLoadedIds.filter((id) => !currentIds.has(id));

  return { toCreate, toUpdate, toDeleteIds };
}

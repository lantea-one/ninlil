/**
 * This module works out the create, update and delete calls a DHCP options table on the scope
 * and tag rule forms must make on save. The option endpoints
 * (`DhcpScopeCustomOptionsController`, `DhcpTagRuleOptionsController`) work one row at a time
 * and have no bulk replace, so the form's edited rows are compared with what was loaded.
 */

/** The shape a row needs: its saved id, or `null` for a row not yet saved. */
export interface ReconcilableRow {
  readonly id: string | null;
}

/** The calls {@link planDhcpOptionReconciliation} says a save must make. */
export interface DhcpOptionReconciliationPlan<TRow extends ReconcilableRow> {
  /** Every row with no `id` yet; each needs a `POST`. */
  readonly toCreate: readonly TRow[];
  /** Every row with an `id`; each gets a `PUT` (full replace), whether or not it changed. */
  readonly toUpdate: readonly TRow[];
  /** Every id that was loaded but is no longer in the rows; each needs a `DELETE`. */
  readonly toDeleteIds: readonly string[];
}

/**
 * This function compares `currentRows` (the edited table) with `originallyLoadedIds` (the ids
 * loaded from the server) and returns the calls a save must make.
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

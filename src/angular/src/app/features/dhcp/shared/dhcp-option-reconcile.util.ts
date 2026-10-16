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

/**
 * The calls {@link planDhcpOptionReconciliation} says a save must make. The forms run them in the
 * order deletes, updates, creates, so a tag freed by a removed row can be taken by a new row in
 * the same save without hitting the unique `(parent, tag)` index.
 */
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
 * loaded from the server) and returns the calls a save must make. It only sorts the calls; the
 * caller decides the order and must run deletes first, then updates, then creates.
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

/** The first pair of rows that share an option tag, as {@link findDuplicateOptionTag} returns it. */
export interface DuplicateOptionTag {
  /** The tag both rows use. */
  readonly tag: number;
  /** The 1-based number of the earlier row. */
  readonly firstRow: number;
  /** The 1-based number of the later row. */
  readonly secondRow: number;
}

/**
 * This function finds two rows that use the same option tag, or returns `null`. The server keeps
 * one option per `(scope or rule, tag)`, so a duplicate would fail with a 500 after the parent
 * was already saved. The forms call this before any request and name both rows in the message.
 *
 * @param tags The option tags in row order.
 */
export function findDuplicateOptionTag(tags: readonly number[]): DuplicateOptionTag | null {
  const firstRowByTag = new Map<number, number>();

  for (let index = 0; index < tags.length; index++) {
    const firstRow = firstRowByTag.get(tags[index]);
    if (firstRow !== undefined) {
      return { tag: tags[index], firstRow, secondRow: index + 1 };
    }
    firstRowByTag.set(tags[index], index + 1);
  }

  return null;
}

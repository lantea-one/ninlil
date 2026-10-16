import { findDuplicateOptionTag, planDhcpOptionReconciliation } from './dhcp-option-reconcile.util';

interface TestRow {
  id: string | null;
  value: string;
}

/** These tests cover splitting edited option rows into creates, updates and deletes. */
describe('planDhcpOptionReconciliation', () => {
  it('treats every id-less row as a create, regardless of originally loaded ids', () => {
    const rows: TestRow[] = [{ id: null, value: 'a' }];
    const plan = planDhcpOptionReconciliation(rows, []);
    expect(plan.toCreate).toEqual(rows);
    expect(plan.toUpdate).toEqual([]);
    expect(plan.toDeleteIds).toEqual([]);
  });

  it('treats every row that still carries its loaded id as an update', () => {
    const rows: TestRow[] = [{ id: '1', value: 'a' }];
    const plan = planDhcpOptionReconciliation(rows, ['1']);
    expect(plan.toCreate).toEqual([]);
    expect(plan.toUpdate).toEqual(rows);
    expect(plan.toDeleteIds).toEqual([]);
  });

  it('deletes an originally loaded id no longer present among the current rows', () => {
    const rows: TestRow[] = [{ id: '1', value: 'a' }];
    const plan = planDhcpOptionReconciliation(rows, ['1', '2']);
    expect(plan.toDeleteIds).toEqual(['2']);
  });

  it('handles a mixed create/update/delete save in one pass', () => {
    const rows: TestRow[] = [
      { id: '1', value: 'kept' },
      { id: null, value: 'new' },
    ];
    const plan = planDhcpOptionReconciliation(rows, ['1', '2']);
    expect(plan.toCreate).toEqual([{ id: null, value: 'new' }]);
    expect(plan.toUpdate).toEqual([{ id: '1', value: 'kept' }]);
    expect(plan.toDeleteIds).toEqual(['2']);
  });

  it('produces no calls at all when nothing changed', () => {
    const rows: TestRow[] = [{ id: '1', value: 'a' }];
    const plan = planDhcpOptionReconciliation(rows, ['1']);
    expect(plan.toCreate).toEqual([]);
    expect(plan.toUpdate).toEqual(rows);
    expect(plan.toDeleteIds).toEqual([]);
  });
});

/** These tests cover the duplicate option tag check the forms run before saving. */
describe('findDuplicateOptionTag', () => {
  it('names the tag and the 1-based numbers of the first two rows that share it', () => {
    expect(findDuplicateOptionTag([6, 3, 6])).toEqual({ tag: 6, firstRow: 1, secondRow: 3 });
  });

  it('returns null when every tag is different', () => {
    expect(findDuplicateOptionTag([1, 2])).toBeNull();
    expect(findDuplicateOptionTag([])).toBeNull();
  });

  it('reports the earliest second occurrence when several tags repeat', () => {
    expect(findDuplicateOptionTag([5, 7, 7, 5])).toEqual({ tag: 7, firstRow: 2, secondRow: 3 });
  });
});

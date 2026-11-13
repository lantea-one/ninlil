import { assignBulkLineNumbers } from './bulk-line-numbers.util';

/**
 * This spec covers `assignBulkLineNumbers`, the helper the bulk forms of filter lists and
 * forwarders use to keep a line's number across retries. It runs on plain arrays and maps.
 */
describe('assignBulkLineNumbers', () => {
  it('numbers new lines from 1 in order', () => {
    const assigned = new Map<string, number>();
    expect(assignBulkLineNumbers(['a', 'b', 'c'], assigned)).toEqual([1, 2, 3]);
  });

  it('keeps the number of a line that is sent again after a failure', () => {
    const assigned = new Map<string, number>();
    assignBulkLineNumbers(['a', 'b', 'c'], assigned);
    expect(assignBulkLineNumbers(['c'], assigned)).toEqual([3]);
  });

  it('gives a new line a number above every number handed out so far', () => {
    const assigned = new Map<string, number>();
    assignBulkLineNumbers(['a', 'b'], assigned);
    expect(assignBulkLineNumbers(['b', 'd'], assigned)).toEqual([2, 3]);
  });
});

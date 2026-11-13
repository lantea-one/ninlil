/**
 * This module gives the lines of a bulk create form their line numbers. The forms name each new
 * entry after its line number, so a line keeps the number it was first given when it is sent again.
 */

/**
 * This function gives every line in `lines` that has no number yet in `assigned` the next unused
 * number, and leaves the numbers already there alone. A bulk create form calls it on each save
 * with one map that lives as long as the page, so a line that failed and is sent again keeps the
 * number (and so the name) it had on the first attempt, instead of taking its new position in the
 * shorter list of failed lines and clashing with an entry an earlier line created. A new or edited
 * line gets a number above every number handed out so far. It returns the number of each line,
 * in the order of `lines`.
 */
export function assignBulkLineNumbers(lines: string[], assigned: Map<string, number>): number[] {
  let next = Math.max(0, ...assigned.values()) + 1;
  return lines.map((line) => {
    if (!assigned.has(line)) assigned.set(line, next++);
    return assigned.get(line) as number;
  });
}

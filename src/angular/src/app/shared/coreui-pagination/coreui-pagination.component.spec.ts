import { TestBed } from '@angular/core/testing';
import { CoreuiPaginationComponent } from './coreui-pagination.component';

/**
 * These tests cover the pagination bar: the selected page size, page count and row range, the
 * first/last-page limits and the events each button emits. TestBed with inputs set on fresh
 * fixtures.
 */
describe('CoreuiPaginationComponent', () => {
  async function createComponent(pageIndex: number, pageSize: number, totalCount: number) {
    // Reset first: one test ("reports hasPrevious/hasNext correctly at each end") creates several
    // fixtures, and configuring TestBed again would otherwise fail with "module already
    // instantiated".
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [CoreuiPaginationComponent],
    }).compileComponents();

    const fixture = TestBed.createComponent(CoreuiPaginationComponent);
    fixture.componentRef.setInput('pageIndex', pageIndex);
    fixture.componentRef.setInput('pageSize', pageSize);
    fixture.componentRef.setInput('totalCount', totalCount);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the current page size as the selected rows-per-page option, not the first option', async () => {
    const fixture = await createComponent(0, 25, 60);
    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select');

    expect(select.value).toBe('25');
  });

  it('computes the total page count from totalCount/pageSize, rounding up', async () => {
    const fixture = await createComponent(0, 25, 60);

    expect(fixture.componentInstance.totalPages()).toBe(3);
  });

  it('always reports at least one page, even with zero results', async () => {
    const fixture = await createComponent(0, 25, 0);

    expect(fixture.componentInstance.totalPages()).toBe(1);
    expect(fixture.componentInstance.rangeStart()).toBe(0);
    expect(fixture.componentInstance.rangeEnd()).toBe(0);
  });

  it('computes the visible row range for a middle page', async () => {
    const fixture = await createComponent(1, 25, 60);

    expect(fixture.componentInstance.rangeStart()).toBe(26);
    expect(fixture.componentInstance.rangeEnd()).toBe(50);
  });

  it('reports hasPrevious/hasNext correctly at each end', async () => {
    const first = await createComponent(0, 25, 60);
    expect(first.componentInstance.hasPrevious()).toBeFalse();
    expect(first.componentInstance.hasNext()).toBeTrue();

    const last = await createComponent(2, 25, 60);
    expect(last.componentInstance.hasPrevious()).toBeTrue();
    expect(last.componentInstance.hasNext()).toBeFalse();
  });

  it('goToNext() emits the next page index, keeping the same page size', async () => {
    const fixture = await createComponent(0, 25, 60);
    const emitted: { pageIndex: number; pageSize: number }[] = [];
    fixture.componentInstance.pageChange.subscribe((event) => emitted.push(event));

    fixture.componentInstance.goToNext();

    expect(emitted).toEqual([{ pageIndex: 1, pageSize: 25 }]);
  });

  it('goToPrevious() does nothing past the first page (clamped, not emitted below zero)', async () => {
    const fixture = await createComponent(0, 25, 60);
    const emitted: { pageIndex: number; pageSize: number }[] = [];
    fixture.componentInstance.pageChange.subscribe((event) => emitted.push(event));

    fixture.componentInstance.goToPrevious();

    expect(emitted).toEqual([{ pageIndex: 0, pageSize: 25 }]);
  });

  it('goToLast() emits the final page index', async () => {
    const fixture = await createComponent(0, 25, 60);
    const emitted: { pageIndex: number; pageSize: number }[] = [];
    fixture.componentInstance.pageChange.subscribe((event) => emitted.push(event));

    fixture.componentInstance.goToLast();

    expect(emitted).toEqual([{ pageIndex: 2, pageSize: 25 }]);
  });

  it('onPageSizeChange() resets to the first page with the newly chosen size', async () => {
    const fixture = await createComponent(2, 25, 60);
    const emitted: { pageIndex: number; pageSize: number }[] = [];
    fixture.componentInstance.pageChange.subscribe((event) => emitted.push(event));

    fixture.componentInstance.onPageSizeChange({ target: { value: '50' } } as unknown as Event);

    expect(emitted).toEqual([{ pageIndex: 0, pageSize: 50 }]);
  });
});

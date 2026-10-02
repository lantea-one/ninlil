import { Component, computed, input, output } from '@angular/core';
import {
  FormSelectDirective,
  PageItemComponent,
  PageLinkDirective,
  PaginationComponent,
} from '@coreui/angular';

/** The page and page size {@link CoreuiPaginationComponent} emits on every change. */
export interface PageChangeEvent {
  /** The zero-based index of the newly selected page. */
  pageIndex: number;

  /** The page size in effect for the newly selected page. */
  pageSize: number;
}

/**
 * This component is the shared pagination bar for lists paged on the server (`skip`/`take`
 * query parameters), such as the zone, record and DNSSEC key lists. It shows the row range, a
 * rows-per-page selector and first/previous/next/last buttons, and emits {@link pageChange}; the
 * list then fetches that page.
 *
 * <p>CoreUI's `PaginationComponent` only draws page links and has no page-index or page-size
 * binding, so this component adds it.
 */
@Component({
  selector: 'app-coreui-pagination',
  imports: [PaginationComponent, PageItemComponent, PageLinkDirective, FormSelectDirective],
  templateUrl: './coreui-pagination.component.html',
  styleUrl: './coreui-pagination.component.scss',
})
export class CoreuiPaginationComponent {
  /** The zero-based index of the page currently shown. */
  readonly pageIndex = input.required<number>();

  /** How many rows one page holds. */
  readonly pageSize = input.required<number>();

  /** The total row count across every page, from the backend's paged response. */
  readonly totalCount = input.required<number>();

  /** The page-size choices offered in the size selector. */
  readonly pageSizeOptions = input<readonly number[]>([10, 25, 50, 100]);

  /** Emits whenever the operator selects a different page or a different page size. */
  readonly pageChange = output<PageChangeEvent>();

  /** The total number of pages, always at least 1 even when {@link totalCount} is 0. */
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));

  /** The 1-based index of the first row shown on the current page, or 0 when there are none. */
  readonly rangeStart = computed(() =>
    this.totalCount() === 0 ? 0 : this.pageIndex() * this.pageSize() + 1,
  );

  /** The 1-based index of the last row shown on the current page. */
  readonly rangeEnd = computed(() =>
    Math.min(this.totalCount(), (this.pageIndex() + 1) * this.pageSize()),
  );

  /** Whether a previous page exists. */
  readonly hasPrevious = computed(() => this.pageIndex() > 0);

  /** Whether a next page exists. */
  readonly hasNext = computed(() => this.pageIndex() < this.totalPages() - 1);

  /** This method moves to the first page. */
  goToFirst(): void {
    this.emitPage(0);
  }

  /** This method moves back one page. */
  goToPrevious(): void {
    this.emitPage(this.pageIndex() - 1);
  }

  /** This method moves forward one page. */
  goToNext(): void {
    this.emitPage(this.pageIndex() + 1);
  }

  /** This method moves to the last page. */
  goToLast(): void {
    this.emitPage(this.totalPages() - 1);
  }

  /** This method applies a newly chosen page size, resetting back to the first page. */
  onPageSizeChange(event: Event): void {
    const pageSize = Number((event.target as HTMLSelectElement).value);
    this.pageChange.emit({ pageIndex: 0, pageSize });
  }

  /** This method clamps and emits a page-index navigation, keeping the current page size. */
  private emitPage(pageIndex: number): void {
    const clamped = Math.min(Math.max(pageIndex, 0), this.totalPages() - 1);
    this.pageChange.emit({ pageIndex: clamped, pageSize: this.pageSize() });
  }
}

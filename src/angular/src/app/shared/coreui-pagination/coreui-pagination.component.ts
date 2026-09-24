import { Component, computed, input, output } from '@angular/core';
import {
  FormSelectDirective,
  PageItemComponent,
  PageLinkDirective,
  PaginationComponent,
} from '@coreui/angular';

/** The page/page-size pair one {@link CoreuiPaginationComponent} emits on every navigation. */
export interface PageChangeEvent {
  /** The zero-based index of the newly selected page. */
  pageIndex: number;

  /** The page size in effect for the newly selected page. */
  pageSize: number;
}

/**
 * This component is a small, reusable server-side pagination control, filling a real gap in
 * CoreUI for Angular's own component set: its free tier ships `PaginationComponent`/
 * `PageItemComponent` as purely presentational page-number chrome (`align`/`size`/`role` inputs,
 * content projected via `<ng-content>`), with no built-in `length`/`pageIndex`/`pageSize`/
 * `(page)`-style data-binding of its own -- so that binding is hand-rolled once, here, for every
 * list in this app that pushes pagination to the server (`skip`/`take` REST params) rather than
 * slicing an already-fully-fetched in-memory array. Kept in `shared/` rather than any one feature
 * folder since nothing about it is feature-specific -- every server-paginated list across this
 * app (`ZoneListComponent`/`RecordListComponent`/`DnssecKeyListComponent` among them) shares this
 * one implementation rather than each hand-rolling its own.
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

  /** The total row count across every page, as reported by the backend's own paged response. */
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

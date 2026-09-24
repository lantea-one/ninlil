import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { RouterLink } from '@angular/router';
import {
  BadgeComponent,
  ButtonDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import { ZoneDetail, ZonesService } from '../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/confirm-dialog/confirm-dialog.component';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../shared/if-permission/has-permission.pipe';

/** How many zones one page of this list shows -- matches the backend's own `PaginationQuery` default. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every zone this server hosts or references, backed by `GET /api/zones`'s
 * own real `skip`/`take` pagination (not a client-side slice of one large fetch), with actions to
 * view/manage a zone's records, edit its metadata, toggle it disabled (via `PATCH`, this feature's
 * own genuine exercise of the merge-patch path alongside `ZoneFormComponent`'s full-replace
 * `PUT`), and delete it (behind a confirmation dialog, since `ZonesController.Delete` also removes
 * every one of the zone's own records and DNSSEC keys -- a fact surfaced directly in the
 * confirmation message, not left implicit).
 */
@Component({
  selector: 'app-zone-list',
  imports: [
    IfPermissionDirective,
    HasPermissionPipe,
    RouterLink,
    CdkTableModule,
    ButtonDirective,
    BadgeComponent,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    EmptyStateComponent,
  ],
  templateUrl: './zone-list.component.html',
  styleUrl: './zone-list.component.scss',
})
export class ZoneListComponent {
  private readonly zonesApi = inject(ZonesService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'type', 'dnssec', 'disabled', 'actions'];

  readonly zones = signal<ZoneDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadPage();
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method toggles a zone's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
  async toggleDisabled(zone: ZoneDetail): Promise<void> {
    if (!zone.id) return;

    try {
      await firstValueFrom(
        this.zonesApi.apiZonesIdPatch({ id: zone.id, body: { isDisabled: !zone.isDisabled } }),
      );
      await this.loadPage();
      this.notification.success(zone.isDisabled ? 'Zone enabled.' : 'Zone disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this zone.'));
    }
  }

  /** This method confirms, then deletes, a zone -- including every one of its own records and DNSSEC keys. */
  async deleteZone(zone: ZoneDetail): Promise<void> {
    if (!zone.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${zone.name}?`,
      message:
        'This permanently deletes the zone, every record it contains (including its own SOA), and every DNSSEC key belonging to it. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.zonesApi.apiZonesIdDelete({ id: zone.id }));
      this.notification.success('Zone deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this zone.'));
    }
  }

  /**
   * This method shows {@link ConfirmDialogComponent} with the given text and resolves once the
   * operator confirms or dismisses it -- the declarative analogue of the old
   * `MatDialog.open(ConfirmDialogComponent, { data }).afterClosed()` this project used before
   * migrating onto CoreUI for Angular.
   */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.zonesApi.apiZonesGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.zones.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load zones.'));
    } finally {
      this.loading.set(false);
    }
  }
}

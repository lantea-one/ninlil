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
import { ForwarderDetail, ForwardersService } from '../../../api-client';
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
import { ClusterSyncInfoComponent } from '../../../shared/cluster-sync-info/cluster-sync-info.component';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../shared/if-permission/has-permission.pipe';

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the Forwarders page (`/forwarders`, `forwarders:read`). It lists the
 * server-wide upstream forwarders a page at a time from `GET /api/forwarders`, ordered by
 * `sortOrder`. Per-zone forwarder addresses are set on the zone form instead. The protocol
 * badge comes from the backend's parse of the definition. With `forwarders:write` it offers
 * create, edit, delete and an enable/disable toggle (`PATCH`); without it the toggle is disabled
 * and the rest hidden.
 */
@Component({
  selector: 'app-forwarder-list',
  imports: [
    IfPermissionDirective,
    HasPermissionPipe,
    RouterLink,
    CdkTableModule,
    BadgeComponent,
    ButtonDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    ClusterSyncInfoComponent,
    EmptyStateComponent,
  ],
  templateUrl: './forwarder-list.component.html',
  styleUrl: './forwarder-list.component.scss',
})
export class ForwarderListComponent {
  private readonly forwardersApi = inject(ForwardersService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = [
    'name',
    'definitionString',
    'protocol',
    'disabled',
    'sync',
    'actions',
  ];

  readonly forwarders = signal<ForwarderDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadPage();
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method flips a forwarder's `isDisabled` with a one-field merge patch (RFC 7396). */
  async toggleDisabled(forwarder: ForwarderDetail): Promise<void> {
    if (!forwarder.id) return;

    try {
      await firstValueFrom(
        this.forwardersApi.apiForwardersIdPatch({
          id: forwarder.id,
          body: { isDisabled: !forwarder.isDisabled },
        }),
      );
      await this.loadPage();
      this.notification.success(
        forwarder.isDisabled ? 'Forwarder enabled.' : 'Forwarder disabled.',
      );
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this forwarder.'));
    }
  }

  /** This method asks for confirmation, then deletes a forwarder. */
  async deleteForwarder(forwarder: ForwarderDetail): Promise<void> {
    if (!forwarder.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${forwarder.name}?`,
      message: 'This permanently removes this upstream forwarder. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.forwardersApi.apiForwardersIdDelete({ id: forwarder.id }));
      this.notification.success('Forwarder deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this forwarder.'));
    }
  }

  /**
   * This method shows {@link ConfirmDialogComponent} with the given text and resolves once the
   * operator confirms or dismisses it.
   */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method hides the confirmation dialog and settles the pending {@link confirm} promise. */
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
        this.forwardersApi.apiForwardersGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.forwarders.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load forwarders.'));
    } finally {
      this.loading.set(false);
    }
  }
}

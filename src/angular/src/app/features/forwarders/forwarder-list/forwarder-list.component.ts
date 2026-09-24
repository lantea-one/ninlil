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

/** How many forwarders one page of this list shows -- matches `ZoneListComponent`'s own default. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every global upstream forwarder this server knows about
 * (`DnsForwarderEntity` -- entirely separate from a zone's own per-zone `forwarderAddresses`
 * override, which is managed through `ZoneFormComponent` instead), backed by
 * `GET /api/forwarders`'s own real `skip`/`take` pagination, ordered by the forwarder's own
 * `sortOrder` the same way `ForwardersController.List` itself orders them. `Protocol` is shown
 * as a plain, read-only badge -- `ForwarderDetail.Protocol` is computed server-side from the
 * stored `definitionString` via the real parser, not something this UI derives itself.
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

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
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

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method toggles a forwarder's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
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

  /** This method confirms, then deletes, a forwarder. */
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

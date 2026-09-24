import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  ButtonDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  ClientGroupNetworkDetail,
  ClientGroupNetworksService,
  ClientGroupsService,
} from '../../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../../shared/confirm-dialog/confirm-dialog.component';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';

/** How many networks one page of this list shows -- matches every other list page's own precedent. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every CIDR network that matches a client into one client group, backed by
 * `GET /api/client-groups/{groupId}/networks`'s own real `skip`/`take` pagination. There is no
 * `PATCH` here, matching `ClientGroupNetworksController`'s own contract -- a network has exactly
 * one field, so a partial update would be indistinguishable from a full replace.
 */
@Component({
  selector: 'app-client-group-network-list',
  imports: [
    RouterLink,
    CdkTableModule,
    ButtonDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    EmptyStateComponent,
  ],
  templateUrl: './client-group-network-list.component.html',
  styleUrl: './client-group-network-list.component.scss',
})
export class ClientGroupNetworkListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly networksApi = inject(ClientGroupNetworksService);
  private readonly clientGroupsApi = inject(ClientGroupsService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending network delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly groupId = this.route.snapshot.paramMap.get('groupId') ?? '';

  readonly displayedColumns = ['cidr', 'actions'];

  readonly groupName = signal<string | null>(null);
  readonly networks = signal<ClientGroupNetworkDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadGroupName();
    void this.loadPage();
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method confirms, then deletes, one CIDR network from this client group. */
  async deleteNetwork(network: ClientGroupNetworkDetail): Promise<void> {
    if (!network.id) return;

    const confirmed = await this.confirm({
      title: 'Delete this network?',
      message: `This permanently removes '${network.cidr}' from the group. This cannot be undone.`,
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.networksApi.apiClientGroupsGroupIdNetworksIdDelete({
          groupId: this.groupId,
          id: network.id,
        }),
      );
      this.notification.success('Network deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this network.'));
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

  private async loadGroupName(): Promise<void> {
    try {
      const group = await firstValueFrom(
        this.clientGroupsApi.apiClientGroupsIdGet({ id: this.groupId }),
      );
      this.groupName.set(group.name ?? null);
    } catch {
      // A missing group name is only cosmetic here -- the network list load below surfaces its
      // own real error if the owning group itself doesn't exist.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.networksApi.apiClientGroupsGroupIdNetworksGet({
          groupId: this.groupId,
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.networks.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load networks.'));
    } finally {
      this.loading.set(false);
    }
  }
}

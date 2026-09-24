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
import {
  ClientGroupsService,
  ClientsService,
  DnsRewriteDetail,
  DnsRewritesService,
} from '../../../api-client';
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

const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every DNS rewrite this server knows about (`DnsRewriteEntity` -- a
 * lightweight `domain -> target` override consulted early in the query pipeline, before
 * zone/forwarder/recursive resolution), backed by `GET /api/dns-rewrites`'s own real
 * `skip`/`take` pagination, ordered by domain the same way `DnsRewritesController.List` itself
 * orders them. `Target kind` is shown as a plain, read-only badge -- `DnsRewriteDetail.TargetKind`
 * is computed server-side from the stored `target` (an IP address classifies as `A`/`Aaaa`,
 * anything else as `Cname`), not something this UI derives itself, the same convention
 * `ForwarderDetail.Protocol` already establishes.
 *
 * <p>The list also holds the rewrites Safe Search creates for a single client or client group
 * (`clientId`/`clientGroupId`). Those are labelled with the client or group they apply to, and
 * can't be edited, toggled or deleted here: they belong to that client's or group's Safe Search
 * setting, and changing one row by hand would leave Safe Search half applied.</p>
 */
@Component({
  selector: 'app-dns-rewrite-list',
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
  templateUrl: './dns-rewrite-list.component.html',
  styleUrl: './dns-rewrite-list.component.scss',
})
export class DnsRewriteListComponent {
  private readonly rewritesApi = inject(DnsRewritesService);
  private readonly clientsApi = inject(ClientsService);
  private readonly clientGroupsApi = inject(ClientGroupsService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = [
    'domain',
    'target',
    'targetKind',
    'scope',
    'disabled',
    'sync',
    'actions',
  ];

  /** Display names of the clients and groups the current page's scoped rewrites apply to, by id. */
  readonly scopeNames = signal<ReadonlyMap<string, string>>(new Map());

  readonly rewrites = signal<DnsRewriteDetail[]>([]);
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

  /** This method toggles a rewrite's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
  async toggleDisabled(rewrite: DnsRewriteDetail): Promise<void> {
    if (!rewrite.id) return;

    try {
      await firstValueFrom(
        this.rewritesApi.apiDnsRewritesIdPatch({
          id: rewrite.id,
          body: { isDisabled: !rewrite.isDisabled },
        }),
      );
      await this.loadPage();
      this.notification.success(rewrite.isDisabled ? 'Rewrite enabled.' : 'Rewrite disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this rewrite.'));
    }
  }

  /** This method confirms, then deletes, a rewrite. */
  async deleteRewrite(rewrite: DnsRewriteDetail): Promise<void> {
    if (!rewrite.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${rewrite.domain}?`,
      message: 'This permanently removes this DNS rewrite. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.rewritesApi.apiDnsRewritesIdDelete({ id: rewrite.id }));
      this.notification.success('Rewrite deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this rewrite.'));
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
        this.rewritesApi.apiDnsRewritesGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.rewrites.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
      void this.loadScopeNames(page.items ?? []);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load DNS rewrites.'));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * This method looks up the names of the clients and groups this page's scoped rewrites apply
   * to. A lookup that fails (most likely a missing `clients:read` or `clientgroups:read`) just
   * leaves that row labelled "a client" or "a group".
   */
  private async loadScopeNames(rewrites: DnsRewriteDetail[]): Promise<void> {
    const clientIds = new Set(rewrites.map((r) => r.clientId).filter((id): id is string => !!id));
    const groupIds = new Set(
      rewrites.map((r) => r.clientGroupId).filter((id): id is string => !!id),
    );

    const lookups = [
      ...[...clientIds].map(async (id) => {
        const client = await firstValueFrom(this.clientsApi.apiClientsIdGet({ id }));
        return [id, client.displayName || client.addresses?.[0]?.clientIdentifier || ''] as const;
      }),
      ...[...groupIds].map(async (id) => {
        const group = await firstValueFrom(this.clientGroupsApi.apiClientGroupsIdGet({ id }));
        return [id, group.name ?? ''] as const;
      }),
    ];

    const names = new Map<string, string>();
    for (const result of await Promise.allSettled(lookups)) {
      if (result.status === 'fulfilled' && result.value[1]) names.set(...result.value);
    }
    this.scopeNames.set(names);
  }
}

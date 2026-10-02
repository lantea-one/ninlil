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
 * This component is the DNS Rewrites page (`/dns-rewrites`, `dnsrewrites:read`). A rewrite answers
 * a domain with a fixed target, before zones, forwarders or recursion are consulted. The page
 * lists rewrites a page at a time from `GET /api/dns-rewrites`, ordered by domain. The target kind
 * badge (A, AAAA or CNAME) comes from the backend. With `dnsrewrites:write` it offers create, edit,
 * delete and an enable/disable toggle (`PATCH`); without it the toggle is disabled and the rest
 * hidden.
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

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
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

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method flips a rewrite's `isDisabled` with a one-field merge patch (RFC 7396). */
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

  /** This method asks for confirmation, then deletes a rewrite. */
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

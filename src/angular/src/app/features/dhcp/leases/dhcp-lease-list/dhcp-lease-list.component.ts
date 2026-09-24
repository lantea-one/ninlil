import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import {
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  DhcpLeaseDetail,
  DhcpLeasesService,
  DhcpScopeDetail,
  DhcpScopesService,
  LeaseStateEnum,
} from '../../../../api-client';
import { AuthService } from '../../../../core/auth/auth.service';
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

const DEFAULT_PAGE_SIZE = 25;

/**
 * This component shows this server's own live DHCP lease table -- a real, engine-owned artifact,
 * never operator-created, so this page offers no "new"/"edit" route at all, the same posture
 * {@link QueryLogListComponent} already establishes for its own server-written data. `GET
 * /api/dhcp-leases`'s own real `skip`/`take` pagination and optional `scopeId` filter are both
 * pushed all the way down to the server -- narrowing to one scope, or a full "what is my network
 * doing right now" view across every scope, re-issues a fresh search rather than filtering an
 * already-fetched page client-side.
 *
 * <p>This page's one write action -- forcing a stuck or stale lease to release (`POST
 * /api/dhcp-leases/{id}/release`) -- is still not an ordinary resource mutation (this whole
 * resource still has no "new"/"edit" route), so it earns a single per-row action button rather
 * than the usual edit/delete pair, gated on `dhcpleases:manage` (hidden outright, not merely
 * disabled, for a session lacking it, mirroring {@link DhcpServiceListComponent}'s own established
 * "hide, don't merely disable" precedent) and confirmed first via {@link ConfirmDialogComponent},
 * since it immediately and irrevocably disconnects whatever real client currently holds that
 * lease.
 */
@Component({
  selector: 'app-dhcp-lease-list',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    CdkTableModule,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    EmptyStateComponent,
    ConfirmDialogComponent,
  ],
  templateUrl: './dhcp-lease-list.component.html',
  styleUrl: './dhcp-lease-list.component.scss',
})
export class DhcpLeaseListComponent {
  private readonly leasesApi = inject(DhcpLeasesService);
  private readonly scopesApi = inject(DhcpScopesService);
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly authService = inject(AuthService);
  private readonly notification = inject(NotificationService);

  readonly leaseStates = LeaseStateEnum;

  readonly displayedColumns = [
    'scopeName',
    'protocolVersion',
    'clientIdentifier',
    'ipAddress',
    'prefixLength',
    'hostName',
    'state',
    'leaseExpiresAtUtc',
    'actions',
  ];

  /** Whether the signed-in session holds `dhcpleases:manage` -- gates the release button. */
  readonly canManage = this.authService.hasPermission('dhcpleases:manage');

  /** The identifier of whichever single lease currently has a release in flight, if any. */
  readonly releasingLeaseId = signal<string | null>(null);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending release. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly filterForm = this.formBuilder.group({ scopeId: [''] });

  /** Every DHCP scope this page's own scope-narrowing filter can select. */
  readonly scopes = signal<DhcpScopeDetail[]>([]);

  readonly leases = signal<DhcpLeaseDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadScopes();
    void this.loadPage();
  }

  /** This method re-runs the search from the first page, using the filter form's own current scope selection. */
  async search(): Promise<void> {
    this.pageIndex.set(0);
    await this.loadPage();
  }

  /** This method reloads the current page without changing any filter or page position -- a manual "refresh" for this live table. */
  async refresh(): Promise<void> {
    await this.loadPage();
  }

  /** This method reloads the current search's own page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /**
   * This method force-releases a stuck or stale lease, but only after the operator confirms a
   * dialog naming this action's own real, irreversible consequence -- the address becomes
   * available for a different client to claim immediately, and any DNS record this lease
   * auto-registered is removed the same way a real client-initiated release already removes it.
   */
  async release(lease: DhcpLeaseDetail): Promise<void> {
    if (!lease.id) return;
    const leaseId = lease.id;

    const confirmed = await this.confirm({
      title: 'Force-release this lease?',
      message:
        `This immediately disconnects ${lease.clientIdentifier} from ${lease.ipAddress} in ` +
        `scope "${lease.scopeName}" -- the address becomes available for a different client to ` +
        'claim right away, and any DNS record this lease auto-registered is removed. This ' +
        'cannot be undone.',
      confirmLabel: 'Release',
    });
    if (!confirmed) return;

    this.releasingLeaseId.set(leaseId);
    try {
      const released = await firstValueFrom(
        this.leasesApi.apiDhcpLeasesIdReleasePost({ id: leaseId }),
      );
      this.replaceLease(released);
      this.notification.success('Lease released.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not release this lease.'));
    } finally {
      this.releasingLeaseId.set(null);
    }
  }

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
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

  /** This method swaps one updated lease into {@link leases} in place, by identifier. */
  private replaceLease(updated: DhcpLeaseDetail): void {
    this.leases.update((leases) =>
      leases.map((lease) => (lease.id === updated.id ? updated : lease)),
    );
  }

  private async loadScopes(): Promise<void> {
    try {
      const page = await firstValueFrom(this.scopesApi.apiDhcpScopesGet({ skip: 0, take: 200 }));
      this.scopes.set(page.items ?? []);
    } catch {
      // A failed scope lookup only narrows this page's own scope filter dropdown -- the
      // all-scopes lease table below still loads and remains fully usable.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    const scopeId = this.filterForm.controls.scopeId.value || undefined;

    try {
      const page = await firstValueFrom(
        this.leasesApi.apiDhcpLeasesGet({
          scopeId,
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.leases.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load DHCP leases.'));
    } finally {
      this.loading.set(false);
    }
  }
}

import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import {
  BadgeComponent,
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
 * The text every quarantine row's client identifier starts with. A declined address (RFC 2131
 * section 4.3.3) is kept as a lease row keyed `declined:<address>` so no client can be offered it
 * until the quarantine ends (`DhcpDeclinedAddress.KeyPrefix` on the server).
 */
export const DECLINED_KEY_PREFIX = 'declined:';

/** This function reports whether a lease row is the quarantine of a declined address. */
export function isDeclinedLease(lease: DhcpLeaseDetail): boolean {
  return lease.clientIdentifier?.startsWith(DECLINED_KEY_PREFIX) ?? false;
}

/**
 * This component is the DHCP Leases page (`/dhcp-leases`, `dhcpleases:read`). It lists leases a
 * page at a time from `GET /api/dhcp-leases`, optionally filtered by scope; the server applies
 * both the paging and the filter. The DHCP server creates leases, so there is no create or edit.
 *
 * <p>The one write action is force-releasing a stuck or stale lease
 * (`POST /api/dhcp-leases/{id}/release`). Its button shows only with `dhcpleases:manage`, and it
 * asks for confirmation first: the address can be handed to another client at once and any DNS
 * record the lease registered is removed.
 *
 * <p>A declined address shows as a "Declined address" row instead of its raw `declined:` key.
 * The release action does not refuse such a row: it marks it released, so the quarantine ends
 * early and the address can be offered again. The confirmation says so.
 */
@Component({
  selector: 'app-dhcp-lease-list',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    CdkTableModule,
    BadgeComponent,
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

  /** Exposes {@link isDeclinedLease} to the template. */
  readonly isDeclined = isDeclinedLease;

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

  /** Whether the user holds `dhcpleases:manage`; the release button shows only then. */
  readonly canManage = this.authService.hasPermission('dhcpleases:manage');

  /** The id of the lease whose release is in flight, if any. */
  readonly releasingLeaseId = signal<string | null>(null);

  /** Whether the release confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly filterForm = this.formBuilder.group({ scopeId: [''] });

  /** The scopes offered in the scope filter (up to 200). */
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

  /** This method searches again from the first page with the selected scope. */
  async search(): Promise<void> {
    this.pageIndex.set(0);
    await this.loadPage();
  }

  /** This method reloads the current page, for the Refresh button. */
  async refresh(): Promise<void> {
    await this.loadPage();
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /**
   * This method asks for confirmation, then force-releases a lease and puts the returned lease
   * in its row. The backend treats it like a client release: the address is free again at once
   * and any DNS record the lease registered is removed.
   */
  async release(lease: DhcpLeaseDetail): Promise<void> {
    if (!lease.id) return;
    const leaseId = lease.id;

    const declined = isDeclinedLease(lease);
    const confirmed = await this.confirm(
      declined
        ? {
            title: 'End this quarantine early?',
            message:
              `This frees ${lease.ipAddress} in scope "${lease.scopeName}", which a client ` +
              'declined as already in use. The address can be offered to a client right away. ' +
              'This cannot be undone.',
            confirmLabel: 'End quarantine',
          }
        : {
            title: 'Force-release this lease?',
            message:
              `This immediately disconnects ${lease.clientIdentifier} from ${lease.ipAddress} in ` +
              `scope "${lease.scopeName}" -- the address becomes available for a different client to ` +
              'claim right away, and any DNS record this lease auto-registered is removed. This ' +
              'cannot be undone.',
            confirmLabel: 'Release',
          },
    );
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

  /** This method hides the confirmation dialog and settles the pending {@link confirm} promise. */
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

  /** This method replaces one lease in {@link leases}, matched by id. */
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
      // Without the scopes (e.g. no dhcpscopes:read) the filter is empty; the lease list still
      // loads for every scope.
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

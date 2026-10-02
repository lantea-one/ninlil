import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { RouterLink } from '@angular/router';
import {
  ButtonDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  DnsClientStaticAddressMappingDetail,
  DnsClientStaticAddressMappingsService,
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
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../shared/if-permission/has-permission.pipe';

const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the Static Address Mappings page (`/client-static-address-mappings`,
 * `clients:read`). It lists the mappings from address or network to client identifier a page at
 * a time from `GET /api/client-static-address-mappings`. With `clients:write` it offers create,
 * edit, delete and an enable/disable toggle; without it the toggle is disabled and the rest hidden.
 */
@Component({
  selector: 'app-static-address-mapping-list',
  imports: [
    IfPermissionDirective,
    HasPermissionPipe,
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
  templateUrl: './static-address-mapping-list.component.html',
  styleUrl: './static-address-mapping-list.component.scss',
})
export class StaticAddressMappingListComponent {
  private readonly mappingsApi = inject(DnsClientStaticAddressMappingsService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = [
    'cidr',
    'clientIdentifier',
    'protocolVersion',
    'disabled',
    'actions',
  ];

  readonly mappings = signal<DnsClientStaticAddressMappingDetail[]>([]);
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

  /** This method flips a mapping's `isDisabled` with a full `PUT`, since the controller has no `PATCH`. */
  async toggleDisabled(mapping: DnsClientStaticAddressMappingDetail): Promise<void> {
    if (!mapping.id) return;

    try {
      await firstValueFrom(
        this.mappingsApi.apiClientStaticAddressMappingsIdPut({
          id: mapping.id,
          dnsClientStaticAddressMappingWriteRequest: {
            cidr: mapping.cidr,
            clientIdentifier: mapping.clientIdentifier,
            protocolVersion: mapping.protocolVersion,
            isDisabled: !mapping.isDisabled,
            comments: mapping.comments,
          },
        }),
      );
      await this.loadPage();
      this.notification.success(mapping.isDisabled ? 'Mapping enabled.' : 'Mapping disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this mapping.'));
    }
  }

  /** This method asks for confirmation, then deletes a mapping. */
  async deleteMapping(mapping: DnsClientStaticAddressMappingDetail): Promise<void> {
    if (!mapping.id) return;

    const confirmed = await this.confirm({
      title: `Delete this mapping?`,
      message: `This permanently removes the pin from ${mapping.cidr} to ${mapping.clientIdentifier}. Any per-client filtering, schedule, or pause configured for that identifier stops applying to this address immediately. This cannot be undone.`,
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.mappingsApi.apiClientStaticAddressMappingsIdDelete({ id: mapping.id }),
      );
      this.notification.success('Mapping deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this mapping.'));
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
        this.mappingsApi.apiClientStaticAddressMappingsGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.mappings.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load static address mappings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

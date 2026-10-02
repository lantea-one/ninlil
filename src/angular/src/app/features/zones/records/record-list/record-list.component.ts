import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { ActivatedRoute, RouterLink } from '@angular/router';
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
  DnsResourceRecordTypeEnum,
  RecordDetail,
  RecordsService,
  ZonesService,
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
import { IfPermissionDirective } from '../../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../../shared/if-permission/has-permission.pipe';

/** How many records one page of this list shows. The backend's `PaginationQuery` default is 50. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is a zone's records page (`/zones/:zoneId/records`). It lists the zone's records
 * a page at a time through `GET /api/zones/{zoneId}/records` with server-side `skip`/`take`, and
 * shows the zone name from `GET /api/zones/{id}`. Reading needs `zones:read`. With `zones:write`
 * the operator can add and edit records (`RecordFormComponent`), enable or disable one (a
 * one-field `PATCH`) and delete one. `RecordsController.Delete` refuses to delete the SOA with a
 * 409, so the SOA row's delete button is disabled with a tooltip saying why.
 */
@Component({
  selector: 'app-record-list',
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
    EmptyStateComponent,
  ],
  templateUrl: './record-list.component.html',
  styleUrl: './record-list.component.scss',
})
export class RecordListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly recordsApi = inject(RecordsService);
  private readonly zonesApi = inject(ZonesService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending record delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /** The `Soa` record type, for the template's "is this the SOA row?" check. */
  readonly soaType = DnsResourceRecordTypeEnum.Soa;

  readonly zoneId = this.route.snapshot.paramMap.get('zoneId') ?? '';

  readonly displayedColumns = ['name', 'type', 'ttl', 'disabled', 'actions'];

  readonly zoneName = signal<string | null>(null);
  readonly records = signal<RecordDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadZoneName();
    void this.loadPage();
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method flips a record's `isDisabled` flag with a one-field RFC 7396 merge patch. */
  async toggleDisabled(record: RecordDetail): Promise<void> {
    if (!record.id) return;

    try {
      await firstValueFrom(
        this.recordsApi.apiZonesZoneIdRecordsIdPatch({
          zoneId: this.zoneId,
          id: record.id,
          body: { isDisabled: !record.isDisabled },
        }),
      );
      await this.loadPage();
      this.notification.success(record.isDisabled ? 'Record enabled.' : 'Record disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this record.'));
    }
  }

  /** This method asks for confirmation, then deletes a record. It ignores the SOA, which the server will not delete. */
  async deleteRecord(record: RecordDetail): Promise<void> {
    if (!record.id || record.type === this.soaType) return;

    const confirmed = await this.confirm({
      title: `Delete this ${record.type} record?`,
      message: `This permanently removes ${record.name} (${record.type}). This cannot be undone.`,
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.recordsApi.apiZonesZoneIdRecordsIdDelete({ zoneId: this.zoneId, id: record.id }),
      );
      this.notification.success('Record deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this record.'));
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

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private async loadZoneName(): Promise<void> {
    try {
      const zone = await firstValueFrom(this.zonesApi.apiZonesIdGet({ id: this.zoneId }));
      this.zoneName.set(zone.name ?? null);
    } catch {
      // The zone name only feeds the heading; the record list load reports a missing zone.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.recordsApi.apiZonesZoneIdRecordsGet({
          zoneId: this.zoneId,
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.records.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load records.'));
    } finally {
      this.loading.set(false);
    }
  }
}

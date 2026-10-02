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
import { DnssecKeyDetail, DnssecKeysService, ZonesService } from '../../../../api-client';
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
import {
  awaitingParentDsConfirmation,
  canStartRollover,
  lifecycleStateBadgeColor,
} from '../dnssec-key.model';
import { IfPermissionDirective } from '../../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../../shared/if-permission/has-permission.pipe';
import { DnssecKeyRecordsDialogComponent } from '../dnssec-key-records-dialog/dnssec-key-records-dialog.component';
import { DnssecKeyRolloverDialogComponent } from '../dnssec-key-rollover-dialog/dnssec-key-rollover-dialog.component';

/** How many keys one page of this list shows. The backend's `PaginationQuery` default is 50. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is a zone's DNSSEC Keys page (`/zones/:zoneId/dnssec-keys`). It lists the keys a
 * page at a time through `GET /api/zones/{zoneId}/dnssec-keys` and shows each key's role,
 * algorithm, key tag and RFC 6781 lifecycle state as a colored badge. Private key material is never
 * shown: `DnssecKeyDetail` does not carry it, since a DNSSEC private key never has to leave the
 * server (unlike a TSIG secret or a cloud credential).
 *
 * Every user with `zones:read` can open {@link DnssecKeyRecordsDialogComponent} to copy the DNSKEY
 * and DS records for the parent zone. With `zones:write` the operator can also generate a key
 * (`DnssecKeyFormComponent`), retire or reactivate one (a one-field `isRetired` merge patch), start
 * a rollover on an active key ({@link canStartRollover}, through
 * {@link DnssecKeyRolloverDialogComponent}), confirm that a KSK rollover's new DS is live at the
 * parent ({@link awaitingParentDsConfirmation}) and delete a key. All writes go to
 * `/api/zones/{zoneId}/dnssec-keys`, which needs `zones:write`.
 */
@Component({
  selector: 'app-dnssec-key-list',
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
    DnssecKeyRecordsDialogComponent,
    DnssecKeyRolloverDialogComponent,
    EmptyStateComponent,
  ],
  templateUrl: './dnssec-key-list.component.html',
  styleUrl: './dnssec-key-list.component.scss',
})
export class DnssecKeyListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly dnssecKeysApi = inject(DnssecKeysService);
  private readonly zonesApi = inject(ZonesService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending key delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /** The key {@link DnssecKeyRolloverDialogComponent} is open for, if any. */
  readonly rolloverKey = signal<DnssecKeyDetail | null>(null);
  readonly rolloverDialogVisible = signal(false);

  /** The key {@link DnssecKeyRecordsDialogComponent} is open for, if any. */
  readonly recordsKey = signal<DnssecKeyDetail | null>(null);
  readonly recordsDialogVisible = signal(false);

  readonly zoneId = this.route.snapshot.paramMap.get('zoneId') ?? '';

  readonly displayedColumns = [
    'role',
    'algorithm',
    'keyTag',
    'lifecycleState',
    'retired',
    'actions',
  ];

  /** Re-exported for the template, which cannot import a plain module function directly. */
  readonly lifecycleStateBadgeColor = lifecycleStateBadgeColor;

  /** Re-exported for the template, which cannot import a plain module function directly. */
  readonly canStartRollover = canStartRollover;

  /** Re-exported for the template, which cannot import a plain module function directly. */
  readonly awaitingParentDsConfirmation = awaitingParentDsConfirmation;

  readonly zoneName = signal<string | null>(null);
  readonly keys = signal<DnssecKeyDetail[]>([]);
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

  /** This method flips a key's `isRetired` flag with a one-field RFC 7396 merge patch. */
  async toggleRetired(key: DnssecKeyDetail): Promise<void> {
    if (!key.id) return;

    try {
      await firstValueFrom(
        this.dnssecKeysApi.apiZonesZoneIdDnssecKeysIdPatch({
          zoneId: this.zoneId,
          id: key.id,
          body: { isRetired: !key.isRetired },
        }),
      );
      await this.loadPage();
      this.notification.success(key.isRetired ? 'Key reactivated.' : 'Key retired.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this key.'));
    }
  }

  /**
   * This method opens {@link DnssecKeyRolloverDialogComponent} for an active key (see
   * {@link canStartRollover}). The dialog confirms, offers a new algorithm and starts the rollover.
   */
  rollKey(key: DnssecKeyDetail): void {
    if (!key.id) return;

    this.rolloverKey.set(key);
    this.rolloverDialogVisible.set(true);
  }

  /** This method runs once a rollover has started, reloading the page to show the incoming key. */
  async onRolled(replacement: DnssecKeyDetail): Promise<void> {
    this.notification.success(
      `Rollover started -- key tag ${replacement.keyTag ?? '?'} is the incoming replacement.`,
    );
    await this.loadPage();
  }

  /** This method opens {@link DnssecKeyRecordsDialogComponent} for one key. */
  showRecords(key: DnssecKeyDetail): void {
    this.recordsKey.set(key);
    this.recordsDialogVisible.set(true);
  }

  /**
   * This method asks for confirmation, then records through `POST .../confirm-parent-ds-updated`
   * that a KSK rollover's new DS record is published at an external parent zone, which lets the
   * rollover continue. {@link awaitingParentDsConfirmation} says when the action is offered.
   */
  async confirmParentDsUpdated(key: DnssecKeyDetail): Promise<void> {
    if (!key.id) return;

    const confirmed = await this.confirm({
      title: 'Confirm the parent DS record has been updated?',
      message:
        "Only confirm this once the new DS record (shown in the new key's records) is actually " +
        "live at this zone's real, externally-hosted parent -- confirming starts the outgoing " +
        "key's own final, safe removal countdown.",
      confirmLabel: 'Confirm DS updated',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.dnssecKeysApi.apiZonesZoneIdDnssecKeysIdConfirmParentDsUpdatedPost({
          zoneId: this.zoneId,
          id: key.id,
        }),
      );
      this.notification.success('Parent DS update confirmed.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(
        extractErrorMessage(error, 'Could not confirm the parent DS update.'),
      );
    }
  }

  /** This method asks for confirmation, then permanently deletes a DNSSEC key. */
  async deleteKey(key: DnssecKeyDetail): Promise<void> {
    if (!key.id) return;

    const confirmed = await this.confirm({
      title: `Delete this ${key.role} key?`,
      message:
        'Prefer retiring a key over deleting it outright, unless it is already retired and no longer needed for signature validation continuity. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.dnssecKeysApi.apiZonesZoneIdDnssecKeysIdDelete({ zoneId: this.zoneId, id: key.id }),
      );
      this.notification.success('Key deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this key.'));
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
      // The zone name only feeds the heading; the key list load reports a missing zone.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.dnssecKeysApi.apiZonesZoneIdDnssecKeysGet({
          zoneId: this.zoneId,
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.keys.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load DNSSEC keys.'));
    } finally {
      this.loading.set(false);
    }
  }
}

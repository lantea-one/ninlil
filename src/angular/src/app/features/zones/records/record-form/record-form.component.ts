import { NgComponentOutlet } from '@angular/common';
import { Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { DnsResourceRecordTypeEnum, RecordsService, ZonesService } from '../../../../api-client';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { RecordDataFormComponent } from '../record-type-forms/record-type-form.model';
import {
  RECORD_TYPE_FORM_REGISTRY,
  SUPPORTED_RECORD_TYPES,
} from '../record-type-forms/record-type-form.registry';

/**
 * This component is both the "add record" page (`/zones/:zoneId/records/new`) and the "edit
 * record" page (`/zones/:zoneId/records/:recordId/edit`); a `recordId` route parameter selects
 * edit mode. Both routes need `zones:write`. It saves with `POST /api/zones/{zoneId}/records` or
 * `PUT /api/zones/{zoneId}/records/{id}` and returns to the record list.
 *
 * It owns the fields every record type has (`name`, `type`, `timeToLiveSeconds`, `isDisabled`,
 * `excludeFromSync`, `comments`). The type-specific data fields come from the component that
 * {@link RECORD_TYPE_FORM_REGISTRY} names for the selected {@link DnsResourceRecordTypeEnum},
 * mounted with `NgComponentOutlet` and read back through its `componentInstance` as a
 * {@link RecordDataFormComponent}. A new record type therefore needs only a registry entry.
 *
 * `RecordsController.Create` requires a zone's first record to be its SOA, so when the zone is
 * empty the type select is set to SOA and locked. The rule that the SOA cannot be deleted is
 * handled in `RecordListComponent`.
 */
@Component({
  selector: 'app-record-form',
  imports: [
    NgComponentOutlet,
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    SpinnerComponent,
  ],
  templateUrl: './record-form.component.html',
  styleUrl: './record-form.component.scss',
})
export class RecordFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly recordsApi = inject(RecordsService);
  private readonly zonesApi = inject(ZonesService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** The owning zone's id; every route this component serves is nested under a zone. */
  readonly zoneId = this.route.snapshot.paramMap.get('zoneId') ?? '';

  /** The record being edited, or `null` when this instance is adding a brand-new record. */
  private readonly recordId = this.route.snapshot.paramMap.get('recordId');

  /** Whether this instance is editing an existing record rather than creating a new one. */
  readonly isEditMode = this.recordId !== null;

  /** The record types this UI can edit the data of; see `record-type-form.registry.ts`. */
  readonly supportedTypes = SUPPORTED_RECORD_TYPES;

  /** The owning zone's name, shown in the page heading once loaded. */
  readonly zoneName = signal<string | null>(null);

  /** Whether this is the very first record being added to an empty zone (create mode only). */
  readonly isFirstRecordInZone = signal(false);

  /** Whether this instance is editing the zone's SOA record (edit mode only). */
  readonly isEditingSoa = signal(false);

  /** Whether the initial data (the zone, and, in edit mode, the record itself) is still loading. */
  readonly loading = signal(true);

  /** Whether a save request is currently in flight. */
  readonly submitting = signal(false);

  /** The most recent save/load error message to show, or `null` when there isn't one. */
  readonly errorMessage = signal<string | null>(null);

  /** The record data this instance was loaded with, handed to the dynamic type-specific form below. */
  readonly initialRecordData = signal<Record<string, unknown> | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    type: [DnsResourceRecordTypeEnum.A as DnsResourceRecordTypeEnum, [Validators.required]],
    // RecordEntity.TimeToLiveSeconds is a 32-bit unsigned integer.
    timeToLiveSeconds: [3600, [Validators.required, Validators.min(0), Validators.max(4294967295)]],
    isDisabled: [false],
    excludeFromSync: [false],
    comments: [''],
  });

  /**
   * The types the type select offers. The SOA is the zone's one apex SOA (RFC 1034 §4.2.1), so an
   * empty zone and an edit of the SOA offer only SOA, and every other case offers every supported
   * type except SOA; the backend answers 409 when an edit moves the SOA.
   */
  readonly typeOptions = computed(() =>
    this.isFirstRecordInZone() || this.isEditingSoa()
      ? [DnsResourceRecordTypeEnum.Soa]
      : this.supportedTypes.filter((type) => type !== DnsResourceRecordTypeEnum.Soa),
  );

  /** The selected record type, mirrored into a signal since `valueChanges` is not one. */
  readonly selectedType = signal<DnsResourceRecordTypeEnum>(DnsResourceRecordTypeEnum.A);

  /** The type-specific form component to mount for {@link selectedType}, or `null` if unsupported. */
  readonly dataFormComponentType = computed(
    () => RECORD_TYPE_FORM_REGISTRY[this.selectedType()] ?? null,
  );

  /** The `NgComponentOutlet` that hosts the type-specific form, via its `exportAs` template reference. */
  private readonly dataFormOutlet = viewChild<NgComponentOutlet>('dataFormOutlet');

  constructor() {
    this.form.controls.type.valueChanges.subscribe((type) => this.selectedType.set(type));
  }

  ngOnInit(): void {
    if (this.isEditMode) {
      void this.loadExistingRecord();
    } else {
      void this.prepareForCreate();
    }
  }

  /** This method reports whether the mounted type-specific form is valid; `false` if none is mounted. */
  isDataFormValid(): boolean {
    return this.dataFormOutlet()?.componentInstance?.form.valid ?? false;
  }

  /** This method reports whether the selected type has no registered data form. */
  isTypeUnsupported(): boolean {
    return this.dataFormComponentType() === null;
  }

  /** This method saves the record: `POST` when creating, `PUT` (full replace) when editing. */
  async save(): Promise<void> {
    const dataFormComponent = this.dataFormOutlet()?.componentInstance as
      RecordDataFormComponent | undefined;

    if (
      this.form.invalid ||
      !dataFormComponent ||
      dataFormComponent.form.invalid ||
      this.submitting()
    ) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { name, type, timeToLiveSeconds, isDisabled, excludeFromSync, comments } =
      this.form.getRawValue();

    const request = {
      name,
      type,
      timeToLiveSeconds,
      data: dataFormComponent.getData(),
      isDisabled,
      excludeFromSync,
      comments: comments.trim().length > 0 ? comments : null,
    };

    try {
      if (this.isEditMode && this.recordId) {
        await firstValueFrom(
          this.recordsApi.apiZonesZoneIdRecordsIdPut({
            zoneId: this.zoneId,
            id: this.recordId,
            recordWriteRequest: request,
          }),
        );
        this.notification.success('Record saved.');
      } else {
        await firstValueFrom(
          this.recordsApi.apiZonesZoneIdRecordsPost({
            zoneId: this.zoneId,
            recordWriteRequest: request,
          }),
        );
        this.notification.success('Record created.');
      }

      await this.router.navigate(['/zones', this.zoneId, 'records']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this record.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method loads the zone name and, when the zone is empty, locks the type to SOA. */
  private async prepareForCreate(): Promise<void> {
    try {
      const [zone, existingRecords] = await Promise.all([
        firstValueFrom(this.zonesApi.apiZonesIdGet({ id: this.zoneId })),
        firstValueFrom(
          this.recordsApi.apiZonesZoneIdRecordsGet({ zoneId: this.zoneId, skip: 0, take: 1 }),
        ),
      ]);

      this.zoneName.set(zone?.name ?? null);

      if ((existingRecords?.totalCount ?? 0) === 0) {
        this.isFirstRecordInZone.set(true);
        this.form.patchValue({ type: DnsResourceRecordTypeEnum.Soa });
        this.form.controls.type.disable();
        this.selectedType.set(DnsResourceRecordTypeEnum.Soa);
      }
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this zone.'));
    } finally {
      this.loading.set(false);
    }
  }

  /** This method loads the zone name and the record being edited. */
  private async loadExistingRecord(): Promise<void> {
    if (!this.recordId) return;

    try {
      const [zone, record] = await Promise.all([
        firstValueFrom(this.zonesApi.apiZonesIdGet({ id: this.zoneId })),
        firstValueFrom(
          this.recordsApi.apiZonesZoneIdRecordsIdGet({ zoneId: this.zoneId, id: this.recordId }),
        ),
      ]);

      this.zoneName.set(zone?.name ?? null);

      if (record) {
        this.form.patchValue({
          name: record.name ?? '',
          type: record.type ?? DnsResourceRecordTypeEnum.A,
          timeToLiveSeconds: record.timeToLiveSeconds ?? 3600,
          isDisabled: record.isDisabled ?? false,
          excludeFromSync: record.excludeFromSync ?? false,
          comments: record.comments ?? '',
        });
        this.selectedType.set(record.type ?? DnsResourceRecordTypeEnum.A);
        this.initialRecordData.set((record.data as Record<string, unknown>) ?? null);

        // The SOA cannot change type here. RecordsController would accept it and leave the zone
        // with no SOA.
        if (record.type === DnsResourceRecordTypeEnum.Soa) {
          this.isEditingSoa.set(true);
          this.form.controls.type.disable();
        }
      }
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this record.'));
    } finally {
      this.loading.set(false);
    }
  }
}

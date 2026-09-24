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
 * This component is both the "add a record" and "edit a record" page for a zone -- one component
 * for both, distinguished by whether the route supplies a `recordId` (see `app.routes.ts`), the
 * same shape `ZoneFormComponent` uses for zones themselves. This is the framework's own host: it
 * owns every field common to *every* record type (`name`, `type`, `timeToLiveSeconds`,
 * `isDisabled`, `excludeFromSync`, `comments`), and delegates the record type's own
 * type-specific fields to whichever component {@link RECORD_TYPE_FORM_REGISTRY} names for the
 * currently selected {@link DnsResourceRecordTypeEnum} -- mounted dynamically via
 * `NgComponentOutlet`, read back through its `exportAs="ngComponentOutlet"` template reference's
 * own `componentInstance` getter, per {@link RecordDataFormComponent}'s own contract. This is the
 * one piece of the record-editing UI that never needs to change, no matter how many more record
 * types {@link RECORD_TYPE_FORM_REGISTRY} gains entries for.
 *
 * Surfaces two of `RecordsController`'s own real validation rules as genuine UI state, not just a
 * raw backend 400/409 message: a zone's very first record must be its own SOA
 * (`RecordsController.Create`), enforced here by forcing and locking the type selector to `SOA`
 * whenever the target zone currently has zero records; and a zone's own SOA record cannot be
 * deleted directly (`RecordsController.Delete`), enforced in `RecordListComponent` rather than
 * here, since that rule only matters for the delete action.
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

  /** The owning zone's own identifier, always present -- every route this component serves is nested under a zone. */
  readonly zoneId = this.route.snapshot.paramMap.get('zoneId') ?? '';

  /** The record being edited, or `null` when this instance is adding a brand-new record. */
  private readonly recordId = this.route.snapshot.paramMap.get('recordId');

  /** Whether this instance is editing an existing record rather than creating a new one. */
  readonly isEditMode = this.recordId !== null;

  /** The record types this UI can edit the data of -- see the registry's own doc comment. */
  readonly supportedTypes = SUPPORTED_RECORD_TYPES;

  /** The owning zone's own name, shown in the page heading once loaded. */
  readonly zoneName = signal<string | null>(null);

  /** Whether this is the very first record being added to an empty zone (create mode only). */
  readonly isFirstRecordInZone = signal(false);

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
    // 4294967295 is the real backend bound: RecordEntity.TimeToLiveSeconds is a 32-bit unsigned
    // integer, and a value outside that range is rejected once it reaches the REST layer.
    timeToLiveSeconds: [3600, [Validators.required, Validators.min(0), Validators.max(4294967295)]],
    isDisabled: [false],
    excludeFromSync: [false],
    comments: [''],
  });

  /** The record type currently selected, tracked as a plain field since `valueChanges` isn't a signal. */
  readonly selectedType = signal<DnsResourceRecordTypeEnum>(DnsResourceRecordTypeEnum.A);

  /** The type-specific form component to mount for {@link selectedType}, or `null` if unsupported. */
  readonly dataFormComponentType = computed(
    () => RECORD_TYPE_FORM_REGISTRY[this.selectedType()] ?? null,
  );

  /** A template reference onto the dynamically mounted type-specific form, per `NgComponentOutlet`'s own `exportAs`. */
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

  /** This method reads the dynamically mounted type-specific form's own validity, if one is mounted. */
  isDataFormValid(): boolean {
    return this.dataFormOutlet()?.componentInstance?.form.valid ?? false;
  }

  /** This method reads whether this record's own type has no registered data-entry form at all. */
  isTypeUnsupported(): boolean {
    return this.dataFormComponentType() === null;
  }

  /** This method saves the record -- `POST` when creating, `PUT` (full replace) when editing. */
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

  /** This method loads the owning zone's own name and, in create mode, whether it's currently empty. */
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

  /** This method loads the existing record's own fields and data, for edit mode. */
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

        // A zone's own SOA record cannot ever change into a different type through this form --
        // RecordsController itself would accept it, but doing so would leave the zone with no
        // SOA at all until a new one is separately created, which this UI has no reason to allow.
        if (record.type === DnsResourceRecordTypeEnum.Soa) {
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

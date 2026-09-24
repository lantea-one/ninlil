import { Component, OnInit, computed, inject, signal } from '@angular/core';
import {
  FormsModule,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
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
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import {
  CloudProviderCredentialDetail,
  CloudProviderCredentialsService,
  DnssecAlgorithmEnum,
  DnssecDenialOfExistenceModeEnum,
  TsigKeyDetail,
  TsigKeysService,
  ZoneCaaReconciliationResponse,
  ZoneDetail,
  ZonePtrReconciliationResponse,
  ZoneTransferTransportEnum,
  ZoneTypeEnum,
  ZonesService,
} from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  isCloudZoneType,
  isLocallyOriginatedZoneType,
  requiresForwarderAddresses,
  requiresPrimaryServerAddresses,
  ZONE_TYPE_OPTIONS,
} from '../zone.model';

/** This function splits a textarea's own one-address-per-line text into a trimmed, non-empty-only array. */
function splitAddressLines(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/**
 * This sentinel value drives the TSIG key select's own "enter a custom name" option -- distinct
 * from any real key name (a real {@link TsigKeyDetail.name} can never equal it, since a TSIG key
 * name is a DNS name and this string is not a valid one), so it is always safe to compare a
 * selection against it directly rather than tracking a separate boolean flag.
 */
const CUSTOM_TSIG_KEY_OPTION = '__custom__';

/**
 * This sentinel value drives the cloud provider credential select's own "enter a custom ID" option
 * -- the same shape as {@link CUSTOM_TSIG_KEY_OPTION}, for the same reason: a real
 * {@link CloudProviderCredentialDetail.id} is a GUID and can never equal this string.
 */
const CUSTOM_CLOUD_CREDENTIAL_OPTION = '__custom__';

/**
 * This component is both the "create a zone" and "edit a zone" page -- one component for both,
 * distinguished by whether the route supplies a `zoneId` (see `app.routes.ts`). A zone's own
 * records (starting with its SOA) are managed separately, through `RecordListComponent`/
 * `RecordFormComponent`; this page only ever touches `ZoneEntity`'s own metadata.
 *
 * The form's own type-specific sections (transfer source, forwarding, cloud sync, DNSSEC signing,
 * dynamic updates, CAA/PTR automation) show and hide based on the currently selected
 * {@link ZoneTypeEnum}, per `zone.model.ts`'s own helpers -- a single form with conditional
 * sections, by deliberate design, rather than a separate wizard per zone
 * type. Every field stays present in the underlying `FormGroup` regardless of visibility (an
 * operator switching zone type and back does not lose what they already typed), and every
 * conditional section's own controls are sent to the backend regardless of whether that section
 * is currently shown -- `ZonesController.ApplyWrite` itself simply ignores a field that doesn't
 * apply to the chosen type, the same "don't duplicate the backend's own validation client-side"
 * stance this feature takes throughout.
 */
@Component({
  selector: 'app-zone-form',
  imports: [
    ReactiveFormsModule,
    FormsModule,
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
    FormTextDirective,
    SpinnerComponent,
    IconDirective,
  ],
  templateUrl: './zone-form.component.html',
  styleUrl: './zone-form.component.scss',
})
export class ZoneFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly zonesApi = inject(ZonesService);
  private readonly tsigKeysApi = inject(TsigKeysService);
  private readonly cloudCredentialsApi = inject(CloudProviderCredentialsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** The sentinel the TSIG key select's own "enter a custom name" option carries as its value. */
  readonly customTsigKeyOption = CUSTOM_TSIG_KEY_OPTION;

  /** The sentinel the cloud provider credential select's own "enter a custom ID" option carries as its value. */
  readonly customCloudCredentialOption = CUSTOM_CLOUD_CREDENTIAL_OPTION;

  /**
   * Every TSIG key this session can currently see, offered as the TSIG key select's own known-
   * value options -- fetched once, up front, alongside the zone itself in edit mode (matching
   * `ClientGroupListAssignmentFormComponent`'s own `skip: 0, take: 500` precedent for "list
   * everything this select could plausibly need," not paged incrementally).
   */
  readonly tsigKeys = signal<TsigKeyDetail[]>([]);

  /**
   * Every cloud provider credential this session can currently see, offered as the cloud
   * credential select's own known-value options -- fetched the same way, and for the same reason,
   * as {@link tsigKeys}.
   */
  readonly cloudCredentials = signal<CloudProviderCredentialDetail[]>([]);

  private readonly zoneId = this.route.snapshot.paramMap.get('zoneId');

  /** Whether this instance is editing an existing zone rather than creating a new one. */
  readonly isEditMode = this.zoneId !== null;

  readonly zoneTypeOptions = ZONE_TYPE_OPTIONS;
  readonly dnssecAlgorithms = Object.values(DnssecAlgorithmEnum);
  readonly denialOfExistenceModes = Object.values(DnssecDenialOfExistenceModeEnum);
  readonly transferTransportOptions = Object.values(ZoneTransferTransportEnum);

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    type: [ZoneTypeEnum.Primary as ZoneTypeEnum, [Validators.required]],
    isDisabled: [false],
    comments: [''],
    primaryServerAddressesText: [''],
    // Only meaningful for a Secondary/Stub/SecondaryCatalog zone (see showTransferSection) --
    // which wire transport SecondaryZoneRefreshService pulls this zone's own AXFR/IXFR over.
    // Defaults to Plain, matching ZoneEntity.TransferTransport's own compiled-in default.
    transferTransport: [ZoneTransferTransportEnum.Plain as ZoneTransferTransportEnum],
    forwarderAddressesText: [''],
    notifyAddressesText: [''],
    tsigKeyName: [''],
    // A UI-only control, never sent to the backend directly (see `save()`, which builds its
    // request from named fields, not a spread of `getRawValue()`) -- drives which shape the TSIG
    // key field takes: '' (none selected), a real key name (that key selected from the list), or
    // `CUSTOM_TSIG_KEY_OPTION` (the operator is typing a name not in the list, into
    // `tsigKeyName` directly).
    tsigKeySelection: [''],
    dynamicUpdateEnabled: [false],
    isDnssecSigned: [false],
    dnssecAlgorithm: [DnssecAlgorithmEnum.Ecdsap256Sha256 as DnssecAlgorithmEnum],
    denialOfExistenceMode: [
      DnssecDenialOfExistenceModeEnum.Nsec3 as DnssecDenialOfExistenceModeEnum,
    ],
    // 65535 is the real backend bound: ZoneEntity.Nsec3Iterations is a 16-bit unsigned integer.
    nsec3Iterations: [10, [Validators.min(0), Validators.max(65535)]],
    nsec3SaltHex: [''],
    cloudProviderCredentialId: [''],
    // A UI-only control, the same shape as `tsigKeySelection` above -- drives which shape the
    // cloud provider credential field takes: '' (none selected), a real credential's own ID
    // (that credential selected from the list), or `CUSTOM_CLOUD_CREDENTIAL_OPTION` (the operator
    // is typing/pasting an ID not in the list, into `cloudProviderCredentialId` directly).
    cloudProviderCredentialSelection: [''],
    cloudProviderZoneId: [''],
    // Allows an optional "d." days prefix (e.g. "1.00:05:00") alongside the plain "hh:mm:ss"
    // form -- ZoneEntity.CloudSyncInterval is a real .NET TimeSpan, serialized/parsed in the
    // standard "c" format, which is not limited to under-24-hour values the way the previous,
    // narrower pattern here implied.
    cloudSyncIntervalText: ['00:05:00', [Validators.pattern(/^(\d{1,2}\.)?\d{1,2}:\d{2}:\d{2}$/)]],
    autoManageCaaRecords: [null as boolean | null],
    caaIssuerDomain: [''],
    autoManagePtrRecords: [null as boolean | null],
    // Both null (inherit PtrSyncOptions' own global default) unless the operator sets an
    // explicit override -- the same "24 inclusive through 30" / "multiple of 4, 16 through 124"
    // bounds ZonesController validates server-side are enforced here too (Validators.min/max),
    // so an obviously-invalid value is rejected before a round trip, not just after.
    ptrIpv4PrefixLength: [null as number | null, [Validators.min(24), Validators.max(30)]],
    ptrIpv6PrefixLength: [null as number | null, [Validators.min(16), Validators.max(124)]],
  });

  /** The zone type currently selected, tracked as a plain signal since `valueChanges` isn't one. */
  readonly selectedType = signal<ZoneTypeEnum>(ZoneTypeEnum.Primary);

  readonly showTransferSection = computed(() =>
    requiresPrimaryServerAddresses(this.selectedType()),
  );
  readonly showForwardingSection = computed(() => requiresForwarderAddresses(this.selectedType()));
  readonly showCloudSection = computed(() => isCloudZoneType(this.selectedType()));
  readonly showOriginationSections = computed(() =>
    isLocallyOriginatedZoneType(this.selectedType()),
  );

  /**
   * The set of collapsible section keys currently collapsed -- every real section (see the
   * template's own `<section class="form-section">` blocks) starts open (absent from this set),
   * so switching from one long unbroken scroll to real, labeled sections never hides any field an
   * operator hasn't deliberately chosen to collapse. Collapsing a section is purely a display
   * convenience -- every underlying form control stays exactly as populated regardless of whether
   * its own section is currently open, matching this form's own existing "every field stays
   * present in the FormGroup regardless of visibility" convention for type-conditional sections.
   */
  private readonly collapsedSections = signal<ReadonlySet<string>>(new Set());

  /** This method reports whether the named section is currently expanded. */
  isSectionOpen(section: string): boolean {
    return !this.collapsedSections().has(section);
  }

  /** This method toggles one named section's own collapsed/expanded display state. */
  toggleSection(section: string): void {
    this.collapsedSections.update((collapsed) => {
      const next = new Set(collapsed);
      if (next.has(section)) next.delete(section);
      else next.add(section);
      return next;
    });
  }

  /** The TSIG key select's own current selection, tracked as a plain signal for the same reason {@link selectedType} is. */
  readonly tsigKeySelection = signal<string>('');
  readonly showCustomTsigKeyField = computed(
    () => this.tsigKeySelection() === CUSTOM_TSIG_KEY_OPTION,
  );

  /** The cloud provider credential select's own current selection, tracked the same way {@link tsigKeySelection} is. */
  readonly cloudCredentialSelection = signal<string>('');
  readonly showCustomCloudCredentialField = computed(
    () => this.cloudCredentialSelection() === CUSTOM_CLOUD_CREDENTIAL_OPTION,
  );

  /**
   * This section shows (join/leave a catalog, `POST`/`DELETE /api/zones/{id}/catalog-membership`)
   * only for an already-saved Primary/CloudPrimary zone -- a brand-new, not-yet-created zone has
   * no `zoneId` for either endpoint to act on yet, and RFC 9432 membership only ever applies to a
   * zone this server itself originates content for (`isLocallyOriginatedZoneType`, the same
   * eligibility `CatalogZoneMembershipService.IsEligibleMemberZoneType` enforces server-side).
   */
  readonly showCatalogMembershipSection = computed(
    () => this.isEditMode && isLocallyOriginatedZoneType(this.selectedType()),
  );

  /**
   * This section shows (`GET /api/zones/{id}/catalog-members`, read-only) for an already-saved
   * Catalog or SecondaryCatalog zone -- the actual current member list an operator can otherwise
   * only infer from the zone's own PTR records.
   */
  readonly showCatalogMembersListSection = computed(
    () =>
      this.isEditMode &&
      (this.selectedType() === ZoneTypeEnum.Catalog ||
        this.selectedType() === ZoneTypeEnum.SecondaryCatalog),
  );

  /** Every Catalog zone this session can currently see, offered as the "join a catalog" select's own options. */
  readonly catalogOptions = signal<ZoneDetail[]>([]);

  /** The identifier of the catalog zone this (already-saved) zone currently belongs to, or `null` if none. */
  readonly memberOfCatalogZoneId = signal<string | null>(null);

  /** The catalog-join select's own current selection, separate from {@link memberOfCatalogZoneId} so choosing a different catalog before pressing "Join" doesn't change what's actually saved. */
  readonly selectedCatalogZoneId = signal<string>('');

  /** The current catalog zone's own currently-enumerated member zone names, for {@link showCatalogMembersListSection}. */
  readonly catalogMembers = signal<string[]>([]);

  /** Whether a join/leave-catalog call is currently in flight. */
  readonly catalogActionPending = signal(false);

  /** The most recent join/leave-catalog failure, if any -- kept separate from {@link errorMessage}, which covers only the main zone form itself. */
  readonly catalogActionError = signal<string | null>(null);

  /**
   * This section (an on-demand "reconcile now" pair of buttons, `POST /api/zones/{id}/reconcile-caa`/
   * `-ptr`) shows only for an already-saved zone eligible for automatic record management -- the
   * same condition {@link showOriginationSections} already gates the surrounding section on, since
   * neither reconciliation service does anything for a zone type it does not own the content of.
   */
  readonly showReconcileNowSection = computed(
    () => this.isEditMode && this.showOriginationSections(),
  );

  /** Whether a CAA reconciliation call is currently in flight. */
  readonly reconcileCaaPending = signal(false);

  /** Whether a PTR reconciliation call is currently in flight. */
  readonly reconcilePtrPending = signal(false);

  /** The most recent CAA reconciliation pass's own result, rendered as a plain summary line. */
  readonly reconcileCaaResult = signal<string | null>(null);

  /** The most recent PTR reconciliation pass's own result, rendered as a plain summary line. */
  readonly reconcilePtrResult = signal<string | null>(null);

  /** The currently-joined catalog's own display name, falling back to its raw identifier if this session can't otherwise see it in {@link catalogOptions}. */
  readonly memberOfCatalogZoneName = computed(() => {
    const id = this.memberOfCatalogZoneId();
    if (!id) return null;
    return this.catalogOptions().find((zone) => zone.id === id)?.name ?? id;
  });

  constructor() {
    this.form.controls.type.valueChanges.subscribe((type) => this.selectedType.set(type));
    // Selecting a real key name from the list writes it straight into `tsigKeyName` (the control
    // actually submitted); selecting "none" clears it; selecting the custom sentinel leaves
    // `tsigKeyName` alone so the operator's own free-text edits underneath aren't clobbered.
    this.form.controls.tsigKeySelection.valueChanges.subscribe((selection) => {
      this.tsigKeySelection.set(selection);
      if (selection === CUSTOM_TSIG_KEY_OPTION) return;
      this.form.controls.tsigKeyName.setValue(selection);
    });
    // Same shape as the TSIG key subscription immediately above, this time driving
    // `cloudProviderCredentialId` from `cloudProviderCredentialSelection`.
    this.form.controls.cloudProviderCredentialSelection.valueChanges.subscribe((selection) => {
      this.cloudCredentialSelection.set(selection);
      if (selection === CUSTOM_CLOUD_CREDENTIAL_OPTION) return;
      this.form.controls.cloudProviderCredentialId.setValue(selection);
    });
  }

  ngOnInit(): void {
    void this.initialize();
  }

  /**
   * This method loads every prerequisite this form needs before it can render meaningfully: the
   * live TSIG key list and cloud provider credential list always, and the existing zone itself in
   * edit mode -- all fetched concurrently (not one after another) since none depends on another's
   * result, only on all three being in hand before `applyExistingZone` can compute the right
   * initial TSIG key and cloud credential selections.
   */
  private async initialize(): Promise<void> {
    try {
      const tsigKeysRequest = firstValueFrom(
        this.tsigKeysApi.apiTsigKeysGet({ skip: 0, take: 500 }),
      );
      const cloudCredentialsRequest = firstValueFrom(
        this.cloudCredentialsApi.apiCloudProviderCredentialsGet({ skip: 0, take: 500 }),
      );

      if (this.isEditMode && this.zoneId) {
        const [tsigKeysResult, cloudCredentialsResult, zone] = await Promise.all([
          tsigKeysRequest,
          cloudCredentialsRequest,
          firstValueFrom(this.zonesApi.apiZonesIdGet({ id: this.zoneId })),
        ]);
        this.tsigKeys.set(tsigKeysResult.items ?? []);
        this.cloudCredentials.set(cloudCredentialsResult.items ?? []);
        this.applyExistingZone(zone);
        // A failure loading catalog membership context is a real but secondary concern -- it
        // must never block the main zone form itself from rendering, so it gets its own signal
        // and its own try/catch rather than sharing this method's own errorMessage.
        void this.loadCatalogMembershipContextAsync(zone);
      } else {
        const [tsigKeysResult, cloudCredentialsResult] = await Promise.all([
          tsigKeysRequest,
          cloudCredentialsRequest,
        ]);
        this.tsigKeys.set(tsigKeysResult.items ?? []);
        this.cloudCredentials.set(cloudCredentialsResult.items ?? []);
      }
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(
          error,
          this.isEditMode
            ? 'Could not load this zone.'
            : 'Could not load the TSIG key / cloud credential lists.',
        ),
      );
    } finally {
      this.loading.set(false);
    }
  }

  /** This method creates (`POST`) or fully replaces (`PUT`) the zone, per {@link isEditMode}. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const request = {
      name: value.name,
      type: value.type,
      isDisabled: value.isDisabled,
      comments: value.comments.trim().length > 0 ? value.comments : null,
      primaryServerAddresses: splitAddressLines(value.primaryServerAddressesText),
      transferTransport: value.transferTransport,
      forwarderAddresses: splitAddressLines(value.forwarderAddressesText),
      notifyAddresses: splitAddressLines(value.notifyAddressesText),
      tsigKeyName: value.tsigKeyName.trim().length > 0 ? value.tsigKeyName : null,
      dynamicUpdateEnabled: value.dynamicUpdateEnabled,
      isDnssecSigned: value.isDnssecSigned,
      // Unlike this request's own string/GUID fields, ZoneWriteRequest.DnssecAlgorithm has no
      // null-clears-it convention of its own -- it's simply omitted (`undefined`) when the zone
      // isn't signed, matching ZoneWriteRequest's own optional-with-no-default C# parameter.
      dnssecAlgorithm: value.isDnssecSigned ? value.dnssecAlgorithm : undefined,
      denialOfExistenceMode: value.denialOfExistenceMode,
      nsec3Iterations: value.nsec3Iterations,
      nsec3SaltHex: value.nsec3SaltHex.trim().length > 0 ? value.nsec3SaltHex : null,
      cloudProviderCredentialId:
        value.cloudProviderCredentialId.trim().length > 0 ? value.cloudProviderCredentialId : null,
      cloudProviderZoneId:
        value.cloudProviderZoneId.trim().length > 0 ? value.cloudProviderZoneId : null,
      cloudSyncInterval: value.cloudSyncIntervalText,
      autoManageCaaRecords: value.autoManageCaaRecords,
      caaIssuerDomain: value.caaIssuerDomain.trim().length > 0 ? value.caaIssuerDomain : null,
      autoManagePtrRecords: value.autoManagePtrRecords,
      ptrIpv4PrefixLength: value.ptrIpv4PrefixLength,
      ptrIpv6PrefixLength: value.ptrIpv6PrefixLength,
    };

    try {
      if (this.isEditMode && this.zoneId) {
        await firstValueFrom(
          this.zonesApi.apiZonesIdPut({ id: this.zoneId, zoneWriteRequest: request }),
        );
        this.notification.success('Zone saved.');
        await this.router.navigate(['/zones']);
      } else {
        const created = await firstValueFrom(
          this.zonesApi.apiZonesPost({ zoneWriteRequest: request }),
        );
        this.notification.success('Zone created. Add its own SOA record to finish setting it up.');
        await this.router.navigate(['/zones', created.id, 'records']);
      }
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this zone.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * This method patches the form with an already-fetched zone's own values -- split out from
   * {@link initialize} so that method's single `try`/`catch` covers both this and the TSIG key /
   * cloud credential fetches identically, rather than duplicating error handling.
   */
  private applyExistingZone(zone: ZoneDetail): void {
    const tsigKeyName = zone.tsigKeyName ?? '';
    // A key name that matches one this session can currently see selects it from the list; a
    // non-empty name that doesn't (the key was since renamed or deleted, or this session simply
    // can't see it) falls back to the custom-entry shape instead of silently discarding it --
    // either way, `tsigKeyName` itself always ends up holding the zone's real, unmodified value.
    const tsigKeySelection =
      tsigKeyName.length === 0
        ? ''
        : this.tsigKeys().some((key) => key.name === tsigKeyName)
          ? tsigKeyName
          : CUSTOM_TSIG_KEY_OPTION;

    const cloudProviderCredentialId = zone.cloudProviderCredentialId ?? '';
    // Same fallback shape as `tsigKeySelection` immediately above, matched by ID rather than
    // name (a cloud provider credential's own `id`, not a human-chosen name, is what
    // `ZoneEntity.CloudProviderCredentialId` actually stores).
    const cloudProviderCredentialSelection =
      cloudProviderCredentialId.length === 0
        ? ''
        : this.cloudCredentials().some((credential) => credential.id === cloudProviderCredentialId)
          ? cloudProviderCredentialId
          : CUSTOM_CLOUD_CREDENTIAL_OPTION;

    this.form.patchValue({
      name: zone.name ?? '',
      type: zone.type ?? ZoneTypeEnum.Primary,
      isDisabled: zone.isDisabled ?? false,
      comments: zone.comments ?? '',
      primaryServerAddressesText: (zone.primaryServerAddresses ?? []).join('\n'),
      transferTransport: zone.transferTransport ?? ZoneTransferTransportEnum.Plain,
      forwarderAddressesText: (zone.forwarderAddresses ?? []).join('\n'),
      notifyAddressesText: (zone.notifyAddresses ?? []).join('\n'),
      tsigKeyName,
      tsigKeySelection,
      dynamicUpdateEnabled: zone.dynamicUpdateEnabled ?? false,
      isDnssecSigned: zone.isDnssecSigned ?? false,
      dnssecAlgorithm: zone.dnssecAlgorithm ?? DnssecAlgorithmEnum.Ecdsap256Sha256,
      denialOfExistenceMode: zone.denialOfExistenceMode ?? DnssecDenialOfExistenceModeEnum.Nsec3,
      nsec3Iterations: zone.nsec3Iterations ?? 10,
      nsec3SaltHex: zone.nsec3SaltHex ?? '',
      cloudProviderCredentialId,
      cloudProviderCredentialSelection,
      cloudProviderZoneId: zone.cloudProviderZoneId ?? '',
      cloudSyncIntervalText: zone.cloudSyncInterval ?? '00:05:00',
      autoManageCaaRecords: zone.autoManageCaaRecords ?? null,
      caaIssuerDomain: zone.caaIssuerDomain ?? '',
      autoManagePtrRecords: zone.autoManagePtrRecords ?? null,
      ptrIpv4PrefixLength: zone.ptrIpv4PrefixLength ?? null,
      ptrIpv6PrefixLength: zone.ptrIpv6PrefixLength ?? null,
    });
    this.selectedType.set(zone.type ?? ZoneTypeEnum.Primary);
    this.tsigKeySelection.set(tsigKeySelection);
    this.cloudCredentialSelection.set(cloudProviderCredentialSelection);
  }

  /**
   * This method loads whatever this zone's own RFC 9432 catalog-membership section needs: for a
   * locally-originated zone, the current membership (already on {@link ZoneDetail} itself) plus
   * the live list of Catalog zones this session can join it to; for a Catalog/SecondaryCatalog
   * zone, its own current member list.
   */
  private async loadCatalogMembershipContextAsync(zone: ZoneDetail): Promise<void> {
    if (!this.zoneId) return;

    try {
      if (isLocallyOriginatedZoneType(zone.type ?? ZoneTypeEnum.Primary)) {
        this.memberOfCatalogZoneId.set(zone.memberOfCatalogZoneId ?? null);
        this.selectedCatalogZoneId.set(zone.memberOfCatalogZoneId ?? '');

        const allZones = await firstValueFrom(this.zonesApi.apiZonesGet({ skip: 0, take: 500 }));
        this.catalogOptions.set(
          (allZones.items ?? []).filter((candidate) => candidate.type === ZoneTypeEnum.Catalog),
        );
      } else if (
        zone.type === ZoneTypeEnum.Catalog ||
        zone.type === ZoneTypeEnum.SecondaryCatalog
      ) {
        const members = await firstValueFrom(
          this.zonesApi.apiZonesIdCatalogMembersGet({ id: this.zoneId }),
        );
        this.catalogMembers.set(members.memberZoneNames ?? []);
      }
    } catch (error) {
      this.catalogActionError.set(
        extractErrorMessage(error, 'Could not load this zone’s catalog membership.'),
      );
    }
  }

  /** This method joins this (already-saved) zone to whichever catalog {@link selectedCatalogZoneId} currently names. */
  async joinCatalog(): Promise<void> {
    const catalogZoneId = this.selectedCatalogZoneId();
    if (!this.zoneId || !catalogZoneId || this.catalogActionPending()) return;

    this.catalogActionPending.set(true);
    this.catalogActionError.set(null);

    try {
      await firstValueFrom(
        this.zonesApi.apiZonesIdCatalogMembershipPost({
          id: this.zoneId,
          zoneCatalogMembershipRequest: { catalogZoneId },
        }),
      );
      this.memberOfCatalogZoneId.set(catalogZoneId);
      this.notification.success('Joined the catalog zone.');
    } catch (error) {
      this.catalogActionError.set(extractErrorMessage(error, 'Could not join the catalog zone.'));
    } finally {
      this.catalogActionPending.set(false);
    }
  }

  /** This method removes this (already-saved) zone's own current catalog membership, if it has one. */
  async leaveCatalog(): Promise<void> {
    if (!this.zoneId || this.catalogActionPending()) return;

    this.catalogActionPending.set(true);
    this.catalogActionError.set(null);

    try {
      await firstValueFrom(this.zonesApi.apiZonesIdCatalogMembershipDelete({ id: this.zoneId }));
      this.memberOfCatalogZoneId.set(null);
      this.selectedCatalogZoneId.set('');
      this.notification.success('Left the catalog zone.');
    } catch (error) {
      this.catalogActionError.set(extractErrorMessage(error, 'Could not leave the catalog zone.'));
    } finally {
      this.catalogActionPending.set(false);
    }
  }

  /**
   * This method triggers one real, synchronous CAA reconciliation pass for this (already-saved)
   * zone right now (`POST /api/zones/{id}/reconcile-caa`), rendering the backend's own real
   * counts/skip-reason as a plain summary line -- the same server-side logic the automatic,
   * event-driven path already runs, just triggered on demand instead of waiting for the next
   * zone content change.
   */
  async reconcileCaaNow(): Promise<void> {
    if (!this.zoneId || this.reconcileCaaPending()) return;

    this.reconcileCaaPending.set(true);
    this.reconcileCaaResult.set(null);

    try {
      const result: ZoneCaaReconciliationResponse = await firstValueFrom(
        this.zonesApi.apiZonesIdReconcileCaaPost({ id: this.zoneId }),
      );
      this.reconcileCaaResult.set(this.summarizeCaaReconciliation(result));
    } catch (error) {
      this.reconcileCaaResult.set(
        extractErrorMessage(error, 'Could not reconcile CAA records for this zone.'),
      );
    } finally {
      this.reconcileCaaPending.set(false);
    }
  }

  /**
   * This method triggers one real, synchronous PTR/reverse-zone sync reconciliation pass for this
   * (already-saved) zone right now (`POST /api/zones/{id}/reconcile-ptr`), the same on-demand
   * convenience {@link reconcileCaaNow} offers for CAA reconciliation -- including RFC 2317
   * classless-delegation glue when this zone's own IPv4 prefix length (or the global default)
   * calls for it.
   */
  async reconcilePtrNow(): Promise<void> {
    if (!this.zoneId || this.reconcilePtrPending()) return;

    this.reconcilePtrPending.set(true);
    this.reconcilePtrResult.set(null);

    try {
      const result: ZonePtrReconciliationResponse = await firstValueFrom(
        this.zonesApi.apiZonesIdReconcilePtrPost({ id: this.zoneId }),
      );
      this.reconcilePtrResult.set(this.summarizePtrReconciliation(result));
    } catch (error) {
      this.reconcilePtrResult.set(
        extractErrorMessage(error, 'Could not sync PTR records for this zone.'),
      );
    } finally {
      this.reconcilePtrPending.set(false);
    }
  }

  /** This method renders a CAA reconciliation result as one short, human-readable summary line. */
  private summarizeCaaReconciliation(result: ZoneCaaReconciliationResponse): string {
    if (result.skipped) return result.skipReason ?? 'Nothing to reconcile.';

    const added = result.recordsAdded ?? 0;
    const removed = result.recordsRemoved ?? 0;
    if (added === 0 && removed === 0) return 'Already up to date -- no changes needed.';
    return `Reconciled: ${added} record(s) added, ${removed} removed.`;
  }

  /** This method renders a PTR reconciliation result as one short, human-readable summary line. */
  private summarizePtrReconciliation(result: ZonePtrReconciliationResponse): string {
    if (result.skipped) return result.skipReason ?? 'Nothing to reconcile.';

    const added = result.recordsAdded ?? 0;
    const removed = result.recordsRemoved ?? 0;
    const zonesTouched = result.reverseZonesTouched ?? 0;
    if (added === 0 && removed === 0) return 'Already up to date -- no changes needed.';
    return `Reconciled: ${added} record(s) added, ${removed} removed, across ${zonesTouched} reverse zone(s).`;
  }
}

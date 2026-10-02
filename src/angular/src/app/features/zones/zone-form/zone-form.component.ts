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
import { AuthService } from '../../../core/auth/auth.service';

/** This function splits one-address-per-line textarea text into trimmed, non-empty entries. */
function splitAddressLines(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/**
 * This sentinel is the value of the TSIG key select's "Enter a custom name" option. A TSIG key name
 * is a DNS name and this string is not a valid one, so it never collides with a
 * {@link TsigKeyDetail.name} and the selection can be compared with it directly.
 */
const CUSTOM_TSIG_KEY_OPTION = '__custom__';

/**
 * This sentinel is the value of the cloud credential select's "Enter a custom ID" option. A
 * {@link CloudProviderCredentialDetail.id} is a GUID, so it never equals this string.
 */
const CUSTOM_CLOUD_CREDENTIAL_OPTION = '__custom__';

/**
 * This component is both the "New zone" page (`/zones/new`) and the "Edit zone" page
 * (`/zones/:zoneId/edit`); a `zoneId` route parameter selects edit mode. Both routes need
 * `zones:write`. It saves with `POST /api/zones` or `PUT /api/zones/{id}` and only edits zone
 * settings; records, including the SOA, are managed by `RecordListComponent` and
 * `RecordFormComponent`, and a new zone opens on its records page.
 *
 * The type-specific sections (transfer source, forwarding, cloud sync, DNSSEC signing, dynamic
 * updates, automatic CAA/PTR records) show or hide with the selected {@link ZoneTypeEnum}, using
 * the helpers in `zone.model.ts`. Hidden fields stay in the `FormGroup`, so switching type and back
 * keeps what was typed, and every field is sent on save. `ZonesController.ApplyWrite` stores them
 * all; the server ignores the ones that do not apply to the zone's type.
 *
 * In edit mode the page also manages RFC 9432 catalog membership and runs CAA and PTR
 * reconciliation on demand, through the `/api/zones/{id}/catalog-membership`,
 * `/catalog-members`, `/reconcile-caa` and `/reconcile-ptr` endpoints. The TSIG key and cloud
 * credential pickers are filled from `GET /api/tsig-keys` and `GET /api/cloud-provider-credentials`
 * when the user holds `tsigkeys:read` or `cloudprovidercredentials:read`.
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
  private readonly authService = inject(AuthService);

  /** The value of the TSIG key select's "Enter a custom name" option, for the template. */
  readonly customTsigKeyOption = CUSTOM_TSIG_KEY_OPTION;

  /** The value of the cloud credential select's "Enter a custom ID" option, for the template. */
  readonly customCloudCredentialOption = CUSTOM_CLOUD_CREDENTIAL_OPTION;

  /**
   * The TSIG keys offered by the TSIG key select. They are fetched once on load as a single page of
   * up to 500, not paged.
   */
  readonly tsigKeys = signal<TsigKeyDetail[]>([]);

  /** The cloud provider credentials offered by the credential select, fetched like {@link tsigKeys}. */
  readonly cloudCredentials = signal<CloudProviderCredentialDetail[]>([]);

  /** Whether {@link tsigKeys} couldn't be loaded (usually no `tsigkeys:read`), leaving it empty. */
  readonly tsigKeysUnavailable = signal(false);

  /** Whether {@link cloudCredentials} couldn't be loaded (usually no `cloudprovidercredentials:read`). */
  readonly cloudCredentialsUnavailable = signal(false);

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
    // The transport SecondaryZoneRefreshService pulls the zone over, for the transfer-in types
    // (see showTransferSection). Plain matches ZoneEntity.TransferTransport's default.
    transferTransport: [ZoneTransferTransportEnum.Plain as ZoneTransferTransportEnum],
    forwarderAddressesText: [''],
    notifyAddressesText: [''],
    tsigKeyName: [''],
    // UI-only; save() does not send it. It holds '' (no key), a listed key's name, or
    // CUSTOM_TSIG_KEY_OPTION while the operator types a name into tsigKeyName.
    tsigKeySelection: [''],
    dynamicUpdateEnabled: [false],
    isDnssecSigned: [false],
    dnssecAlgorithm: [DnssecAlgorithmEnum.Ecdsap256Sha256 as DnssecAlgorithmEnum],
    denialOfExistenceMode: [
      DnssecDenialOfExistenceModeEnum.Nsec3 as DnssecDenialOfExistenceModeEnum,
    ],
    // ZoneEntity.Nsec3Iterations is a 16-bit unsigned integer.
    nsec3Iterations: [10, [Validators.min(0), Validators.max(65535)]],
    nsec3SaltHex: [''],
    cloudProviderCredentialId: [''],
    // UI-only, like tsigKeySelection: '' (none), a listed credential's id, or
    // CUSTOM_CLOUD_CREDENTIAL_OPTION while the operator types an id into cloudProviderCredentialId.
    cloudProviderCredentialSelection: [''],
    cloudProviderZoneId: [''],
    // A .NET TimeSpan in the "c" format, so an optional "d." days prefix (e.g. "1.00:05:00") is
    // allowed alongside "hh:mm:ss".
    cloudSyncIntervalText: ['00:05:00', [Validators.pattern(/^(\d{1,2}\.)?\d{1,2}:\d{2}:\d{2}$/)]],
    autoManageCaaRecords: [null as boolean | null],
    caaIssuerDomain: [''],
    autoManagePtrRecords: [null as boolean | null],
    // Null inherits the PtrSyncOptions default. The min/max mirror ZonesController's ranges
    // (IPv4 24 to 30, IPv6 16 to 124); the IPv6 "multiple of 4" rule is checked only by the server.
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
   * The keys of the collapsible sections the operator has collapsed. Every section starts open,
   * so no field is hidden until the operator chooses. Collapsing only affects display: the
   * section's controls keep their values in the `FormGroup`.
   */
  private readonly collapsedSections = signal<ReadonlySet<string>>(new Set());

  /** This method reports whether the named section is currently expanded. */
  isSectionOpen(section: string): boolean {
    return !this.collapsedSections().has(section);
  }

  /** This method collapses or expands the named section. */
  toggleSection(section: string): void {
    this.collapsedSections.update((collapsed) => {
      const next = new Set(collapsed);
      if (next.has(section)) next.delete(section);
      else next.add(section);
      return next;
    });
  }

  /** The TSIG key select's current value, mirrored into a signal like {@link selectedType}. */
  readonly tsigKeySelection = signal<string>('');
  readonly showCustomTsigKeyField = computed(
    () => this.tsigKeySelection() === CUSTOM_TSIG_KEY_OPTION,
  );

  /** The cloud credential select's current value, mirrored like {@link tsigKeySelection}. */
  readonly cloudCredentialSelection = signal<string>('');
  readonly showCustomCloudCredentialField = computed(
    () => this.cloudCredentialSelection() === CUSTOM_CLOUD_CREDENTIAL_OPTION,
  );

  /**
   * Whether the catalog membership section (join or leave a catalog through `POST` and
   * `DELETE /api/zones/{id}/catalog-membership`) shows. It needs a saved zone, since a new one has
   * no id yet, of a type `CatalogZoneMembershipService.IsEligibleMemberZoneType` accepts
   * (`Primary` or `CloudPrimary`).
   */
  readonly showCatalogMembershipSection = computed(
    () => this.isEditMode && isLocallyOriginatedZoneType(this.selectedType()),
  );

  /**
   * Whether the read-only catalog members section (`GET /api/zones/{id}/catalog-members`) shows:
   * for a saved Catalog or SecondaryCatalog zone. Otherwise the members are visible only as the
   * catalog's PTR records.
   */
  readonly showCatalogMembersListSection = computed(
    () =>
      this.isEditMode &&
      (this.selectedType() === ZoneTypeEnum.Catalog ||
        this.selectedType() === ZoneTypeEnum.SecondaryCatalog),
  );

  /** The Catalog zones offered by the "join a catalog" select. */
  readonly catalogOptions = signal<ZoneDetail[]>([]);

  /** The id of the catalog zone this saved zone belongs to, or `null` if none. */
  readonly memberOfCatalogZoneId = signal<string | null>(null);

  /** The catalog chosen in the join select, kept apart from {@link memberOfCatalogZoneId} until "Join catalog" succeeds. */
  readonly selectedCatalogZoneId = signal<string>('');

  /** The member zone names of this catalog zone, for {@link showCatalogMembersListSection}. */
  readonly catalogMembers = signal<string[]>([]);

  /** Whether a join/leave-catalog call is currently in flight. */
  readonly catalogActionPending = signal(false);

  /** The latest catalog load, join or leave failure, kept apart from the main form's {@link errorMessage}. */
  readonly catalogActionError = signal<string | null>(null);

  /**
   * Whether the "Reconcile CAA now" and "Reconcile PTR now" buttons
   * (`POST /api/zones/{id}/reconcile-caa` and `/reconcile-ptr`) show: for a saved zone of a type
   * both reconciliation services accept, the same condition as {@link showOriginationSections}.
   */
  readonly showReconcileNowSection = computed(
    () => this.isEditMode && this.showOriginationSections(),
  );

  /** Whether a CAA reconciliation call is currently in flight. */
  readonly reconcileCaaPending = signal(false);

  /** Whether a PTR reconciliation call is currently in flight. */
  readonly reconcilePtrPending = signal(false);

  /** The latest CAA reconciliation result or error, as one summary line. */
  readonly reconcileCaaResult = signal<string | null>(null);

  /** The latest PTR reconciliation result or error, as one summary line. */
  readonly reconcilePtrResult = signal<string | null>(null);

  /** The joined catalog's name, or its id when it is not in {@link catalogOptions}. */
  readonly memberOfCatalogZoneName = computed(() => {
    const id = this.memberOfCatalogZoneId();
    if (!id) return null;
    return this.catalogOptions().find((zone) => zone.id === id)?.name ?? id;
  });

  constructor() {
    this.form.controls.type.valueChanges.subscribe((type) => this.selectedType.set(type));
    // A listed key copies its name into tsigKeyName, the control that is saved, and "None" clears
    // it. The custom option leaves tsigKeyName alone so typed text is not overwritten.
    this.form.controls.tsigKeySelection.valueChanges.subscribe((selection) => {
      this.tsigKeySelection.set(selection);
      if (selection === CUSTOM_TSIG_KEY_OPTION) return;
      this.form.controls.tsigKeyName.setValue(selection);
    });
    // The same for the cloud credential select and cloudProviderCredentialId.
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
   * This method loads the zone (in edit mode) and the TSIG key and cloud credential lists in
   * parallel; `applyExistingZone` needs all three to pick the initial selections. Each list needs
   * its read permission. A list the user cannot read leaves only the picker's "Enter a custom"
   * choice (see {@link tsigKeysUnavailable}) instead of failing the form.
   */
  private async initialize(): Promise<void> {
    try {
      const [tsigKeys, cloudCredentials, zone] = await Promise.all([
        this.loadOptionalList('tsigkeys:read', () =>
          firstValueFrom(this.tsigKeysApi.apiTsigKeysGet({ skip: 0, take: 500 })),
        ),
        this.loadOptionalList('cloudprovidercredentials:read', () =>
          firstValueFrom(
            this.cloudCredentialsApi.apiCloudProviderCredentialsGet({ skip: 0, take: 500 }),
          ),
        ),
        this.isEditMode && this.zoneId
          ? firstValueFrom(this.zonesApi.apiZonesIdGet({ id: this.zoneId }))
          : Promise.resolve(null),
      ]);

      this.tsigKeys.set(tsigKeys ?? []);
      this.tsigKeysUnavailable.set(tsigKeys === null);
      this.cloudCredentials.set(cloudCredentials ?? []);
      this.cloudCredentialsUnavailable.set(cloudCredentials === null);

      if (zone) {
        this.applyExistingZone(zone);
        // Catalog data is secondary, so it loads in the background and reports failures in
        // catalogActionError without blocking the form.
        void this.loadCatalogMembershipContextAsync(zone);
      }
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this zone.'));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * This method loads one picker's list, or returns `null` when the user lacks the permission
   * (skipping a request the API would refuse) or the request fails.
   */
  private async loadOptionalList<T>(
    permission: string,
    load: () => Promise<{ items?: T[] | null }>,
  ): Promise<T[] | null> {
    if (!this.authService.hasPermission(permission)) return null;

    try {
      return (await load()).items ?? [];
    } catch {
      return null;
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
      // Omitted when the zone is not signed; the PUT then stores no algorithm.
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
   * This method fills the form from a fetched zone and sets the picker selections. It runs inside
   * {@link initialize}'s `try`/`catch`, after the key and credential lists have loaded.
   */
  private applyExistingZone(zone: ZoneDetail): void {
    const tsigKeyName = zone.tsigKeyName ?? '';
    // A listed key name is selected in the list. A name that is not listed (renamed, deleted or
    // not readable) switches to custom entry rather than being dropped. tsigKeyName always keeps
    // the zone's stored value.
    const tsigKeySelection =
      tsigKeyName.length === 0
        ? ''
        : this.tsigKeys().some((key) => key.name === tsigKeyName)
          ? tsigKeyName
          : CUSTOM_TSIG_KEY_OPTION;

    const cloudProviderCredentialId = zone.cloudProviderCredentialId ?? '';
    // The same fallback, matched by id, since ZoneEntity.CloudProviderCredentialId stores the id.
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
   * This method loads the RFC 9432 catalog data for a saved zone. For a Primary or CloudPrimary
   * zone it takes the membership from {@link ZoneDetail} and lists the Catalog zones it can join
   * (from the first 500 zones). For a Catalog or SecondaryCatalog zone it loads the member list.
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

  /** This method joins the saved zone to the catalog in {@link selectedCatalogZoneId}. */
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

  /** This method removes the saved zone from its catalog. */
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
   * This method runs CAA reconciliation for the saved zone now (`POST
   * /api/zones/{id}/reconcile-caa`) and shows the counts or skip reason as a summary line. The
   * server runs the same logic it applies automatically after zone changes.
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
   * This method runs PTR sync for the saved zone now (`POST /api/zones/{id}/reconcile-ptr`), like
   * {@link reconcileCaaNow}. It includes RFC 2317 classless delegation records when the zone's
   * IPv4 prefix length, or the global default, calls for them.
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

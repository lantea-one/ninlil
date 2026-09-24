import { Component, OnInit, computed, inject, signal } from '@angular/core';
import {
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
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
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  DhcpOptionCatalogEntryDetail,
  DhcpOptionCatalogService,
  DhcpOptionValueSourceEnum,
  DhcpOptionValueTypeEnum,
  DhcpScopeCustomOptionDetail,
  DhcpScopeCustomOptionWriteRequest,
  DhcpScopeCustomOptionsService,
  DhcpScopeKindEnum,
  DhcpScopesService,
  InternetProtocolVersionEnum,
  ZoneDetail,
  ZoneTypeEnum,
  ZonesService,
} from '../../../../api-client';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { planDhcpOptionReconciliation } from '../../shared/dhcp-option-reconcile.util';
import { decodeDhcpOptionValue, encodeDhcpOptionValue } from '../../shared/dhcp-option-value.util';
import { joinLines, splitLines } from './dhcp-scope-form.model';

/**
 * This type alias names one custom-option table row's own typed shape explicitly, the same
 * `noPropertyAccessFromIndexSignature`-driven pattern `RaRouteFormGroup`
 * (`DhcpSettingsComponent`) already establishes for a `FormArray` row. `catalogSelection` is the
 * dropdown's own bound value -- either a catalog entry's `code` (as a string) or the literal
 * sentinel `'custom'` for a genuinely uncataloged raw tag; `optionTag` is the real numeric tag
 * this row actually submits, kept in sync with `catalogSelection` by
 * {@link DhcpScopeFormComponent.onOptionCatalogSelectionChange}. `valueSource` is a real,
 * server-side string-enum field (`DhcpOptionValueSourceEnum`) worked around the same
 * generated-client-quirk way `DhcpTagRuleFormComponent`'s own match-mode fields already are (see
 * that class's own doc comment) -- kept as a plain `'Custom' | 'ServerAddress'` string throughout
 * this component and cast only at the two points a write request is actually built.
 */
type DhcpScopeOptionRowFormGroup = FormGroup<{
  id: FormControl<string | null>;
  catalogSelection: FormControl<string>;
  optionTag: FormControl<number>;
  valueSource: FormControl<'Custom' | 'ServerAddress'>;
  value: FormControl<string>;
  isDisabled: FormControl<boolean>;
  comments: FormControl<string>;
}>;

/** The literal `catalogSelection` value meaning "a raw, uncataloged tag" rather than a real catalog entry. */
const CUSTOM_TAG_SELECTION = 'custom';

/**
 * This interface is the small, derived-per-row display state {@link DhcpScopeFormComponent}
 * recomputes (never incrementally patched) every time something that could invalidate it changes
 * -- a row added/removed, a row's own catalog selection changed, the catalog itself reloaded for
 * a new protocol version, or an existing scope's options finished loading. Recomputing the whole
 * array each time is simple and cheap at this table's realistic size, and keeps this zoneless
 * component's own reactivity explicit rather than relying on a template expression reading a
 * `FormControl`'s live value directly (which this app's own established convention -- see this
 * class's own `selectedProtocolVersion`/`selectedKind` signals -- avoids in favor of an explicit
 * signal update on every relevant `valueChanges`/DOM event instead).
 */
interface DhcpOptionRowViewState {
  /** The resolved catalog entry's own value type, or `null` for a row with no catalog match (an uncataloged/custom tag, or a stored option tag the current protocol's catalog doesn't recognize). */
  valueType: DhcpOptionValueTypeEnum | null;
  /** Whether the resolved catalog entry allows "use this server's own address" as this row's value source. */
  isServerAddressEligible: boolean;
}

/**
 * This component is both the "create a DHCP scope" and "edit a DHCP scope" page -- one component
 * for both, distinguished by whether the route supplies a `scopeId` (see `app.routes.ts`). A
 * scope's own reservations are still managed separately, through `DhcpReservationListComponent`,
 * but its custom options are now managed inline, right here, as a dynamic table -- an option
 * dropdown (sourced from `GET /api/dhcp-option-catalog`, filtered to this scope's own
 * `protocolVersion`) plus a value editor that adapts to the selected option's own value type
 * (an IP address field, a number field, a checkbox, ...) instead of a raw hex box, and a "use
 * this server's own address" toggle for a catalog entry that allows it -- see
 * `DhcpOptionValueSourceEnum`'s own doc comment for exactly what that substitutes at delivery
 * time. A row whose tag isn't in the catalog (typed by hand via the dropdown's own "Custom /
 * other option..." choice) still falls back to a plain raw-hex value box, unchanged from this
 * form's original behavior -- nothing about a genuinely custom option's own real flexibility was
 * lost by adding the catalog-driven path alongside it. Saving reconciles this table's current
 * rows against `DhcpScopeCustomOptionsController`'s own real per-row CRUD endpoints (it has no
 * bulk-replace action) via {@link planDhcpOptionReconciliation} -- for a brand-new scope (no
 * `scopeId` yet), every row is naturally a create, issued only after the scope itself is created
 * and its own real id is known.
 *
 * <p>`gatewayAddresses`/`dnsServerAddresses` are each edited as a one-address-per-line textarea,
 * split/joined via {@link splitLines}/{@link joinLines} -- the same convention
 * `TransportSettingsComponent` already establishes for a certificate's own subject alternative
 * names list. `defaultLeaseDuration`/`maxLeaseDuration` are edited as raw .NET `TimeSpan` strings
 * (for example `1.00:00:00` for one day) -- this project's own established convention for every
 * duration field across the admin SPA (see `TransportSettingsComponent`'s own `tcpIdleTimeout`),
 * rather than a bespoke duration picker.
 *
 * <p>`dnsZoneId` is an optional DHCP-&gt;DNS integration setting: the zone a lease drawn from this
 * scope should auto-register an A/AAAA record for its own presented host name into. Its own
 * dropdown, populated from a one-time zones fetch narrowed to `Primary`/`CloudPrimary` (the only
 * zone types this feature ever actually acts on -- see `DhcpScopeEntity.DnsZoneId`'s own doc
 * comment), follows the exact same "optional dropdown resolved from a one-time fetch" pattern
 * `DhcpTagRuleFormComponent`'s own scope-restriction dropdown already establishes.
 *
 * <p>`bootFileName`/`nextServerAddress` are this scope's own optional PXE boot configuration --
 * see `DhcpScopeEntity.BootFileName`'s own doc comment. Left blank, `nextServerAddress` falls
 * back to this server's own Server Identifier address at lease-grant time.
 *
 * <p>`kind`/`delegatedPrefixLength` are meaningful only for an IPv6 scope -- RFC 8415 Prefix
 * Delegation (`DhcpScopeKindEnum.PrefixDelegation`) has no DHCPv4 analogue at all, so the `kind`
 * dropdown, and its own `delegatedPrefixLength` field, are shown only once {@link protocolVersion}
 * is IPv6 ({@link showKindField}); the `delegatedPrefixLength` field itself is shown, and required,
 * only once `kind` is actually `PrefixDelegation` ({@link showDelegatedPrefixLengthField}).
 * Switching `protocolVersion` away from IPv6 resets `kind` back to `AddressAssignment`, and
 * switching `kind` away from `PrefixDelegation` resets `delegatedPrefixLength` back to
 * `null` -- both mirror the exact "reset the field the new mode no longer uses, re-require the one
 * it does" convention `ClientGroupNetworkFormComponent`'s own `matchKind` switch already
 * establishes, so `save()` never submits a stale, no-longer-applicable value. Switching
 * `protocolVersion` also refetches the option catalog {@link options}'s own dropdown uses, since
 * DHCPv4 and DHCPv6 each have their own distinct set of well-known options.
 */
@Component({
  selector: 'app-dhcp-scope-form',
  imports: [
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
    FormTextDirective,
    IconDirective,
    SpinnerComponent,
  ],
  templateUrl: './dhcp-scope-form.component.html',
  styleUrl: './dhcp-scope-form.component.scss',
})
export class DhcpScopeFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly scopesApi = inject(DhcpScopesService);
  private readonly zonesApi = inject(ZonesService);
  private readonly optionCatalogApi = inject(DhcpOptionCatalogService);
  private readonly customOptionsApi = inject(DhcpScopeCustomOptionsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly scopeId = this.route.snapshot.paramMap.get('scopeId');

  /** Whether this instance is editing an existing scope rather than creating a new one. */
  readonly isEditMode = this.scopeId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Every zone this scope's own optional DHCP-&gt;DNS integration setting can designate. */
  readonly eligibleZones = signal<ZoneDetail[]>([]);

  /** This scope's own well-known DHCP option catalog, refetched whenever {@link protocolVersion} changes. */
  readonly optionCatalog = signal<DhcpOptionCatalogEntryDetail[]>([]);

  /** Every custom-option row's own id as it stood right after this table was last loaded -- see {@link planDhcpOptionReconciliation}. */
  private originallyLoadedOptionIds: string[] = [];

  /** {@link DhcpOptionRowViewState}, one entry per {@link options} row, recomputed on every change that could invalidate it. */
  readonly optionRowViewState = signal<DhcpOptionRowViewState[]>([]);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    protocolVersion: this.formBuilder.control<InternetProtocolVersionEnum>(
      InternetProtocolVersionEnum.Ipv4,
      [Validators.required],
    ),
    rangeStartAddress: ['', [Validators.required]],
    rangeEndAddress: ['', [Validators.required]],
    subnetPrefixLength: [24, [Validators.required, Validators.min(0), Validators.max(128)]],
    defaultLeaseDuration: ['1.00:00:00', [Validators.required]],
    maxLeaseDuration: ['1.00:00:00', [Validators.required]],
    isDisabled: [false],
    gatewayAddresses: [''],
    dnsServerAddresses: [''],
    comments: [''],
    dnsZoneId: [''],
    bootFileName: [''],
    nextServerAddress: [''],
    kind: this.formBuilder.control<DhcpScopeKindEnum>(DhcpScopeKindEnum.AddressAssignment),
    delegatedPrefixLength: [null as number | null],
    options: this.formBuilder.array<DhcpScopeOptionRowFormGroup>([]),
  });

  /** {@link options}'s own `FormArray`, exposed for the template. */
  get options() {
    return this.form.controls.options;
  }

  /** {@link form}'s own live `protocolVersion` value, tracked as a plain signal since `valueChanges` isn't one. */
  private readonly selectedProtocolVersion = signal<InternetProtocolVersionEnum>(
    InternetProtocolVersionEnum.Ipv4,
  );

  /** {@link form}'s own live `kind` value, tracked the same way {@link selectedProtocolVersion} is. */
  private readonly selectedKind = signal<DhcpScopeKindEnum>(DhcpScopeKindEnum.AddressAssignment);

  /** Whether the `kind` dropdown itself is shown at all -- IPv6 scopes only, per this class's own doc comment. */
  readonly showKindField = computed(
    () => this.selectedProtocolVersion() === InternetProtocolVersionEnum.Ipv6,
  );

  /** Whether the `delegatedPrefixLength` field is shown (and required) -- an IPv6 scope set to `PrefixDelegation` only. */
  readonly showDelegatedPrefixLengthField = computed(
    () => this.showKindField() && this.selectedKind() === DhcpScopeKindEnum.PrefixDelegation,
  );

  constructor() {
    this.form.controls.protocolVersion.valueChanges.subscribe((protocolVersion) => {
      this.selectedProtocolVersion.set(protocolVersion);

      // Prefix Delegation has no DHCPv4 analogue -- switching away from IPv6 always resets `kind`
      // back to the only value a non-IPv6 scope ever legitimately has.
      if (
        protocolVersion !== InternetProtocolVersionEnum.Ipv6 &&
        this.form.controls.kind.value !== DhcpScopeKindEnum.AddressAssignment
      ) {
        this.form.controls.kind.setValue(DhcpScopeKindEnum.AddressAssignment);
      }

      void this.loadOptionCatalog(protocolVersion);
    });

    this.form.controls.kind.valueChanges.subscribe((kind) => {
      this.selectedKind.set(kind);

      if (kind === DhcpScopeKindEnum.PrefixDelegation) {
        this.form.controls.delegatedPrefixLength.setValidators([
          Validators.required,
          Validators.min(1),
          Validators.max(128),
        ]);
      } else {
        this.form.controls.delegatedPrefixLength.clearValidators();
        this.form.controls.delegatedPrefixLength.setValue(null);
      }

      this.form.controls.delegatedPrefixLength.updateValueAndValidity();
    });
  }

  ngOnInit(): void {
    void this.loadEligibleZones();
    void this.loadOptionCatalog(this.form.controls.protocolVersion.value);
    if (this.isEditMode) void this.loadExistingScope();
  }

  /** This method appends one new, empty custom-option row, defaulting to a raw/custom tag exactly like this form's original standalone add page did. */
  addOptionRow(): void {
    this.options.push(this.buildOptionRowGroup(null));
    this.recomputeOptionRowViewState();
  }

  /** This method removes one custom-option row -- its own `DELETE` (if it was ever persisted) is issued only when the whole scope is saved, per this class's own doc comment. */
  removeOptionRow(index: number): void {
    this.options.removeAt(index);
    this.recomputeOptionRowViewState();
  }

  /**
   * This method runs when one row's own catalog dropdown changes: keeps `optionTag` in sync with
   * the newly chosen catalog entry (or leaves it freely editable for
   * {@link CUSTOM_TAG_SELECTION}), resets `value`/`valueSource` since a prior value's own shape
   * is very unlikely to still make sense for a genuinely different option, and recomputes
   * {@link optionRowViewState}.
   */
  onOptionCatalogSelectionChange(index: number): void {
    const row = this.options.at(index);
    const selection = row.controls.catalogSelection.value;

    if (selection !== CUSTOM_TAG_SELECTION) {
      row.controls.optionTag.setValue(Number(selection));
    }

    row.controls.value.setValue('');
    row.controls.valueSource.setValue('Custom');
    this.recomputeOptionRowViewState();
  }

  /** This method creates (`POST`) or fully replaces (`PUT`) the scope, then reconciles the options table's own rows. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    let optionRequests: { id: string | null; request: DhcpScopeCustomOptionWriteRequest }[];

    try {
      optionRequests = this.buildOptionWriteRequests();
    } catch (error) {
      this.errorMessage.set(
        error instanceof Error ? error.message : 'One of the custom option values is invalid.',
      );
      this.submitting.set(false);
      return;
    }

    const value = this.form.getRawValue();
    const request = {
      name: value.name,
      protocolVersion: value.protocolVersion,
      rangeStartAddress: value.rangeStartAddress,
      rangeEndAddress: value.rangeEndAddress,
      subnetPrefixLength: value.subnetPrefixLength,
      defaultLeaseDuration: value.defaultLeaseDuration,
      maxLeaseDuration: value.maxLeaseDuration,
      isDisabled: value.isDisabled,
      gatewayAddresses: splitLines(value.gatewayAddresses),
      dnsServerAddresses: splitLines(value.dnsServerAddresses),
      comments: value.comments || null,
      dnsZoneId: value.dnsZoneId || null,
      bootFileName: value.bootFileName || null,
      nextServerAddress: value.nextServerAddress || null,
      kind: value.kind,
      delegatedPrefixLength: value.delegatedPrefixLength,
    };

    try {
      let resolvedScopeId: string;

      if (this.isEditMode && this.scopeId) {
        await firstValueFrom(
          this.scopesApi.apiDhcpScopesIdPut({
            id: this.scopeId,
            dhcpScopeWriteRequest: request,
          }),
        );
        resolvedScopeId = this.scopeId;
      } else {
        const created = await firstValueFrom(
          this.scopesApi.apiDhcpScopesPost({ dhcpScopeWriteRequest: request }),
        );
        resolvedScopeId = created.id!;
      }

      await this.reconcileOptions(resolvedScopeId, optionRequests);

      this.notification.success(this.isEditMode ? 'Scope saved.' : 'Scope created.');
      await this.router.navigate(['/dhcp-scopes']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this scope.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method looks up {@link optionCatalog}'s own entry for one row, or `undefined` for {@link CUSTOM_TAG_SELECTION}/an unmatched tag. */
  catalogEntryFor(row: DhcpScopeOptionRowFormGroup): DhcpOptionCatalogEntryDetail | undefined {
    const selection = row.controls.catalogSelection.value;
    if (selection === CUSTOM_TAG_SELECTION) return undefined;
    return this.optionCatalog().find((entry) => String(entry.code) === selection);
  }

  private async loadEligibleZones(): Promise<void> {
    try {
      const page = await firstValueFrom(this.zonesApi.apiZonesGet({ skip: 0, take: 500 }));
      this.eligibleZones.set(
        (page.items ?? []).filter(
          (zone) => zone.type === ZoneTypeEnum.Primary || zone.type === ZoneTypeEnum.CloudPrimary,
        ),
      );
    } catch {
      // A failed zone lookup only narrows this form's own optional DNS zone dropdown -- the rest
      // of the form remains usable, and a genuinely invalid dnsZoneId is still rejected
      // server-side.
    }
  }

  private async loadOptionCatalog(protocolVersion: InternetProtocolVersionEnum): Promise<void> {
    try {
      const entries = await firstValueFrom(
        this.optionCatalogApi.apiDhcpOptionCatalogGet({ protocolVersion }),
      );
      this.optionCatalog.set(entries);
    } catch {
      // A failed catalog fetch only narrows the options table's own dropdown to the "Custom /
      // other option..." choice for every row -- raw tag/hex entry still works, exactly as this
      // form always supported.
      this.optionCatalog.set([]);
    } finally {
      this.recomputeOptionRowViewState();
    }
  }

  /** This method rebuilds {@link optionRowViewState} from scratch against {@link options}'s current rows and {@link optionCatalog}'s current entries -- see that signal's own doc comment for why this is a full recompute rather than an incremental patch. */
  private recomputeOptionRowViewState(): void {
    this.optionRowViewState.set(
      this.options.controls.map((row) => {
        const entry = this.catalogEntryFor(row);
        return {
          valueType: entry?.valueType ?? null,
          isServerAddressEligible: entry?.isServerAddressEligible ?? false,
        };
      }),
    );
  }

  /** This method builds one option row's own `FormGroup`, from an existing loaded option or as a fresh, empty row defaulting to a raw/custom tag. */
  private buildOptionRowGroup(
    existing: DhcpScopeCustomOptionDetail | null,
  ): DhcpScopeOptionRowFormGroup {
    const optionTag = existing?.optionTag ?? 1;
    // `existing.valueSource` is real, server-side string-enum data at runtime ("Custom" /
    // "ServerAddress") despite its generated TypeScript type claiming a numeric 0/1 -- the same
    // generated-client quirk `DhcpTagRuleFormComponent`'s own doc comment documents for
    // `Dhcpv4TagMatchModeEnum`.
    const normalizedValueSource: 'Custom' | 'ServerAddress' =
      (existing?.valueSource as unknown as string) === 'ServerAddress' ? 'ServerAddress' : 'Custom';

    // A loaded row's own catalogSelection starts as the matching catalog entry's own code (as a
    // string) if one exists for the current catalog snapshot, or the "Custom / other option..."
    // sentinel otherwise -- the catalog for a brand-new row's own protocol is already loaded by
    // the time this method can be called from `addOptionRow`, but a row built while loading an
    // existing scope (see `loadExistingScope`) may run before that fetch resolves, in which case
    // every row starts as `CUSTOM_TAG_SELECTION` and is corrected retroactively by
    // `recomputeOptionRowViewState`'s own caller once the catalog itself arrives.
    const matchingEntry = existing
      ? this.optionCatalog().find((entry) => entry.code === optionTag)
      : undefined;
    const catalogSelection = matchingEntry ? String(matchingEntry.code) : CUSTOM_TAG_SELECTION;

    let displayValue = '';
    if (existing && normalizedValueSource === 'Custom') {
      try {
        displayValue = decodeDhcpOptionValue(
          matchingEntry?.valueType ?? null,
          existing.valueHex ?? '',
        );
      } catch {
        // The stored value doesn't fit the matched catalog entry's own expected byte shape (or
        // there's no catalog match at all) -- fall back to plain raw-hex editing for this one
        // row, exactly like an uncataloged tag always has, rather than losing the value or
        // crashing the form.
        displayValue = existing.valueHex ?? '';
      }
    }

    return this.formBuilder.group({
      id: [existing?.id ?? (null as string | null)],
      catalogSelection: [catalogSelection],
      optionTag: [optionTag, [Validators.required, Validators.min(0), Validators.max(255)]],
      valueSource: [normalizedValueSource],
      value: [displayValue],
      isDisabled: [existing?.isDisabled ?? false],
      comments: [existing?.comments ?? ''],
    });
  }

  /** This method turns {@link options}'s current rows into real write-request payloads, throwing with a row-identifying message the moment any one row's own value fails to encode. */
  private buildOptionWriteRequests(): {
    id: string | null;
    request: DhcpScopeCustomOptionWriteRequest;
  }[] {
    return this.options.controls.map((row, index) => {
      const raw = row.getRawValue();
      const entry = this.catalogEntryFor(row);
      const isServerAddress = raw.valueSource === 'ServerAddress';

      let valueHex = '';
      if (!isServerAddress) {
        try {
          valueHex = encodeDhcpOptionValue(entry?.valueType ?? null, raw.value);
        } catch (error) {
          const reason = error instanceof Error ? error.message : String(error);
          throw new Error(`Option row ${index + 1} (tag ${raw.optionTag}): ${reason}`, {
            cause: error,
          });
        }
      }

      return {
        id: raw.id,
        request: {
          optionTag: raw.optionTag,
          valueHex,
          isDisabled: raw.isDisabled,
          valueSource: (isServerAddress
            ? 'ServerAddress'
            : 'Custom') as unknown as DhcpOptionValueSourceEnum,
          comments: raw.comments || null,
        },
      };
    });
  }

  private async reconcileOptions(
    scopeId: string,
    optionRequests: { id: string | null; request: DhcpScopeCustomOptionWriteRequest }[],
  ): Promise<void> {
    const plan = planDhcpOptionReconciliation(
      optionRequests.map((entry) => ({ id: entry.id, request: entry.request })),
      this.originallyLoadedOptionIds,
    );

    for (const entry of plan.toCreate) {
      await firstValueFrom(
        this.customOptionsApi.apiDhcpScopesScopeIdCustomOptionsPost({
          scopeId,
          dhcpScopeCustomOptionWriteRequest: entry.request,
        }),
      );
    }

    for (const entry of plan.toUpdate) {
      await firstValueFrom(
        this.customOptionsApi.apiDhcpScopesScopeIdCustomOptionsIdPut({
          scopeId,
          id: entry.id!,
          dhcpScopeCustomOptionWriteRequest: entry.request,
        }),
      );
    }

    for (const id of plan.toDeleteIds) {
      await firstValueFrom(
        this.customOptionsApi.apiDhcpScopesScopeIdCustomOptionsIdDelete({ scopeId, id }),
      );
    }
  }

  private async loadExistingScope(): Promise<void> {
    if (!this.scopeId) return;

    try {
      const scope = await firstValueFrom(this.scopesApi.apiDhcpScopesIdGet({ id: this.scopeId }));

      // `protocolVersion` and `kind` are patched before `delegatedPrefixLength` deliberately --
      // each one's own valueChanges subscription (see this class's own constructor) may reset a
      // later field, so the actual loaded value must be applied last to win over that reset.
      this.form.patchValue({
        name: scope.name ?? '',
        protocolVersion: scope.protocolVersion ?? InternetProtocolVersionEnum.Ipv4,
        rangeStartAddress: scope.rangeStartAddress ?? '',
        rangeEndAddress: scope.rangeEndAddress ?? '',
        subnetPrefixLength: scope.subnetPrefixLength ?? 24,
        defaultLeaseDuration: scope.defaultLeaseDuration ?? '1.00:00:00',
        maxLeaseDuration: scope.maxLeaseDuration ?? '1.00:00:00',
        isDisabled: scope.isDisabled ?? false,
        gatewayAddresses: joinLines(scope.gatewayAddresses),
        dnsServerAddresses: joinLines(scope.dnsServerAddresses),
        comments: scope.comments ?? '',
        dnsZoneId: scope.dnsZoneId ?? '',
        bootFileName: scope.bootFileName ?? '',
        nextServerAddress: scope.nextServerAddress ?? '',
        kind: scope.kind ?? DhcpScopeKindEnum.AddressAssignment,
        delegatedPrefixLength: scope.delegatedPrefixLength ?? null,
      });

      // The protocolVersion patch above already triggered a fresh catalog fetch via its own
      // valueChanges subscription -- wait for it so each loaded row's own catalogSelection can
      // resolve against the real, matching catalog rather than whatever the form started with.
      await this.loadOptionCatalog(scope.protocolVersion ?? InternetProtocolVersionEnum.Ipv4);

      const optionsPage = await firstValueFrom(
        this.customOptionsApi.apiDhcpScopesScopeIdCustomOptionsGet({
          scopeId: this.scopeId,
          skip: 0,
          take: 500,
        }),
      );
      const existingOptions = optionsPage.items ?? [];

      this.options.clear();
      for (const option of existingOptions) this.options.push(this.buildOptionRowGroup(option));
      this.originallyLoadedOptionIds = existingOptions
        .map((option) => option.id)
        .filter((id): id is string => !!id);
      this.recomputeOptionRowViewState();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this scope.'));
    } finally {
      this.loading.set(false);
    }
  }
}

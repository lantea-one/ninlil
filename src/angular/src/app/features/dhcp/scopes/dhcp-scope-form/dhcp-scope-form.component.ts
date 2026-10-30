import { Component, OnInit, computed, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidatorFn,
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
import {
  findDuplicateOptionTag,
  planDhcpOptionReconciliation,
} from '../../shared/dhcp-option-reconcile.util';
import {
  assertDhcpv4OptionValueLength,
  assertDhcpv6OptionValueLength,
  dhcpv4OptionHint,
  dhcpv4OptionTagValidator,
  dhcpv6OptionCodeValidator,
} from '../../shared/dhcp-option-rules.util';
import { decodeDhcpOptionValue, encodeDhcpOptionValue } from '../../shared/dhcp-option-value.util';
import { joinLines, splitLines } from './dhcp-scope-form.model';

/**
 * This type names the typed shape of one custom-option row, so the controls can be read without
 * index-signature access. `catalogSelection` is the dropdown value: a catalog entry's `code` as a
 * string, or `'custom'` for a tag not in the catalog. `optionTag` is the tag actually sent, kept
 * in step with `catalogSelection` by {@link DhcpScopeFormComponent.onOptionCatalogSelectionChange}.
 */
type DhcpScopeOptionRowFormGroup = FormGroup<{
  id: FormControl<string | null>;
  catalogSelection: FormControl<string>;
  optionTag: FormControl<number>;
  valueSource: FormControl<DhcpOptionValueSourceEnum>;
  value: FormControl<string>;
  isDisabled: FormControl<boolean>;
  comments: FormControl<string>;
  /** UI only, never sent: the catalog entry whose format the stored bytes did not fit, or `null`. */
  storedValueNote: FormControl<string | null>;
}>;

/**
 * This interface is one option write the save is about to make, with the row it came from so a
 * failure can name the row and a created id can be written back to it.
 */
interface DhcpScopeOptionWrite {
  /** The row's `FormGroup`; the created id is written to its `id` control. */
  row: DhcpScopeOptionRowFormGroup;
  /** The 1-based row number shown in messages. */
  rowNumber: number;
  /** The saved option's id, or `null` for a row not yet saved. */
  id: string | null;
  request: DhcpScopeCustomOptionWriteRequest;
}

/** The `catalogSelection` value for a tag typed by hand rather than chosen from the catalog. */
const CUSTOM_TAG_SELECTION = 'custom';

/**
 * This interface is the display state of one option row. The component rebuilds the whole array
 * whenever a row is added or removed, a row's catalog selection changes, the catalog reloads, or
 * a scope's options finish loading. That is cheap at this size and keeps the zoneless template
 * on signals instead of reading form control values directly.
 */
interface DhcpOptionRowViewState {
  /** The catalog entry's value type, or `null` for a custom tag or a tag the catalog does not know. */
  valueType: DhcpOptionValueTypeEnum | null;
  /** Whether the catalog entry allows "use this server's address" as the value. */
  isServerAddressEligible: boolean;
}

/**
 * This component is the create and edit page for a DHCP scope (`/dhcp-scopes/new` and
 * `/dhcp-scopes/:scopeId/edit`, `dhcpscopes:write`); edit mode is chosen by the `scopeId` route
 * parameter. It saves with `POST /api/dhcp-scopes` or `PUT /api/dhcp-scopes/{id}`. Reservations
 * have their own page (`DhcpReservationListComponent`).
 *
 * <p>The scope's custom options are a table on this form. Each row picks an option from
 * `GET /api/dhcp-option-catalog` for the scope's protocol version and edits its value with a
 * field fitting the option's type (`dhcp-option-value.util.ts`), or with "use this server's
 * address" where the catalog allows it (`DhcpOptionValueSourceEnum`). The "Custom / other option"
 * choice takes a typed tag and a raw hex value. On save the rows are written one call at a time
 * through `/api/dhcp-scopes/{id}/custom-options`, which has no bulk replace
 * ({@link planDhcpOptionReconciliation}).
 *
 * <p>A stored value that does not decode for its catalog type is shown as a custom row with the
 * raw hex and a note, and saves unchanged. Two rows with the same tag are refused before any
 * call ({@link findDuplicateOptionTag}).
 *
 * <p>The save is not atomic. The scope is saved first, then its options in the order deletes,
 * updates, creates. {@link savedScopeId} holds the scope's id once it exists, so a retry after a
 * failed option call edits that scope instead of creating a second one, and each created option's
 * id is written back to its row so the retry updates it instead of posting it again. The page
 * navigates away only when the scope and every option call succeeded.
 *
 * <p>Custom options of an IPv6 scope are delivered like those of an IPv4 scope, with the DHCPv6
 * limits (codes 1 to 255 without the codes the engine owns). The gateway field shows for IPv4
 * scopes only.
 *
 * <p>Gateways and DNS servers are one address per line ({@link splitLines}, {@link joinLines}).
 * Lease durations are .NET `TimeSpan` strings, such as `1.00:00:00` for one day.
 *
 * <p>`dnsZoneId` is the zone leases from this scope register A/AAAA records in; the select offers
 * Primary and CloudPrimary zones, the only types registration works with. `bootFileName` and
 * `nextServerAddress` are the PXE boot settings; a blank next server means this server's
 * address.
 *
 * <p>`kind` and `delegatedPrefixLength` apply to IPv6 only, since Prefix Delegation (RFC 8415)
 * has no DHCPv4 form: the `kind` select shows for IPv6 scopes ({@link showKindField}) and the
 * prefix length for Prefix Delegation ({@link showDelegatedPrefixLengthField}). Switching away
 * resets the field that no longer applies, so a stale value is never sent. Changing the protocol
 * version also reloads the option catalog.
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

  /** The `scopeId` route parameter: the scope to load, or `null` on the create page. */
  private readonly routeScopeId = this.route.snapshot.paramMap.get('scopeId');

  /**
   * The id of the scope this page edits: the route parameter, or the id the first successful
   * `POST` returned. It is state, not a constant, so a save that created the scope but failed on
   * an option row makes the next save a `PUT` instead of a second `POST`.
   */
  readonly savedScopeId = signal<string | null>(this.routeScopeId);

  /** Whether the next save edits an existing scope: true on the edit page and after a create. */
  readonly isEditMode = computed(() => this.savedScopeId() !== null);

  readonly loading = signal(this.routeScopeId !== null);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** The zones offered for DNS registration (Primary and CloudPrimary). */
  readonly eligibleZones = signal<ZoneDetail[]>([]);

  /** The DHCP option catalog for the selected protocol, refetched when the protocol changes. */
  readonly optionCatalog = signal<DhcpOptionCatalogEntryDetail[]>([]);

  /** The ids of the option rows as loaded, for {@link planDhcpOptionReconciliation}. */
  private originallyLoadedOptionIds: string[] = [];

  /** One {@link DhcpOptionRowViewState} per option row. */
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

  /** The option rows' `FormArray`, for the template. */
  get options() {
    return this.form.controls.options;
  }

  /** The selected `protocolVersion` as a signal (the form's `valueChanges` is not one). */
  private readonly selectedProtocolVersion = signal<InternetProtocolVersionEnum>(
    InternetProtocolVersionEnum.Ipv4,
  );

  /** The selected `kind` as a signal. */
  private readonly selectedKind = signal<DhcpScopeKindEnum>(DhcpScopeKindEnum.AddressAssignment);

  /** Whether the `kind` select is shown: IPv6 scopes only. */
  readonly showKindField = computed(
    () => this.selectedProtocolVersion() === InternetProtocolVersionEnum.Ipv6,
  );

  /** Whether the `delegatedPrefixLength` field is shown and required: IPv6 Prefix Delegation scopes only. */
  readonly showDelegatedPrefixLengthField = computed(
    () => this.showKindField() && this.selectedKind() === DhcpScopeKindEnum.PrefixDelegation,
  );

  /** Whether the gateway field is shown: IPv4 scopes only, because DHCPv6 has no router option (Router Advertisements supply routers). */
  readonly showGatewayField = computed(
    () => this.selectedProtocolVersion() === InternetProtocolVersionEnum.Ipv4,
  );

  /**
   * This validator applies the protocol's option limits to an option row: tags 1 to 254 without
   * the server-owned tags for DHCPv4, codes 1 to 255 without the codes the engine writes itself
   * for DHCPv6 (the server rejects both on save).
   */
  private readonly optionTagValidator: ValidatorFn = (control) =>
    this.selectedProtocolVersion() === InternetProtocolVersionEnum.Ipv4
      ? dhcpv4OptionTagValidator(control)
      : dhcpv6OptionCodeValidator(control);

  /** Whether the scope is a DHCPv6 scope; the option rows show the DHCPv6 value length hint. */
  readonly isDhcpv6Scope = computed(
    () => this.selectedProtocolVersion() === InternetProtocolVersionEnum.Ipv6,
  );

  /**
   * This method returns the message for a custom option row whose tag the protocol refuses, or
   * null. It repeats what the server refuses on save, so the row says so while the operator
   * types. An empty tag has no message here: `required` is reported on save.
   */
  optionTagError(row: { controls: { optionTag: AbstractControl } }): string | null {
    const errors = row.controls.optionTag.errors;
    if (!errors) return null;
    const isDhcpv6 = this.isDhcpv6Scope();
    if (errors['min'] || errors['max']) {
      return isDhcpv6
        ? 'A DHCPv6 option code is between 1 and 255.'
        : 'A DHCPv4 option tag is between 1 and 254 (0 is Pad and 255 is End).';
    }
    const reserved = errors['reservedTag'] as { tag: number; name?: string } | undefined;
    if (reserved) {
      return isDhcpv6
        ? `Code ${reserved.tag} (${reserved.name}) is written by the server and cannot be set here.`
        : `Tag ${reserved.tag} is set by the server or the relay agent and cannot be set here.`;
    }
    return null;
  }

  /** This method returns the hint for a custom option row (IPv4 scopes only), or null. */
  optionHint(tag: number | null): string | null {
    return this.selectedProtocolVersion() === InternetProtocolVersionEnum.Ipv4
      ? dhcpv4OptionHint(tag)
      : null;
  }

  constructor() {
    this.form.controls.protocolVersion.valueChanges.subscribe((protocolVersion) => {
      this.selectedProtocolVersion.set(protocolVersion);

      // The tag limits depend on the protocol, so every row is checked again.
      this.options.controls.forEach((row) => row.controls.optionTag.updateValueAndValidity());

      // DHCPv6 has no router option, and the server rejects a gateway on an IPv6 scope.
      if (protocolVersion === InternetProtocolVersionEnum.Ipv6) {
        this.form.controls.gatewayAddresses.setValue('');
      }

      // Prefix Delegation exists only for IPv6, so a non-IPv6 scope is reset to AddressAssignment.
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
    if (this.routeScopeId !== null) void this.loadExistingScope();
  }

  /** This method adds an empty option row, starting as a custom tag. */
  addOptionRow(): void {
    this.options.push(this.buildOptionRowGroup(null));
    this.recomputeOptionRowViewState();
  }

  /** This method removes an option row; a saved row is deleted on the server when the scope is saved. */
  removeOptionRow(index: number): void {
    this.options.removeAt(index);
    this.recomputeOptionRowViewState();
  }

  /**
   * This method runs when a row's catalog dropdown changes. It sets `optionTag` to the chosen
   * entry (or leaves it editable for {@link CUSTOM_TAG_SELECTION}), clears `value` and
   * `valueSource`, since the old value rarely fits a different option, and recomputes
   * {@link optionRowViewState}.
   */
  onOptionCatalogSelectionChange(index: number): void {
    const row = this.options.at(index);
    const selection = row.controls.catalogSelection.value;

    if (selection !== CUSTOM_TAG_SELECTION) {
      row.controls.optionTag.setValue(Number(selection));
    }

    row.controls.value.setValue('');
    row.controls.valueSource.setValue(DhcpOptionValueSourceEnum.Custom);
    row.controls.storedValueNote.setValue(null);
    this.recomputeOptionRowViewState();
  }

  /**
   * This method checks every option value and tag, creates the scope (`POST`) or replaces it
   * (`PUT`), then saves the option rows one call at a time ({@link reconcileOptions}). A new
   * scope's options are saved after the scope exists, since they need its id.
   *
   * <p>When the scope is saved but an option call fails, the error says so and names the row, the
   * page stays, and the next save edits the scope ({@link savedScopeId}).
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    let optionRequests: DhcpScopeOptionWrite[];

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
      gatewayAddresses:
        value.protocolVersion === InternetProtocolVersionEnum.Ipv4
          ? splitLines(value.gatewayAddresses)
          : [],
      dnsServerAddresses: splitLines(value.dnsServerAddresses),
      comments: value.comments || null,
      dnsZoneId: value.dnsZoneId || null,
      bootFileName: value.bootFileName || null,
      nextServerAddress: value.nextServerAddress || null,
      kind: value.kind,
      delegatedPrefixLength: value.delegatedPrefixLength,
    };

    let scopeId = this.savedScopeId();
    const isCreate = scopeId === null;

    try {
      if (scopeId !== null) {
        await firstValueFrom(
          this.scopesApi.apiDhcpScopesIdPut({
            id: scopeId,
            dhcpScopeWriteRequest: request,
          }),
        );
      } else {
        const created = await firstValueFrom(
          this.scopesApi.apiDhcpScopesPost({ dhcpScopeWriteRequest: request }),
        );
        scopeId = created.id!;
        // Remember the id before the options are saved: if one fails, a retry must PUT this scope.
        this.savedScopeId.set(scopeId);
      }
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this scope.'));
      this.submitting.set(false);
      return;
    }

    try {
      await this.reconcileOptions(scopeId, optionRequests);
    } catch (error) {
      this.errorMessage.set(error instanceof Error ? error.message : 'An option failed to save.');
      this.submitting.set(false);
      return;
    }

    try {
      this.notification.success(isCreate ? 'Scope created.' : 'Scope saved.');
      await this.router.navigate(['/dhcp-scopes']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this scope.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method returns a row's catalog entry, or `undefined` for a custom or unknown tag. */
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
      // Without the zones (e.g. no zones:read) the zone select is empty; the rest of the form
      // works, and the backend rejects an unknown dnsZoneId.
    }
  }

  private async loadOptionCatalog(protocolVersion: InternetProtocolVersionEnum): Promise<void> {
    try {
      const entries = await firstValueFrom(
        this.optionCatalogApi.apiDhcpOptionCatalogGet({ protocolVersion }),
      );
      this.optionCatalog.set(entries);
    } catch {
      // Without the catalog every row can only be a custom tag with a raw hex value.
      this.optionCatalog.set([]);
    } finally {
      this.recomputeOptionRowViewState();
    }
  }

  /** This method rebuilds {@link optionRowViewState} from the current rows and catalog. */
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

  /** This method builds an option row's `FormGroup` from a loaded option, or an empty custom-tag row. */
  private buildOptionRowGroup(
    existing: DhcpScopeCustomOptionDetail | null,
  ): DhcpScopeOptionRowFormGroup {
    const optionTag = existing?.optionTag ?? 1;
    const valueSource = existing?.valueSource ?? DhcpOptionValueSourceEnum.Custom;

    // A loaded row selects the catalog entry for its tag, or the custom choice when the catalog
    // has none. loadExistingScope() waits for the catalog before building rows, so it is here.
    const matchingEntry = existing
      ? this.optionCatalog().find((entry) => entry.code === optionTag)
      : undefined;
    let displayValue = '';
    let storedValueNote: string | null = null;
    let catalogSelection = matchingEntry ? String(matchingEntry.code) : CUSTOM_TAG_SELECTION;
    if (existing && valueSource === DhcpOptionValueSourceEnum.Custom) {
      try {
        displayValue = decodeDhcpOptionValue(
          matchingEntry?.valueType ?? null,
          existing.valueHex ?? '',
        );
      } catch {
        // The stored bytes do not fit the catalog type (the server accepted them before the
        // catalog knew the option, or by another route). A typed editor cannot show them and
        // would fail to encode them on every save, so the row becomes a custom row whose raw hex
        // editor writes the same bytes back. The note tells the operator why.
        displayValue = existing.valueHex ?? '';
        storedValueNote = matchingEntry?.name ?? null;
        catalogSelection = CUSTOM_TAG_SELECTION;
      }
    }

    return this.formBuilder.group({
      id: [existing?.id ?? (null as string | null)],
      catalogSelection: [catalogSelection],
      optionTag: [optionTag, [Validators.required, this.optionTagValidator]],
      valueSource: [valueSource],
      value: [displayValue],
      isDisabled: [existing?.isDisabled ?? false],
      comments: [existing?.comments ?? ''],
      storedValueNote: [storedValueNote],
    });
  }

  /**
   * This method turns the option rows into writes. It throws a message naming the rows when two
   * share a tag (checked first, so no call is made) or naming the row whose value fails to encode.
   */
  private buildOptionWriteRequests(): DhcpScopeOptionWrite[] {
    const duplicate = findDuplicateOptionTag(
      this.options.controls.map((row) => row.getRawValue().optionTag),
    );
    if (duplicate) {
      throw new Error(
        `Option rows ${duplicate.firstRow} and ${duplicate.secondRow} both use tag ${duplicate.tag}; each tag can appear once.`,
      );
    }

    return this.options.controls.map((row, index) => {
      const raw = row.getRawValue();
      const entry = this.catalogEntryFor(row);
      const isServerAddress = raw.valueSource === DhcpOptionValueSourceEnum.ServerAddress;

      let valueHex = '';
      if (!isServerAddress) {
        try {
          valueHex = encodeDhcpOptionValue(entry?.valueType ?? null, raw.value);
          if (this.selectedProtocolVersion() === InternetProtocolVersionEnum.Ipv4) {
            assertDhcpv4OptionValueLength(valueHex);
          } else {
            assertDhcpv6OptionValueLength(valueHex);
          }
        } catch (error) {
          const reason = error instanceof Error ? error.message : String(error);
          throw new Error(`Option row ${index + 1} (tag ${raw.optionTag}): ${reason}`, {
            cause: error,
          });
        }
      }

      return {
        row,
        rowNumber: index + 1,
        id: raw.id,
        request: {
          optionTag: raw.optionTag,
          valueHex,
          isDisabled: raw.isDisabled,
          valueSource: raw.valueSource,
          comments: raw.comments || null,
        },
      };
    });
  }

  /**
   * This method writes the option rows in the order deletes, updates, creates. The order matters
   * because the server keeps one option per `(scope, tag)`: a row removed and a new row with the
   * same tag in one save must have the old one deleted before the new one is posted.
   *
   * <p>The calls are not atomic, so progress is kept on the form after each success. A created
   * option's id goes into its row, which turns the retry's call for that row into a `PUT`, and a
   * deleted id leaves {@link originallyLoadedOptionIds}, so a retry repeats only the calls that
   * did not succeed.
   *
   * @throws Error naming the failing row (or the removal) and saying the scope was saved.
   */
  private async reconcileOptions(scopeId: string, writes: DhcpScopeOptionWrite[]): Promise<void> {
    const plan = planDhcpOptionReconciliation(writes, this.originallyLoadedOptionIds);

    for (const id of plan.toDeleteIds) {
      try {
        await firstValueFrom(
          this.customOptionsApi.apiDhcpScopesScopeIdCustomOptionsIdDelete({ scopeId, id }),
        );
      } catch (error) {
        throw new Error(
          `The scope was saved, but removing an option failed: ${extractErrorMessage(error, 'The server refused the request.')} Save again to retry.`,
          { cause: error },
        );
      }
      this.originallyLoadedOptionIds = this.originallyLoadedOptionIds.filter(
        (loadedId) => loadedId !== id,
      );
    }

    for (const write of plan.toUpdate) {
      try {
        await firstValueFrom(
          this.customOptionsApi.apiDhcpScopesScopeIdCustomOptionsIdPut({
            scopeId,
            id: write.id!,
            dhcpScopeCustomOptionWriteRequest: write.request,
          }),
        );
      } catch (error) {
        throw this.optionRowFailure(write, error);
      }
    }

    for (const write of plan.toCreate) {
      try {
        const created = await firstValueFrom(
          this.customOptionsApi.apiDhcpScopesScopeIdCustomOptionsPost({
            scopeId,
            dhcpScopeCustomOptionWriteRequest: write.request,
          }),
        );
        if (created.id) {
          write.row.controls.id.setValue(created.id);
          this.originallyLoadedOptionIds.push(created.id);
        }
      } catch (error) {
        throw this.optionRowFailure(write, error);
      }
    }
  }

  /** This method builds the error for a failed option row call, naming the row and its tag. */
  private optionRowFailure(write: DhcpScopeOptionWrite, error: unknown): Error {
    return new Error(
      `The scope was saved, but option row ${write.rowNumber} (tag ${write.request.optionTag}) was not: ${extractErrorMessage(error, 'The server refused the request.')} Correct the row and save again.`,
      { cause: error },
    );
  }

  private async loadExistingScope(): Promise<void> {
    if (!this.routeScopeId) return;

    try {
      const scope = await firstValueFrom(
        this.scopesApi.apiDhcpScopesIdGet({ id: this.routeScopeId }),
      );

      // Keep this key order: the protocolVersion and kind subscriptions (constructor) can reset
      // kind and delegatedPrefixLength, so the loaded values must be applied after them.
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

      // Load the catalog for the scope's protocol before building rows, so each row can match
      // its catalog entry.
      await this.loadOptionCatalog(scope.protocolVersion ?? InternetProtocolVersionEnum.Ipv4);

      const optionsPage = await firstValueFrom(
        this.customOptionsApi.apiDhcpScopesScopeIdCustomOptionsGet({
          scopeId: this.routeScopeId,
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

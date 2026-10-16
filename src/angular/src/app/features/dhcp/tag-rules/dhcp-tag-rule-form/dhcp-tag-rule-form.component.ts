import { Component, OnInit, computed, inject, signal } from '@angular/core';
import {
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
  Dhcpv4TagMatchModeEnum,
  DhcpOptionCatalogEntryDetail,
  DhcpOptionCatalogService,
  DhcpOptionValueSourceEnum,
  DhcpOptionValueTypeEnum,
  DhcpScopeDetail,
  DhcpScopesService,
  DhcpTagRuleOptionDetail,
  DhcpTagRuleOptionWriteRequest,
  DhcpTagRuleOptionsService,
  DhcpTagRuleWriteRequest,
  DhcpTagRulesService,
  InternetProtocolVersionEnum,
} from '../../../../api-client';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { planDhcpOptionReconciliation } from '../../shared/dhcp-option-reconcile.util';
import {
  assertDhcpv4OptionValueLength,
  assertDhcpv6OptionValueLength,
  dhcpv4OptionHint,
  dhcpv4OptionTagValidator,
  dhcpv6OptionCodeValidator,
} from '../../shared/dhcp-option-rules.util';
import { decodeDhcpOptionValue, encodeDhcpOptionValue } from '../../shared/dhcp-option-value.util';

/**
 * This interface is one entry of {@link DhcpTagRuleFormComponent.combinedCatalog}. A tag rule
 * has no protocol field; its protocol follows its scope. The form loads both catalogs and offers
 * the ones of the rule's protocol ({@link DhcpTagRuleFormComponent.candidateCatalog}), since the
 * same tag number can mean different options in the two.
 */
interface CombinedCatalogEntry {
  protocolVersion: InternetProtocolVersionEnum;
  entry: DhcpOptionCatalogEntryDetail;
  /** The dropdown value, `"{protocolVersion}:{code}"`, unique across both catalogs even where tag numbers collide. */
  selectionValue: string;
}

/** This type names the typed shape of one option row, like the scope form's row but without `isDisabled` and `comments`, which tag rule options do not have. */
type DhcpTagRuleOptionRowFormGroup = FormGroup<{
  id: FormControl<string | null>;
  catalogSelection: FormControl<string>;
  optionTag: FormControl<number>;
  valueSource: FormControl<DhcpOptionValueSourceEnum>;
  value: FormControl<string>;
}>;

/** The `catalogSelection` value for a tag typed by hand, as on the scope form. */
const CUSTOM_TAG_SELECTION = 'custom';

/** The display state of one option row, rebuilt on every relevant change, as on the scope form. */
interface DhcpOptionRowViewState {
  valueType: DhcpOptionValueTypeEnum | null;
  isServerAddressEligible: boolean;
}

/**
 * This component is the create and edit page for a DHCP tag rule (`/dhcp-tag-rules/new` and
 * `/dhcp-tag-rules/:tagRuleId/edit`, `dhcptagrules:write`); edit mode is chosen by the
 * `tagRuleId` route parameter. A tag rule matches clients (by vendor class, user class or relay
 * agent fields, optionally within one scope) and adds DHCP options for them. It saves with
 * `POST /api/dhcp-tag-rules` or `PUT /api/dhcp-tag-rules/{id}`.
 *
 * <p>The rule's options are a table on this form, working like the scope form's table
 * (`DhcpScopeFormComponent`) but drawing on both protocols' catalogs and without per-row
 * `isDisabled` and `comments`; the rule's `isDisabled` switches them all. On save the rows are
 * written one call at a time through `/api/dhcp-tag-rules/{id}/options`
 * ({@link planDhcpOptionReconciliation}).
 *
 * <p>A rule belongs to one protocol (the server enforces it): a global rule and a rule on an IPv4
 * scope are DHCPv4 rules, a rule on an IPv6 scope is a DHCPv6 rule ({@link ruleProtocol}). The
 * protocol decides the offered catalog, the option code limits, and which match fields show:
 * `relayInterfaceIdHex` and `vendorClassEnterpriseNumber` for DHCPv6, `relayAgentCircuitIdHex`
 * for DHCPv4. Changing the scope clears the criteria that no longer apply and says so
 * ({@link clearedCriteriaHint}), and turns the typed option rows of the other protocol into
 * custom rows that keep their bytes.
 */
@Component({
  selector: 'app-dhcp-tag-rule-form',
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
  templateUrl: './dhcp-tag-rule-form.component.html',
  styleUrl: './dhcp-tag-rule-form.component.scss',
})
export class DhcpTagRuleFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly tagRulesApi = inject(DhcpTagRulesService);
  private readonly scopesApi = inject(DhcpScopesService);
  private readonly optionCatalogApi = inject(DhcpOptionCatalogService);
  private readonly tagRuleOptionsApi = inject(DhcpTagRuleOptionsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly tagRuleId = this.route.snapshot.paramMap.get('tagRuleId');

  /** Whether this instance is editing an existing tag rule rather than creating a new one. */
  readonly isEditMode = this.tagRuleId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** The scopes offered for the rule's optional scope restriction. */
  readonly scopes = signal<DhcpScopeDetail[]>([]);

  /** The DHCPv4 and DHCPv6 option catalogs merged; see {@link CombinedCatalogEntry}. */
  readonly combinedCatalog = signal<CombinedCatalogEntry[]>([]);

  /** The selected `scopeId` as a signal (the form's `valueChanges` is not one). */
  private readonly selectedScopeId = signal('');

  /** Resolves when the scope list has loaded, so an existing rule's protocol is known before its option rows are built. */
  private scopesLoaded: Promise<void> = Promise.resolve();

  /**
   * The rule's protocol: the scope's protocol version, or IPv4 for a global rule (and while the
   * scope is unknown).
   */
  readonly ruleProtocol = computed(
    () =>
      this.scopes().find((scope) => scope.id === this.selectedScopeId())?.protocolVersion ??
      InternetProtocolVersionEnum.Ipv4,
  );

  /** Whether the rule is a DHCPv6 rule. */
  readonly isDhcpv6Rule = computed(() => this.ruleProtocol() === InternetProtocolVersionEnum.Ipv6);

  /** The catalog entries of the rule's protocol, the options the dropdown offers. */
  readonly candidateCatalog = computed(() =>
    this.combinedCatalog().filter((entry) => entry.protocolVersion === this.ruleProtocol()),
  );

  /** A note shown after a scope change cleared match fields that the new protocol cannot evaluate, or null. */
  readonly clearedCriteriaHint = signal<string | null>(null);

  /** The ids of the option rows as loaded, for {@link planDhcpOptionReconciliation}. */
  private originallyLoadedOptionIds: string[] = [];

  /** One {@link DhcpOptionRowViewState} per option row. */
  readonly optionRowViewState = signal<DhcpOptionRowViewState[]>([]);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    scopeId: [''],
    priority: [0, [Validators.required]],
    isDisabled: [false],
    vendorClassIdentifierMatch: [''],
    vendorClassIdentifierMatchMode: [Dhcpv4TagMatchModeEnum.Exact as Dhcpv4TagMatchModeEnum],
    vendorClassEnterpriseNumber: [null as number | null],
    userClassMatch: [''],
    userClassMatchMode: [Dhcpv4TagMatchModeEnum.Exact as Dhcpv4TagMatchModeEnum],
    relayAgentCircuitIdHex: [''],
    relayAgentRemoteIdHex: [''],
    relayInterfaceIdHex: [''],
    comments: [''],
    options: this.formBuilder.array<DhcpTagRuleOptionRowFormGroup>([]),
  });

  /** The option rows' `FormArray`, for the template. */
  get options() {
    return this.form.controls.options;
  }

  constructor() {
    this.form.controls.scopeId.valueChanges.subscribe((scopeId) => {
      const previousProtocol = this.ruleProtocol();
      this.selectedScopeId.set(scopeId);
      this.applyProtocolChange(previousProtocol);
    });
  }

  ngOnInit(): void {
    this.scopesLoaded = this.loadScopes();
    void this.loadCombinedCatalog();
    if (this.isEditMode) void this.loadExistingRule();
  }

  /**
   * This method clears the match fields the new protocol cannot evaluate (the server rejects
   * them), converts typed option rows of the old protocol to custom rows, and re-checks the
   * option codes against the new protocol's limits.
   */
  private applyProtocolChange(previousProtocol: InternetProtocolVersionEnum): void {
    const protocol = this.ruleProtocol();
    if (protocol === previousProtocol) return;

    const cleared: string[] = [];
    const controls = this.form.controls;

    if (protocol === InternetProtocolVersionEnum.Ipv6) {
      if (controls.relayAgentCircuitIdHex.value !== '') {
        controls.relayAgentCircuitIdHex.setValue('');
        cleared.push('Relay Agent Circuit ID');
      }
    } else {
      if (controls.relayInterfaceIdHex.value !== '') {
        controls.relayInterfaceIdHex.setValue('');
        cleared.push('Relay Interface ID');
      }
      if (controls.vendorClassEnterpriseNumber.value !== null) {
        controls.vendorClassEnterpriseNumber.setValue(null);
        cleared.push('Vendor Class enterprise number');
      }
    }

    this.clearedCriteriaHint.set(
      cleared.length === 0
        ? null
        : `${cleared.join(' and ')} cleared: the ${protocol === InternetProtocolVersionEnum.Ipv6 ? 'DHCPv6' : 'DHCPv4'} rule cannot match on it.`,
    );

    for (const row of this.options.controls) {
      const combined = this.catalogEntryFor(row);

      if (combined && combined.protocolVersion !== protocol) {
        // Keep the typed value's bytes as hex so nothing the operator entered is lost.
        let hex = '';
        if (row.controls.valueSource.value !== DhcpOptionValueSourceEnum.ServerAddress) {
          try {
            hex = encodeDhcpOptionValue(combined.entry.valueType ?? null, row.controls.value.value);
          } catch {
            hex = row.controls.value.value;
          }
        }
        row.controls.catalogSelection.setValue(CUSTOM_TAG_SELECTION);
        row.controls.valueSource.setValue(DhcpOptionValueSourceEnum.Custom);
        row.controls.value.setValue(hex);
      }

      row.controls.optionTag.updateValueAndValidity();
    }

    this.recomputeOptionRowViewState();
  }

  /** This method returns the hint for an option row's tag (DHCPv4 rules only), or null. */
  optionHint(tag: number | null): string | null {
    return this.isDhcpv6Rule() ? null : dhcpv4OptionHint(tag);
  }

  /** This validator applies the rule's protocol limits to an option row's tag or code. */
  private readonly optionTagValidator: ValidatorFn = (control) =>
    this.isDhcpv6Rule() ? dhcpv6OptionCodeValidator(control) : dhcpv4OptionTagValidator(control);

  /** This method adds an empty option row, starting as a custom tag. */
  addOptionRow(): void {
    this.options.push(this.buildOptionRowGroup(null));
    this.recomputeOptionRowViewState();
  }

  /** This method removes an option row; a saved row is deleted on the server when the rule is saved. */
  removeOptionRow(index: number): void {
    this.options.removeAt(index);
    this.recomputeOptionRowViewState();
  }

  /** This method runs when a row's catalog dropdown changes, as on the scope form (`DhcpScopeFormComponent.onOptionCatalogSelectionChange`). */
  onOptionCatalogSelectionChange(index: number): void {
    const row = this.options.at(index);
    const selection = row.controls.catalogSelection.value;

    if (selection !== CUSTOM_TAG_SELECTION) {
      const matched = this.candidateCatalog().find(
        (candidate) => candidate.selectionValue === selection,
      );
      if (matched) row.controls.optionTag.setValue(matched.entry.code ?? 0);
    }

    row.controls.value.setValue('');
    row.controls.valueSource.setValue(DhcpOptionValueSourceEnum.Custom);
    this.recomputeOptionRowViewState();
  }

  /** This method creates the rule (`POST`) or replaces it (`PUT`), then saves the option rows one call at a time. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    let optionRequests: { id: string | null; request: DhcpTagRuleOptionWriteRequest }[];

    try {
      optionRequests = this.buildOptionWriteRequests();
    } catch (error) {
      this.errorMessage.set(
        error instanceof Error ? error.message : 'One of the contributed option values is invalid.',
      );
      this.submitting.set(false);
      return;
    }

    const value = this.form.getRawValue();
    const request: DhcpTagRuleWriteRequest = {
      name: value.name,
      scopeId: value.scopeId || null,
      priority: value.priority,
      isDisabled: value.isDisabled,
      vendorClassIdentifierMatch: value.vendorClassIdentifierMatch || null,
      vendorClassIdentifierMatchMode: value.vendorClassIdentifierMatchMode,
      vendorClassEnterpriseNumber: value.vendorClassEnterpriseNumber ?? null,
      userClassMatch: value.userClassMatch || null,
      userClassMatchMode: value.userClassMatchMode,
      relayAgentCircuitIdHex: value.relayAgentCircuitIdHex || null,
      relayAgentRemoteIdHex: value.relayAgentRemoteIdHex || null,
      relayInterfaceIdHex: value.relayInterfaceIdHex || null,
      comments: value.comments || null,
    };

    try {
      let resolvedTagRuleId: string;

      if (this.isEditMode && this.tagRuleId) {
        await firstValueFrom(
          this.tagRulesApi.apiDhcpTagRulesIdPut({
            id: this.tagRuleId,
            dhcpTagRuleWriteRequest: request,
          }),
        );
        resolvedTagRuleId = this.tagRuleId;
      } else {
        const created = await firstValueFrom(
          this.tagRulesApi.apiDhcpTagRulesPost({ dhcpTagRuleWriteRequest: request }),
        );
        resolvedTagRuleId = created.id!;
      }

      await this.reconcileOptions(resolvedTagRuleId, optionRequests);

      this.notification.success(this.isEditMode ? 'Rule saved.' : 'Rule created.');
      await this.router.navigate(['/dhcp-tag-rules']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this rule.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method returns a row's catalog entry, or `undefined` for a custom or unknown tag. */
  catalogEntryFor(row: DhcpTagRuleOptionRowFormGroup): CombinedCatalogEntry | undefined {
    const selection = row.controls.catalogSelection.value;
    if (selection === CUSTOM_TAG_SELECTION) return undefined;
    return this.combinedCatalog().find((candidate) => candidate.selectionValue === selection);
  }

  private async loadScopes(): Promise<void> {
    try {
      const page = await firstValueFrom(this.scopesApi.apiDhcpScopesGet({ skip: 0, take: 500 }));
      this.scopes.set(page.items ?? []);
    } catch {
      // Without the scopes the restriction select is empty; the rest of the form works, and the
      // backend rejects an unknown scopeId.
    }
  }

  private async loadCombinedCatalog(): Promise<void> {
    try {
      const [ipv4Entries, ipv6Entries] = await Promise.all([
        firstValueFrom(
          this.optionCatalogApi.apiDhcpOptionCatalogGet({
            protocolVersion: InternetProtocolVersionEnum.Ipv4,
          }),
        ),
        firstValueFrom(
          this.optionCatalogApi.apiDhcpOptionCatalogGet({
            protocolVersion: InternetProtocolVersionEnum.Ipv6,
          }),
        ),
      ]);

      this.combinedCatalog.set([
        ...ipv4Entries.map((entry) =>
          this.toCombinedEntry(InternetProtocolVersionEnum.Ipv4, entry),
        ),
        ...ipv6Entries.map((entry) =>
          this.toCombinedEntry(InternetProtocolVersionEnum.Ipv6, entry),
        ),
      ]);
    } catch {
      // Without the catalog every row can only be a custom tag with a raw hex value.
      this.combinedCatalog.set([]);
    } finally {
      this.recomputeOptionRowViewState();
    }
  }

  private toCombinedEntry(
    protocolVersion: InternetProtocolVersionEnum,
    entry: DhcpOptionCatalogEntryDetail,
  ): CombinedCatalogEntry {
    return { protocolVersion, entry, selectionValue: `${protocolVersion}:${entry.code}` };
  }

  private recomputeOptionRowViewState(): void {
    this.optionRowViewState.set(
      this.options.controls.map((row) => {
        const combined = this.catalogEntryFor(row);
        return {
          valueType: combined?.entry.valueType ?? null,
          isServerAddressEligible: combined?.entry.isServerAddressEligible ?? false,
        };
      }),
    );
  }

  /**
   * This method builds an option row's `FormGroup`. A stored option has no protocol, and the same
   * tag can name different options in the two catalogs (DHCPv4 23 is Default IP Time-to-live,
   * DHCPv6 23 is DNS Recursive Name Server). The choice decides the value editor, so when several
   * catalog entries share the tag, the row takes the first whose type decodes the stored value. A
   * fixed-size type (an address, an integer) settles it; `String` and `OpaqueHex` accept any
   * bytes, so between those (DHCPv4 17 Root Path and DHCPv6 17 Vendor-specific Information) the
   * first entry wins.
   */
  private buildOptionRowGroup(
    existing: DhcpTagRuleOptionDetail | null,
  ): DhcpTagRuleOptionRowFormGroup {
    const optionTag = existing?.optionTag ?? 1;
    const valueSource = existing?.valueSource ?? DhcpOptionValueSourceEnum.Custom;

    const candidates = existing
      ? this.candidateCatalog().filter((candidate) => candidate.entry.code === optionTag)
      : [];

    let matchingEntry: CombinedCatalogEntry | undefined;
    let displayValue = '';

    if (existing && valueSource === DhcpOptionValueSourceEnum.Custom && candidates.length > 0) {
      const decodable = candidates
        .map((candidate) => {
          try {
            return {
              candidate,
              decoded: decodeDhcpOptionValue(
                candidate.entry.valueType ?? null,
                existing.valueHex ?? '',
              ),
            };
          } catch {
            return null;
          }
        })
        .find((result) => result !== null);

      if (decodable) {
        matchingEntry = decodable.candidate;
        displayValue = decodable.decoded;
      } else {
        // No entry's type fits the stored bytes: keep the raw hex as the value rather than lose
        // it. The row still selects the first entry, so its typed editor shows the hex.
        matchingEntry = candidates[0];
        displayValue = existing.valueHex ?? '';
      }
    } else {
      matchingEntry = candidates[0];
    }

    const catalogSelection = matchingEntry ? matchingEntry.selectionValue : CUSTOM_TAG_SELECTION;

    return this.formBuilder.group({
      id: [existing?.id ?? (null as string | null)],
      catalogSelection: [catalogSelection],
      optionTag: [optionTag, [Validators.required, this.optionTagValidator]],
      valueSource: [valueSource],
      value: [displayValue],
    });
  }

  private buildOptionWriteRequests(): {
    id: string | null;
    request: DhcpTagRuleOptionWriteRequest;
  }[] {
    return this.options.controls.map((row, index) => {
      const raw = row.getRawValue();
      const combined = this.catalogEntryFor(row);
      const isServerAddress = raw.valueSource === DhcpOptionValueSourceEnum.ServerAddress;

      let valueHex = '';
      if (!isServerAddress) {
        try {
          valueHex = encodeDhcpOptionValue(combined?.entry.valueType ?? null, raw.value);
          if (this.isDhcpv6Rule()) assertDhcpv6OptionValueLength(valueHex);
          else assertDhcpv4OptionValueLength(valueHex);
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
          valueSource: raw.valueSource,
        },
      };
    });
  }

  private async reconcileOptions(
    tagRuleId: string,
    optionRequests: { id: string | null; request: DhcpTagRuleOptionWriteRequest }[],
  ): Promise<void> {
    const plan = planDhcpOptionReconciliation(
      optionRequests.map((entry) => ({ id: entry.id, request: entry.request })),
      this.originallyLoadedOptionIds,
    );

    for (const entry of plan.toCreate) {
      await firstValueFrom(
        this.tagRuleOptionsApi.apiDhcpTagRulesTagRuleIdOptionsPost({
          tagRuleId,
          dhcpTagRuleOptionWriteRequest: entry.request,
        }),
      );
    }

    for (const entry of plan.toUpdate) {
      await firstValueFrom(
        this.tagRuleOptionsApi.apiDhcpTagRulesTagRuleIdOptionsIdPut({
          tagRuleId,
          id: entry.id!,
          dhcpTagRuleOptionWriteRequest: entry.request,
        }),
      );
    }

    for (const id of plan.toDeleteIds) {
      await firstValueFrom(
        this.tagRuleOptionsApi.apiDhcpTagRulesTagRuleIdOptionsIdDelete({ tagRuleId, id }),
      );
    }
  }

  private async loadExistingRule(): Promise<void> {
    if (!this.tagRuleId) return;

    try {
      const rule = await firstValueFrom(
        this.tagRulesApi.apiDhcpTagRulesIdGet({ id: this.tagRuleId }),
      );

      // The rule's protocol (from its scope) decides which catalog entries its rows can match.
      await this.scopesLoaded;

      this.form.patchValue({
        name: rule.name ?? '',
        scopeId: rule.scopeId ?? '',
        priority: rule.priority ?? 0,
        isDisabled: rule.isDisabled ?? false,
        vendorClassIdentifierMatch: rule.vendorClassIdentifierMatch ?? '',
        vendorClassIdentifierMatchMode:
          rule.vendorClassIdentifierMatchMode ?? Dhcpv4TagMatchModeEnum.Exact,
        vendorClassEnterpriseNumber: rule.vendorClassEnterpriseNumber ?? null,
        userClassMatch: rule.userClassMatch ?? '',
        userClassMatchMode: rule.userClassMatchMode ?? Dhcpv4TagMatchModeEnum.Exact,
        relayAgentCircuitIdHex: rule.relayAgentCircuitIdHex ?? '',
        relayAgentRemoteIdHex: rule.relayAgentRemoteIdHex ?? '',
        relayInterfaceIdHex: rule.relayInterfaceIdHex ?? '',
        comments: rule.comments ?? '',
      });

      // Load the catalog before building rows, so each row can match its catalog entry.
      await this.loadCombinedCatalog();

      const optionsPage = await firstValueFrom(
        this.tagRuleOptionsApi.apiDhcpTagRulesTagRuleIdOptionsGet({
          tagRuleId: this.tagRuleId,
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
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this rule.'));
    } finally {
      this.loading.set(false);
    }
  }
}

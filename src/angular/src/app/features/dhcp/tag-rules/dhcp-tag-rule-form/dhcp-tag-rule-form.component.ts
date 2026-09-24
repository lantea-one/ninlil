import { Component, OnInit, inject, signal } from '@angular/core';
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
import { decodeDhcpOptionValue, encodeDhcpOptionValue } from '../../shared/dhcp-option-value.util';

/**
 * This interface is one merged catalog entry {@link DhcpTagRuleFormComponent.combinedCatalog}
 * carries -- a tag rule has no `protocolVersion` of its own (unlike a scope), since a rule can
 * apply regardless of which protocol a matching client actually used, so its own options table
 * dropdown shows both DHCPv4 and DHCPv6's own well-known options together, each labeled with its
 * own protocol so a same-numbered-but-different-meaning tag across the two protocols is never
 * ambiguous.
 */
interface CombinedCatalogEntry {
  protocolVersion: InternetProtocolVersionEnum;
  entry: DhcpOptionCatalogEntryDetail;
  /** This entry's own dropdown value -- `"{protocolVersion}:{code}"`, unique across both protocols' catalogs even where their raw tag/code numbers happen to collide. */
  selectionValue: string;
}

/** This type alias names one contributed-option table row's own typed shape explicitly -- the tag-rule analogue of `DhcpScopeFormComponent`'s own `DhcpScopeOptionRowFormGroup`, minus the `isDisabled`/`comments` fields a tag-rule option has no backend concept of at all. */
type DhcpTagRuleOptionRowFormGroup = FormGroup<{
  id: FormControl<string | null>;
  catalogSelection: FormControl<string>;
  optionTag: FormControl<number>;
  valueSource: FormControl<DhcpOptionValueSourceEnum>;
  value: FormControl<string>;
}>;

/** The literal `catalogSelection` value meaning "a raw, uncataloged tag" rather than a real catalog entry -- matches `DhcpScopeFormComponent`'s own sentinel. */
const CUSTOM_TAG_SELECTION = 'custom';

/** The small, per-row derived display state this component recomputes on every change that could invalidate it -- the tag-rule analogue of `DhcpScopeFormComponent`'s own `DhcpOptionRowViewState`. */
interface DhcpOptionRowViewState {
  valueType: DhcpOptionValueTypeEnum | null;
  isServerAddressEligible: boolean;
}

/**
 * This component is both the "create a tag rule" and "edit a tag rule" page -- one component for
 * both, distinguished by whether the route supplies a `tagRuleId` (see `app.routes.ts`). A rule's
 * own contributed options are now managed inline, right here, as a dynamic table -- the same
 * catalog-driven option dropdown plus type-adaptive value editor `DhcpScopeFormComponent`'s own
 * doc comment describes, minus that form's own `isDisabled`/`comments` per-row fields, since
 * `DhcpTagRuleOptionEntity` has no backend concept of either (the owning rule's own `isDisabled`
 * flag is this table's only real on/off switch). Saving reconciles this table's current rows
 * against `DhcpTagRuleOptionsController`'s own real per-row CRUD endpoints via
 * {@link planDhcpOptionReconciliation}, exactly like the scope form's own table.
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

  /** Every DHCP scope this rule's own optional scope restriction can be pinned to. */
  readonly scopes = signal<DhcpScopeDetail[]>([]);

  /** Both protocols' own well-known DHCP option catalogs, merged -- see this class's own doc comment for why a tag rule's options table isn't narrowed to one protocol. */
  readonly combinedCatalog = signal<CombinedCatalogEntry[]>([]);

  /** Every contributed-option row's own id as it stood right after this table was last loaded -- see {@link planDhcpOptionReconciliation}. */
  private originallyLoadedOptionIds: string[] = [];

  /** One {@link DhcpOptionRowViewState} per {@link options} row -- see `DhcpScopeFormComponent`'s own identically-named signal for why this is a full recompute rather than an incremental patch. */
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

  /** {@link options}'s own `FormArray`, exposed for the template. */
  get options() {
    return this.form.controls.options;
  }

  ngOnInit(): void {
    void this.loadScopes();
    void this.loadCombinedCatalog();
    if (this.isEditMode) void this.loadExistingRule();
  }

  /** This method appends one new, empty contributed-option row, defaulting to a raw/custom tag exactly like this table's original standalone add page did. */
  addOptionRow(): void {
    this.options.push(this.buildOptionRowGroup(null));
    this.recomputeOptionRowViewState();
  }

  /** This method removes one contributed-option row -- its own `DELETE` (if it was ever persisted) is issued only when the whole rule is saved. */
  removeOptionRow(index: number): void {
    this.options.removeAt(index);
    this.recomputeOptionRowViewState();
  }

  /** This method runs when one row's own catalog dropdown changes -- the tag-rule analogue of `DhcpScopeFormComponent.onOptionCatalogSelectionChange`. */
  onOptionCatalogSelectionChange(index: number): void {
    const row = this.options.at(index);
    const selection = row.controls.catalogSelection.value;

    if (selection !== CUSTOM_TAG_SELECTION) {
      const matched = this.combinedCatalog().find(
        (candidate) => candidate.selectionValue === selection,
      );
      if (matched) row.controls.optionTag.setValue(matched.entry.code ?? 0);
    }

    row.controls.value.setValue('');
    row.controls.valueSource.setValue(DhcpOptionValueSourceEnum.Custom);
    this.recomputeOptionRowViewState();
  }

  /** This method creates (`POST`) or fully replaces (`PUT`) the rule, then reconciles the options table's own rows. */
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

  /** This method looks up {@link combinedCatalog}'s own entry for one row, or `undefined` for {@link CUSTOM_TAG_SELECTION}/an unmatched tag. */
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
      // A failed scope lookup only narrows this form's own optional scope-restriction dropdown --
      // the rest of the form remains usable, and a genuinely invalid scopeId is still rejected
      // server-side.
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
      // A failed catalog fetch only narrows the options table's own dropdown to the "Custom /
      // other option..." choice for every row -- raw tag/hex entry still works.
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
   * This method builds one option row's own `FormGroup`. A loaded row's own `catalogSelection`
   * resolves against {@link combinedCatalog}'s own entries matching its `optionTag` -- since a
   * rule's own contributed option carries no protocol of its own either, the same raw tag number
   * can genuinely name two different options across the two catalogs (confirmed live: DHCPv4
   * option 23, Default IP Time-to-live, collides with DHCPv6 option 23, DNS Recursive Name
   * Server). Picking the wrong one isn't merely cosmetic here -- it drives which value-type editor
   * {@link decodeDhcpOptionValue} renders, so blindly preferring whichever catalog was fetched
   * first (DHCPv4) previously produced a genuinely broken display (a 16-byte IPv6 address decoded
   * as a 1-byte UInt8, rendering an empty, invalid numeric spinbutton). When more than one
   * candidate shares this row's own `optionTag`, this method now prefers whichever one's own
   * value type actually decodes the stored `valueHex` without throwing (a strict, fixed/aligned
   * byte-length type like an address or a fixed-width integer disambiguates this cleanly); when
   * more than one candidate's type is lenient enough to "succeed" trivially (both `String` and
   * `OpaqueHex` decode any byte sequence without throwing -- DHCPv4 option 17/Root Path vs DHCPv6
   * option 17/Vendor-specific Information is exactly this case), the first match still wins, an
   * accepted, narrower residual ambiguity given a tag-rule option has no protocol field at all to
   * resolve it definitively.
   */
  private buildOptionRowGroup(
    existing: DhcpTagRuleOptionDetail | null,
  ): DhcpTagRuleOptionRowFormGroup {
    const optionTag = existing?.optionTag ?? 1;
    const valueSource = existing?.valueSource ?? DhcpOptionValueSourceEnum.Custom;

    const candidates = existing
      ? this.combinedCatalog().filter((candidate) => candidate.entry.code === optionTag)
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
        // No candidate's own value type fits the stored bytes at all -- fall back to plain
        // raw-hex editing for this one row, exactly like an uncataloged tag always has.
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
      optionTag: [optionTag, [Validators.required, Validators.min(0), Validators.max(255)]],
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

      // The combined catalog is fetched unconditionally at ngOnInit -- wait for it so each
      // loaded row's own catalogSelection can resolve against the real catalog rather than an
      // empty one.
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

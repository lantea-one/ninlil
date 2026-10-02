import { Component, OnInit, inject, signal } from '@angular/core';
import {
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
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
  DnssecAlgorithmEnum,
  RecursiveRootHintServerDetail,
  RecursiveSettingsService,
  RecursiveTrustAnchorDetail,
} from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { DnsCacheCardComponent } from '../dns-cache-card/dns-cache-card.component';

/** One root hint server row: a name server's name and its addresses, comma-separated. */
type RootHintServerFormGroup = FormGroup<{
  name: FormControl<string>;
  addresses: FormControl<string>;
}>;

/** One trust anchor row, the DS-style fields `TrustAnchorOption` binds. */
type TrustAnchorFormGroup = FormGroup<{
  keyTag: FormControl<number>;
  algorithm: FormControl<DnssecAlgorithmEnum>;
  digestType: FormControl<number>;
  digestHex: FormControl<string>;
}>;

/**
 * The algorithms offered for a trust anchor. The older ones with hyphenated names (DSA-NSEC3-SHA1
 * and the like) are left out: no root has used them, and the backend's parser only accepts names
 * without hyphens.
 */
const TRUST_ANCHOR_ALGORITHMS: DnssecAlgorithmEnum[] = [
  DnssecAlgorithmEnum.Rsasha1,
  DnssecAlgorithmEnum.Rsasha256,
  DnssecAlgorithmEnum.Rsasha512,
  DnssecAlgorithmEnum.Ecdsap256Sha256,
  DnssecAlgorithmEnum.Ecdsap384Sha384,
  DnssecAlgorithmEnum.Ed25519,
  DnssecAlgorithmEnum.Ed448,
];

/**
 * This component is the "Recursive Resolver" settings page (`/settings/recursive`). It edits
 * `RecursiveServerOptions` through `GET` and `PATCH /api/recursive-settings`
 * (`recursivesettings:read`, `recursivesettings:write`): recursion on or off (off on a fresh
 * install), DNSSEC validation, timeouts and limits, the cache, serve-stale, prefetch and RFC 5011
 * automatic trust anchor updates.
 *
 * <p>Every field is hot-reloaded except the root hint and trust anchor override lists, which are
 * read at startup. Those lists are edited as rows at the bottom of the form and sent only when
 * they changed, so saving another field does not produce the restart note.
 *
 * <p>Below the form, {@link DnsCacheCardComponent} shows the forwarded and recursive answer caches
 * and can flush them; it uses this page's permissions.
 */
@Component({
  selector: 'app-recursive-settings',
  imports: [
    DnsCacheCardComponent,
    IfPermissionDirective,
    ReactiveFormsModule,
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
  templateUrl: './recursive-settings.component.html',
  styleUrl: './recursive-settings.component.scss',
})
export class RecursiveSettingsComponent implements OnInit {
  private readonly recursiveApi = inject(RecursiveSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enableRecursion: [false],
    enableDnssecValidation: [true],
    enableQnameMinimization: [true],
    upstreamQueryTimeout: ['00:00:03', [Validators.required]],
    overallQueryTimeout: ['00:00:15', [Validators.required]],
    maxReferralDepth: [30, [Validators.required, Validators.min(1)]],
    maxCnameChainLength: [15, [Validators.required, Validators.min(1)]],
    maxUpstreamQueries: [100, [Validators.required, Validators.min(1)]],
    maxCacheEntries: [100_000, [Validators.required, Validators.min(1)]],
    enableAutomaticTrustAnchorUpdate: [true],
    trustAnchorRefreshInterval: ['15.00:00:00', [Validators.required]],
    trustAnchorHoldDownDuration: ['30.00:00:00', [Validators.required]],
    trustAnchorRemoveHoldDownDuration: ['30.00:00:00', [Validators.required]],
    enableServeStale: [true],
    maxStaleServeAge: ['1.00:00:00', [Validators.required]],
    enablePrefetch: [true],
    prefetchWindow: ['00:00:10', [Validators.required]],
    prefetchRecentAccessWindow: ['00:05:00', [Validators.required]],
    rootHintServers: this.formBuilder.array<RootHintServerFormGroup>([]),
    trustAnchors: this.formBuilder.array<TrustAnchorFormGroup>([]),
  });

  /** The algorithms a trust anchor row offers (see `TRUST_ANCHOR_ALGORITHMS`). */
  readonly trustAnchorAlgorithms = TRUST_ANCHOR_ALGORITHMS;

  /** The two override lists as last loaded or saved, to tell whether a save must send them. */
  private loadedOverrides = { rootHints: '[]', trustAnchors: '[]' };

  /** Every root hint server row. */
  get rootHintServers() {
    return this.form.controls.rootHintServers;
  }

  /** Every trust anchor row. */
  get trustAnchors() {
    return this.form.controls.trustAnchors;
  }

  addRootHintServer(): void {
    this.rootHintServers.push(this.buildRootHintServerGroup(null));
  }

  removeRootHintServer(index: number): void {
    this.rootHintServers.removeAt(index);
  }

  addTrustAnchor(): void {
    this.trustAnchors.push(this.buildTrustAnchorGroup(null));
  }

  removeTrustAnchor(index: number): void {
    this.trustAnchors.removeAt(index);
  }

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field (the override lists only when changed), then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { rootHintServers, trustAnchors, ...value } = this.form.getRawValue();
    const body: Record<string, unknown> = { ...value };

    // The override lists are read only at startup, so sending them unchanged would make the
    // backend report that a restart is needed when nothing that needs one was touched.
    const rootHints = toRootHintServers(rootHintServers);
    if (JSON.stringify(rootHints) !== this.loadedOverrides.rootHints) {
      body['rootHintServersOverride'] = rootHints;
    }
    const anchors = toTrustAnchors(trustAnchors);
    if (JSON.stringify(anchors) !== this.loadedOverrides.trustAnchors) {
      body['trustAnchorsOverride'] = anchors;
    }

    try {
      const result = await firstValueFrom(this.recursiveApi.apiRecursiveSettingsPatch({ body }));
      this.loadedOverrides = {
        rootHints: JSON.stringify(rootHints),
        trustAnchors: JSON.stringify(anchors),
      };
      this.notification.success(result.note ?? 'Recursive resolver settings saved.');
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not save recursive resolver settings.'),
      );
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.recursiveApi.apiRecursiveSettingsGet());
      this.form.patchValue({
        enableRecursion: settings.enableRecursion ?? false,
        enableDnssecValidation: settings.enableDnssecValidation ?? true,
        enableQnameMinimization: settings.enableQnameMinimization ?? true,
        upstreamQueryTimeout: settings.upstreamQueryTimeout ?? '00:00:03',
        overallQueryTimeout: settings.overallQueryTimeout ?? '00:00:15',
        maxReferralDepth: settings.maxReferralDepth ?? 30,
        maxCnameChainLength: settings.maxCnameChainLength ?? 15,
        maxUpstreamQueries: settings.maxUpstreamQueries ?? 100,
        maxCacheEntries: settings.maxCacheEntries ?? 100_000,
        enableAutomaticTrustAnchorUpdate: settings.enableAutomaticTrustAnchorUpdate ?? true,
        trustAnchorRefreshInterval: settings.trustAnchorRefreshInterval ?? '15.00:00:00',
        trustAnchorHoldDownDuration: settings.trustAnchorHoldDownDuration ?? '30.00:00:00',
        trustAnchorRemoveHoldDownDuration:
          settings.trustAnchorRemoveHoldDownDuration ?? '30.00:00:00',
        enableServeStale: settings.enableServeStale ?? true,
        maxStaleServeAge: settings.maxStaleServeAge ?? '1.00:00:00',
        enablePrefetch: settings.enablePrefetch ?? true,
        prefetchWindow: settings.prefetchWindow ?? '00:00:10',
        prefetchRecentAccessWindow: settings.prefetchRecentAccessWindow ?? '00:05:00',
      });

      this.rootHintServers.clear();
      for (const server of settings.rootHintServersOverride ?? []) {
        this.rootHintServers.push(this.buildRootHintServerGroup(server));
      }
      this.trustAnchors.clear();
      for (const anchor of settings.trustAnchorsOverride ?? []) {
        this.trustAnchors.push(this.buildTrustAnchorGroup(anchor));
      }
      this.loadedOverrides = {
        rootHints: JSON.stringify(toRootHintServers(this.rootHintServers.getRawValue())),
        trustAnchors: JSON.stringify(toTrustAnchors(this.trustAnchors.getRawValue())),
      };
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load recursive resolver settings.'),
      );
    } finally {
      this.loading.set(false);
    }
  }

  private buildRootHintServerGroup(
    server: RecursiveRootHintServerDetail | null,
  ): RootHintServerFormGroup {
    return this.formBuilder.group({
      name: [server?.name ?? '', [Validators.required]],
      addresses: [(server?.addresses ?? []).join(', '), [Validators.required]],
    });
  }

  private buildTrustAnchorGroup(anchor: RecursiveTrustAnchorDetail | null): TrustAnchorFormGroup {
    return this.formBuilder.group({
      keyTag: [
        anchor?.keyTag ?? 0,
        [Validators.required, Validators.min(0), Validators.max(65535)],
      ],
      algorithm: [anchor?.algorithm ?? DnssecAlgorithmEnum.Rsasha256],
      digestType: [anchor?.digestType ?? 2],
      digestHex: [
        anchor?.digestHex ?? '',
        [Validators.required, Validators.pattern(/^[0-9A-Fa-f\s]+$/)],
      ],
    });
  }
}

/** This function turns the root hint rows into the request shape, splitting each address list. */
export function toRootHintServers(
  rows: { name: string; addresses: string }[],
): RecursiveRootHintServerDetail[] {
  return rows.map((row) => ({
    name: row.name.trim(),
    addresses: row.addresses
      .split(/[\s,]+/)
      .map((address) => address.trim())
      .filter((address) => address.length > 0),
  }));
}

/** This function turns the trust anchor rows into the request shape, normalizing the digest. */
export function toTrustAnchors(
  rows: { keyTag: number; algorithm: DnssecAlgorithmEnum; digestType: number; digestHex: string }[],
): RecursiveTrustAnchorDetail[] {
  return rows.map((row) => ({
    keyTag: Number(row.keyTag),
    algorithm: row.algorithm,
    digestType: Number(row.digestType),
    digestHex: row.digestHex.replace(/\s+/g, '').toUpperCase(),
  }));
}

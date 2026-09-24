import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { RecursiveSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component reads and writes this host's own `RecursiveServerOptions` (recursion on/off,
 * DNSSEC validation, resolution timeouts and bounds, and automatic RFC 5011 trust anchor update)
 * via `GET`/`PATCH /api/recursive-settings` -- the single highest-priority gap a full REST/UI
 * configuration-completeness audit found: a fresh install starts with recursion off (the correct,
 * documented default) and, before this page existed, had no way to turn it on short of
 * hand-editing the configuration file and restarting this host's own process.
 *
 * <p>Almost every field this form edits takes effect immediately, with no restart -- the backend
 * reads `RecursiveServerOptions` live via `IOptionsMonitor` everywhere it actually matters for a
 * query already in flight. The backend's own PATCH response note (surfaced directly in the
 * success notification, never suppressed) says so explicitly, and calls out the two fields that
 * are the deliberate exception: the root-hints/trust-anchor override lists, each read once at
 * startup or first seed by design. This form deliberately does not expose those two advanced,
 * private-root-only override lists at all -- editing an array of name-server/trust-anchor objects
 * has no natural fit in this project's established flat-settings-form shape, and the feature they
 * serve (recursing against something other than the real internet root) is rare enough that a
 * direct configuration-file edit remains the appropriate path, exactly as before this page
 * existed. Every other field the backend accepts is covered here.
 */
@Component({
  selector: 'app-recursive-settings',
  imports: [
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
    FormTextDirective,
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
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field this form holds, then surfaces the backend's own hot-reload/restart-required note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.recursiveApi.apiRecursiveSettingsPatch({ body: value }),
      );
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
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load recursive resolver settings.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}

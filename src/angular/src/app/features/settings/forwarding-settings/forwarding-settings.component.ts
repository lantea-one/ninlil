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
  FormSelectDirective,
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { RouterLink } from '@angular/router';
import { ForwarderFailoverStrategyEnum, ForwardingSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component reads and writes this host's own `ForwardingOptions` -- the engine-level toggle
 * and failover/health-check tuning for upstream forwarding, via `GET`/`PATCH /api/forwarding-settings`
 * (`forwardingsettings:read`/`forwardingsettings:write`). Forwarder entries themselves (the actual
 * upstream addresses) are a separate page (Forwarders); this page covers only whether the
 * forwarding resolver consults them at query time at all, plus how it picks among multiple
 * configured forwarders, and whether (and how many) forwarded answers are cached. The cache's
 * statistics and flush controls live on the Recursive Resolver page, beside the serve-stale
 * setting forwarded answers also follow.
 *
 * <p>Every field this form edits takes effect immediately, with no restart -- `ForwarderQueryResolver`
 * is now always constructed and registered as a query resolver regardless of `Enable`'s own value
 * at process startup, reading its own live `Enable`/failover-strategy/timeout configuration fresh
 * on every query via `IOptionsMonitor`. The backend's own PATCH response note (surfaced directly in
 * the success notification, never suppressed) confirms this explicitly.
 */
@Component({
  selector: 'app-forwarding-settings',
  imports: [
    IfPermissionDirective,
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
    SpinnerComponent,
  ],
  templateUrl: './forwarding-settings.component.html',
  styleUrl: './forwarding-settings.component.scss',
})
export class ForwardingSettingsComponent implements OnInit {
  private readonly forwardingApi = inject(ForwardingSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** Every failover strategy this picker offers, mirroring the real backend enum. */
  readonly failoverStrategyOptions = [
    { value: ForwarderFailoverStrategyEnum.TryInOrder, label: 'Try in order' },
    {
      value: ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin,
      label: 'Health-checked round robin',
    },
  ];

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enable: [false],
    failoverStrategy: [
      ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin as ForwarderFailoverStrategyEnum,
    ],
    healthCheckInterval: ['00:00:30', [Validators.required]],
    perForwarderTimeout: ['00:00:03', [Validators.required]],
    enableCache: [true],
    maxCacheEntries: [10_000, [Validators.required, Validators.min(1)]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field this form holds, then surfaces the backend's own hot-reload note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.forwardingApi.apiForwardingSettingsPatch({ body: value }),
      );
      this.notification.success(result.note ?? 'Forwarding settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save forwarding settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.forwardingApi.apiForwardingSettingsGet());
      this.form.patchValue({
        enable: settings.enable ?? false,
        failoverStrategy:
          settings.failoverStrategy ?? ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin,
        healthCheckInterval: settings.healthCheckInterval ?? '00:00:30',
        perForwarderTimeout: settings.perForwarderTimeout ?? '00:00:03',
        enableCache: settings.enableCache ?? true,
        maxCacheEntries: settings.maxCacheEntries ?? 10_000,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load forwarding settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

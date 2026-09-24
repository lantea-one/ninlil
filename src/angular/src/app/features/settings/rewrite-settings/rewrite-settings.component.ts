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
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { RewriteSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component reads and writes this host's own `DnsRewriteOptions` -- the engine-level toggle
 * and synthesized-answer TTL for DNS rewrites, via `GET`/`PATCH /api/rewrite-settings`
 * (`rewritesettings:read`/`rewritesettings:write`). Rewrite entries themselves (each individual
 * domain/target override) are a separate page (Rewrites); this page covers only whether the
 * rewrite engine consults them at query time at all.
 *
 * <p>Every field this form edits takes effect immediately, with no restart -- `DnsRewriteEngine`
 * is now always constructed and consulted by the query pipeline regardless of `Enable`'s own value
 * at process startup, reading its own live `Enable`/TTL configuration fresh on every check via
 * `IOptionsMonitor`. The backend's own PATCH response note (surfaced directly in the success
 * notification, never suppressed) confirms this explicitly.
 */
@Component({
  selector: 'app-rewrite-settings',
  imports: [
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
    SpinnerComponent,
  ],
  templateUrl: './rewrite-settings.component.html',
  styleUrl: './rewrite-settings.component.scss',
})
export class RewriteSettingsComponent implements OnInit {
  private readonly rewriteApi = inject(RewriteSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enable: [false],
    // 4294967295 is the real backend bound: DnsRewriteOptions.TimeToLiveSeconds is a 32-bit
    // unsigned integer.
    timeToLiveSeconds: [300, [Validators.required, Validators.min(0), Validators.max(4294967295)]],
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
      const result = await firstValueFrom(this.rewriteApi.apiRewriteSettingsPatch({ body: value }));
      this.notification.success(result.note ?? 'Rewrite settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save rewrite settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.rewriteApi.apiRewriteSettingsGet());
      this.form.patchValue({
        enable: settings.enable ?? false,
        timeToLiveSeconds: settings.timeToLiveSeconds ?? 300,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load rewrite settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

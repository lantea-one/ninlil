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
 * This component is the "DNS Rewrites" settings page (`/settings/rewrites`). It edits
 * `DnsRewriteOptions` through `GET` and `PATCH /api/rewrite-settings` (`rewritesettings:read`,
 * `rewritesettings:write`): whether rewrites are applied and the TTL of rewritten answers. The
 * rewrites themselves are on the DNS Rewrites page.
 *
 * <p>Both fields are hot-reloaded: `DnsRewriteEngine` is always in the query pipeline and reads
 * these options on each check, whatever `Enable` was at startup.
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
    // DnsRewriteOptions.TimeToLiveSeconds is a 32-bit unsigned integer.
    timeToLiveSeconds: [300, [Validators.required, Validators.min(0), Validators.max(4294967295)]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes both fields, then shows the backend's note. */
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

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
import { WebListenSettingsService } from '../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/confirm-dialog/confirm-dialog.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  buildPfxPasswordPatchField,
  joinLines,
  splitLines,
  splitListenAddresses,
} from './web-listen-settings.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Web Listener" settings page (`/settings/web-listen`). It edits
 * `WebListenOptions`, the address, port and certificate of the web listener shared by the REST
 * API, the SignalR hub and DNS-over-HTTPS, through `GET` and `PATCH /api/web-listen-settings`
 * (`weblistensettings:read`, `weblistensettings:write`).
 * <p>
 * A save is written to the configuration file but applies only after a restart; the running
 * listener cannot be rebound (see `WebListenSettingsController`). Because the change can cut off
 * the session making it, {@link save} first asks for confirmation and warns that the console will
 * be at the new address after the restart. The PFX password is never returned, so a blank field
 * means "keep the stored one".
 */
@Component({
  selector: 'app-web-listen-settings',
  imports: [
    IfPermissionDirective,
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    ConfirmDialogComponent,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './web-listen-settings.component.html',
  styleUrl: './web-listen-settings.component.scss',
})
export class WebListenSettingsComponent implements OnInit {
  private readonly webListenApi = inject(WebListenSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether a PFX password is configured; `Get` never returns the password itself. */
  readonly pfxPasswordConfigured = signal(false);

  readonly confirmDialogVisible = signal(false);
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly form = this.formBuilder.group({
    httpPort: [80, [Validators.required, Validators.min(1), Validators.max(65535)]],
    httpsPort: [443, [Validators.required, Validators.min(1), Validators.max(65535)]],
    listenAddresses: [''],
    pfxPath: [''],
    pfxPassword: [''],
    selfSignedSubjectAlternativeNames: ['localhost'],
    certificateReloadPollInterval: ['00:05:00', [Validators.required]],
    allowedProtocolTls12: [false],
    allowedProtocolTls13: [false],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method asks the operator to confirm the restart-only change, then PATCHes every field. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    const confirmed = await this.confirm({
      title: 'Apply web listener changes?',
      message:
        'This host will keep running at its current address until you restart the process manually -- reconnecting at any new port takes effect only after that restart. If a certificate value here is wrong, this session (and every other admin session) could be unable to reconnect at all until the mistake is corrected and the process restarted again.',
      confirmLabel: 'Apply',
    });
    if (!confirmed) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.webListenApi.apiWebListenSettingsPatch({
          body: {
            httpPort: value.httpPort,
            httpsPort: value.httpsPort,
            listenAddresses: splitListenAddresses(value.listenAddresses),
            certificate: {
              pfxPath: value.pfxPath,
              ...buildPfxPasswordPatchField(value.pfxPassword),
              selfSignedSubjectAlternativeNames: splitLines(
                value.selfSignedSubjectAlternativeNames,
              ),
              certificateReloadPollInterval: value.certificateReloadPollInterval,
              allowedProtocols: [
                ...(value.allowedProtocolTls12 ? ['Tls12'] : []),
                ...(value.allowedProtocolTls13 ? ['Tls13'] : []),
              ],
            },
          },
        }),
      );
      this.notification.success(result.note ?? 'Web listener settings saved.');
      this.form.patchValue({ pfxPassword: '' });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save web listener settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.webListenApi.apiWebListenSettingsGet());
      this.pfxPasswordConfigured.set(settings.certificate?.pfxPasswordConfigured ?? false);
      this.form.patchValue({
        httpPort: settings.httpPort ?? 80,
        httpsPort: settings.httpsPort ?? 443,
        listenAddresses: joinLines(settings.listenAddresses),
        pfxPath: settings.certificate?.pfxPath ?? '',
        pfxPassword: '',
        selfSignedSubjectAlternativeNames: joinLines(
          settings.certificate?.selfSignedSubjectAlternativeNames,
        ),
        certificateReloadPollInterval:
          settings.certificate?.certificateReloadPollInterval ?? '00:05:00',
        allowedProtocolTls12: (settings.certificate?.allowedProtocols ?? []).includes('Tls12'),
        allowedProtocolTls13: (settings.certificate?.allowedProtocols ?? []).includes('Tls13'),
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load web listener settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

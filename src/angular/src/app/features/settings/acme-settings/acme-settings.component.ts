import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { CdkTableModule } from '@angular/cdk/table';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  BadgeComponent,
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
  TableDirective,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { AcmeCertificateInventoryEntry, AcmeSettingsService } from '../../../api-client';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/** This function splits one-value-per-line textarea text into trimmed, non-blank entries. */
function linesToArray(raw: string): string[] {
  return raw
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/**
 * This component is the "ACME / Certificates" settings page (`/settings/acme`). It edits ACME
 * certificate issuance (`AcmeOptions`) and the server-wide default for CAA management
 * (`CaaAutoManagementOptions`; the per-zone override is on `ZoneFormComponent`) through `GET` and
 * `PATCH /api/acme-settings` (`acmesettings:read`, `acmesettings:write`). It also lists each
 * configured domain's certificate state (issue and expiry dates, last failure) from
 * `GET /api/acme-settings/certificates`, and with `acmesettings:manage` can issue or renew one
 * domain now through `POST .../certificates/{domain}/issue`.
 *
 * <p>Every field except `accountStateFilePath` is hot-reloaded; that one needs a restart. The
 * backend's note in the success notification says which applies.
 */
@Component({
  selector: 'app-acme-settings',
  imports: [
    DatePipe,
    IfPermissionDirective,
    ReactiveFormsModule,
    CdkTableModule,
    AlertComponent,
    BadgeComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    EmptyStateComponent,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormTextDirective,
    SpinnerComponent,
    TableDirective,
  ],
  templateUrl: './acme-settings.component.html',
  styleUrl: './acme-settings.component.scss',
})
export class AcmeSettingsComponent implements OnInit {
  private readonly acmeApi = inject(AcmeSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly certificatesLoading = signal(true);
  readonly certificatesErrorMessage = signal<string | null>(null);
  readonly certificates = signal<AcmeCertificateInventoryEntry[]>([]);
  /** The domain whose "Issue/renew now" request is in flight, if any; other rows wait for it. */
  readonly issuingDomain = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enable: [false],
    directoryUri: ['https://acme-v02.api.letsencrypt.org/directory', [Validators.required]],
    contactEmailsText: [''],
    domainsText: [''],
    renewBeforeExpiration: ['30.00:00:00', [Validators.required]],
    pollInterval: ['12:00:00', [Validators.required]],
    accountStateFilePath: ['', [Validators.required]],
    certificateDirectoryPath: ['', [Validators.required]],
    csrOrganization: ['Ninlil', [Validators.required]],
    caaDefaultEnabled: [true],
    caaDefaultIssuerDomain: [''],
  });

  ngOnInit(): void {
    void this.loadSettings();
    void this.loadCertificates();
  }

  /** This method PATCHes every field, shows the backend's note and reloads the certificate list. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.acmeApi.apiAcmeSettingsPatch({
          body: {
            enable: value.enable,
            directoryUri: value.directoryUri,
            contactEmails: linesToArray(value.contactEmailsText),
            domains: linesToArray(value.domainsText),
            renewBeforeExpiration: value.renewBeforeExpiration,
            pollInterval: value.pollInterval,
            accountStateFilePath: value.accountStateFilePath,
            certificateDirectoryPath: value.certificateDirectoryPath,
            csrOrganization: value.csrOrganization,
            caaAutoManagement: {
              defaultEnabled: value.caaDefaultEnabled,
              defaultIssuerDomain: value.caaDefaultIssuerDomain,
            },
          },
        }),
      );
      this.notification.success(result.note ?? 'ACME settings saved.');
      await this.loadCertificates();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save ACME settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method issues or renews one domain's certificate now, outside the renewal service's schedule. */
  async issueOrRenewNow(domain: string): Promise<void> {
    if (this.issuingDomain() !== null) return;

    this.issuingDomain.set(domain);
    this.certificatesErrorMessage.set(null);

    try {
      const entry = await firstValueFrom(
        this.acmeApi.apiAcmeSettingsCertificatesDomainIssuePost({ domain }),
      );

      if (entry.lastFailureMessage) {
        this.notification.error(`'${domain}': ${entry.lastFailureMessage}`);
      } else {
        this.notification.success(`'${domain}': certificate issued/renewed.`);
      }

      await this.loadCertificates();
    } catch (error) {
      this.certificatesErrorMessage.set(
        extractErrorMessage(error, `Could not issue/renew a certificate for '${domain}'.`),
      );
    } finally {
      this.issuingDomain.set(null);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.acmeApi.apiAcmeSettingsGet());
      this.form.patchValue({
        enable: settings.enable ?? false,
        directoryUri: settings.directoryUri ?? 'https://acme-v02.api.letsencrypt.org/directory',
        contactEmailsText: (settings.contactEmails ?? []).join('\n'),
        domainsText: (settings.domains ?? []).join('\n'),
        renewBeforeExpiration: settings.renewBeforeExpiration ?? '30.00:00:00',
        pollInterval: settings.pollInterval ?? '12:00:00',
        accountStateFilePath: settings.accountStateFilePath ?? '',
        certificateDirectoryPath: settings.certificateDirectoryPath ?? '',
        csrOrganization: settings.csrOrganization ?? 'Ninlil',
        caaDefaultEnabled: settings.caaAutoManagement?.defaultEnabled ?? true,
        caaDefaultIssuerDomain: settings.caaAutoManagement?.defaultIssuerDomain ?? '',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load ACME settings.'));
    } finally {
      this.loading.set(false);
    }
  }

  private async loadCertificates(): Promise<void> {
    this.certificatesLoading.set(true);
    this.certificatesErrorMessage.set(null);

    try {
      const entries = await firstValueFrom(this.acmeApi.apiAcmeSettingsCertificatesGet());
      this.certificates.set(entries);
    } catch (error) {
      this.certificatesErrorMessage.set(
        extractErrorMessage(error, 'Could not load the certificate inventory.'),
      );
    } finally {
      this.certificatesLoading.set(false);
    }
  }
}

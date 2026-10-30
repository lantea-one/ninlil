import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
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
import { firstValueFrom, map } from 'rxjs';
import { AcmeCertificateInventoryEntry, AcmeSettingsService } from '../../../api-client';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import {
  changedFields,
  hasChangedFields,
} from '../../../shared/settings-patch/changed-fields.util';

/** This function splits one-value-per-line textarea text into trimmed, non-blank entries. */
function linesToArray(raw: string): string[] {
  return raw
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/** The `PATCH /api/acme-settings` fields the page edits, as the request names them. */
interface AcmeSettingsBody {
  enable: boolean;
  directoryUri: string;
  contactEmails: string[];
  domains: string[];
  renewBeforeExpiration: string;
  pollInterval: string;
  accountStateFilePath: string;
  certificateDirectoryPath: string;
  csrOrganization: string;
  caaAutoManagement: { defaultEnabled: boolean; defaultIssuerDomain: string };
}

/** This function turns the form value into the request shape, splitting the two list editors. */
function toSettingsBody(value: {
  enable: boolean;
  directoryUri: string;
  contactEmailsText: string;
  domainsText: string;
  renewBeforeExpiration: string;
  pollInterval: string;
  accountStateFilePath: string;
  certificateDirectoryPath: string;
  csrOrganization: string;
  caaDefaultEnabled: boolean;
  caaDefaultIssuerDomain: string;
}): AcmeSettingsBody {
  return {
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
  };
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
 * backend picks its note from the fields present in the request, so the page sends only the
 * fields that changed since the last load or save (`changedFields`): saving the domain list alone
 * does not report that a restart is needed. A list is sent whole, and so is the `caaAutoManagement`
 * object (both of its fields are hot-reloaded). Save stays disabled while nothing changed.
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

  /** The form's value after every edit, so the difference below follows the inputs. */
  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  /** The request fields as last loaded or saved; the fields that differ from it are sent. */
  private readonly baseline = signal<AcmeSettingsBody | null>(null);

  /** The fields edited since the last load or save, the body of the next `PATCH`. */
  readonly changes = computed(() => {
    const baseline = this.baseline();
    return baseline === null ? {} : changedFields(baseline, toSettingsBody(this.formValue()));
  });

  /** Whether any field was edited since the last load or save. */
  readonly hasChanges = computed(() => hasChangedFields(this.changes()));

  ngOnInit(): void {
    void this.loadSettings();
    void this.loadCertificates();
  }

  /**
   * This method PATCHes the changed fields only, shows the backend's note and reloads the
   * certificate list.
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting() || !this.hasChanges()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const saved = toSettingsBody(this.form.getRawValue());

    try {
      const result = await firstValueFrom(
        this.acmeApi.apiAcmeSettingsPatch({ body: this.changes() }),
      );
      this.baseline.set(saved);
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
      this.baseline.set(toSettingsBody(this.form.getRawValue()));
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

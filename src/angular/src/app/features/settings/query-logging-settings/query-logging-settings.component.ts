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
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { QueryLogPrivacyLevelEnum, QueryLoggingSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This constant lists the query log privacy levels with a label and a one-line description of what
 * each keeps, shown under the select. It is one 0-3 scale rather than a separate "anonymize client
 * addresses" switch; {@link QueryLogPrivacyLevelEnum.ClientAnonymized} covers that case without a
 * second, possibly conflicting setting.
 */
const PRIVACY_LEVEL_OPTIONS: {
  value: QueryLogPrivacyLevelEnum;
  label: string;
  description: string;
}[] = [
  {
    value: QueryLogPrivacyLevelEnum.Full,
    label: '0 -- Full detail',
    description: 'Every field is stored exactly as observed. This is the default.',
  },
  {
    value: QueryLogPrivacyLevelEnum.DomainRedacted,
    label: '1 -- Domain names hidden',
    description:
      'The queried domain name is never stored. Client address, type, and response code are.',
  },
  {
    value: QueryLogPrivacyLevelEnum.ClientAnonymized,
    label: '2 -- Client identity hidden',
    description:
      'Domain names stay hidden, and only the client’s own containing subnet is stored, never its exact address.',
  },
  {
    value: QueryLogPrivacyLevelEnum.FullyAnonymous,
    label: '3 -- Fully anonymous',
    description:
      'Neither the domain name nor any form of the client’s address is stored -- only aggregate counts.',
  },
];

/**
 * This component is the "Query logging" settings page (`/settings/query-logging`). It edits
 * `QueryLoggingOptions` through `GET` and `PATCH /api/query-logging-settings` (`querylog:read`,
 * `querylog:write`, the query log's permissions): whether answered queries are logged, how long
 * and how many entries are kept, the cleanup interval and the privacy level.
 *
 * <p>Only `privacyLevel` is hot-reloaded; the other fields need a restart. The backend's note in
 * the success notification says which applies.
 */
@Component({
  selector: 'app-query-logging-settings',
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
    FormSelectDirective,
    SpinnerComponent,
  ],
  templateUrl: './query-logging-settings.component.html',
  styleUrl: './query-logging-settings.component.scss',
})
export class QueryLoggingSettingsComponent implements OnInit {
  private readonly queryLoggingApi = inject(QueryLoggingSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** The privacy levels offered, each with its description. */
  readonly privacyLevelOptions = PRIVACY_LEVEL_OPTIONS;

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enable: [true],
    retentionPeriod: ['30.00:00:00', [Validators.required]],
    maxRetainedEntries: [1_000_000, [Validators.required, Validators.min(0)]],
    cleanupInterval: ['01:00:00', [Validators.required]],
    privacyLevel: [QueryLogPrivacyLevelEnum.Full as QueryLogPrivacyLevelEnum],
  });

  /** This method returns the description for the selected, unsaved level. */
  selectedPrivacyLevelDescription(): string {
    const selected = this.privacyLevelOptions.find(
      (option) => option.value === this.form.controls.privacyLevel.value,
    );
    return selected?.description ?? this.privacyLevelOptions[0].description;
  }

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.queryLoggingApi.apiQueryLoggingSettingsPatch({ body: value }),
      );
      this.notification.success(result.note ?? 'Query logging settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save query logging settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.queryLoggingApi.apiQueryLoggingSettingsGet());
      this.form.patchValue({
        enable: settings.enable ?? true,
        retentionPeriod: settings.retentionPeriod ?? '30.00:00:00',
        maxRetainedEntries: settings.maxRetainedEntries ?? 1_000_000,
        cleanupInterval: settings.cleanupInterval ?? '01:00:00',
        privacyLevel: settings.privacyLevel ?? QueryLogPrivacyLevelEnum.Full,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load query logging settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

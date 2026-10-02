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
import { ClientPresenceSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This constant matches a .NET `TimeSpan` in its invariant `[d.]hh:mm:ss[.fffffff]` form, as the
 * backend returns and accepts it. The backend rejects a zero or negative interval with a 400, shown
 * as the page's error banner.
 */
const TIME_SPAN_PATTERN = /^(\d+\.)?\d{1,2}:\d{2}:\d{2}(\.\d+)?$/;

/**
 * This component is the "Client Presence" settings page (`/settings/client-presence`). It edits
 * `ClientPresenceOptions` through `GET` and `PATCH /api/client-presence-settings`
 * (`clientpresencesettings:read`, `clientpresencesettings:write`): how recently a client must have
 * been seen to count as online, how often sightings are saved, and whether and how often the
 * kernel neighbor table is sampled.
 * <p>
 * Presence is passive: it comes from reachable neighbor-table entries, DHCP leases and DNS queries
 * the server already sees, never from scanning. Online status is worked out when read, so every
 * change applies at once without a restart.
 */
@Component({
  selector: 'app-client-presence-settings',
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
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './client-presence-settings.component.html',
  styleUrl: './client-presence-settings.component.scss',
})
export class ClientPresenceSettingsComponent implements OnInit {
  private readonly settingsApi = inject(ClientPresenceSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    onlineWindow: ['00:10:00', [Validators.required, Validators.pattern(TIME_SPAN_PATTERN)]],
    flushInterval: ['00:00:30', [Validators.required, Validators.pattern(TIME_SPAN_PATTERN)]],
    enableNeighborTableSignal: [true],
    neighborTablePollInterval: [
      '00:00:15',
      [Validators.required, Validators.pattern(TIME_SPAN_PATTERN)],
    ],
  });

  ngOnInit(): void {
    void this.load();
  }

  /** This method PATCHes every field, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.settingsApi.apiClientPresenceSettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'Client presence settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save client presence settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.settingsApi.apiClientPresenceSettingsGet());
      this.form.patchValue({
        onlineWindow: settings.onlineWindow ?? '00:10:00',
        flushInterval: settings.flushInterval ?? '00:00:30',
        enableNeighborTableSignal: settings.enableNeighborTableSignal ?? true,
        neighborTablePollInterval: settings.neighborTablePollInterval ?? '00:00:15',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load client presence settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

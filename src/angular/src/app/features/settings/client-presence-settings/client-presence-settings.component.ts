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
 * A .NET `TimeSpan` in its invariant `[d.]hh:mm:ss[.fffffff]` form -- the shape the backend both
 * returns and accepts. Positivity is enforced by the backend itself, which rejects a zero or
 * negative interval with a `400` this page surfaces as its own error banner.
 */
const TIME_SPAN_PATTERN = /^(\d+\.)?\d{1,2}:\d{2}:\d{2}(\.\d+)?$/;

/**
 * This component reads and writes this host's own `ClientPresenceOptions` via `GET`/`PATCH
 * /api/client-presence-settings`: how recently a known client must have been seen to count as
 * online, how often buffered sightings are persisted, and whether (and how often) the kernel
 * neighbor table is sampled as a presence signal.
 * <p>
 * Presence is entirely passive -- inferred from confirmed-reachable neighbor-table entries, DHCP
 * lease grants and renewals, and live DNS queries this server already sees, never from active
 * network scanning -- and online/offline is computed at read time against the online window, so a
 * change here applies to every client immediately, with no restart and no recomputation.
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

  /** This method PATCHes every field this form holds, then surfaces the backend's own hot-reload note. */
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

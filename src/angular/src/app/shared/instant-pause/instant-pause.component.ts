import { DatePipe } from '@angular/common';
import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  FormControlDirective,
  FormLabelDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import {
  ClientDetail,
  ClientGroupDetail,
  ClientGroupsService,
  ClientsService,
} from '../../api-client';
import { extractErrorMessage } from '../http-error/http-error.util';
import { NotificationService } from '../notification/notification.service';

/**
 * This component is a one-click, time-boxed "instant pause" kill switch, embedded as a shared
 * panel on both the client (`ClientFormComponent`) and client group (`ClientGroupFormComponent`)
 * edit pages -- mirroring `BlockedServicesComponent`/`SafeSearchComponent`'s own reusable-panel
 * shape, except each of those two calls one shared, scope-parameterized REST endpoint while a
 * pause is two genuinely separate endpoints (`ClientsController.Pause`/`.Resume` versus
 * `ClientGroupsController.Pause`/`.Resume`), so this component picks which API service to call
 * based on which of {@link clientId}/{@link clientGroupId} is actually set rather than forwarding
 * both as optional query parameters the way those two components do.
 *
 * <p>While paused, every one of that client's (or every member of that group's) own DNS queries
 * is blocked outright, taking priority over every schedule, filter list, and rewrite this project
 * otherwise applies -- see `DnsClientScheduleEngine`'s own doc comment for the real enforcement.
 * This panel only starts/ends that pause; it never shows or edits anything else.
 */
@Component({
  selector: 'app-instant-pause',
  imports: [
    ReactiveFormsModule,
    AlertComponent,
    BadgeComponent,
    ButtonDirective,
    FormControlDirective,
    FormLabelDirective,
    SpinnerComponent,
    DatePipe,
  ],
  templateUrl: './instant-pause.component.html',
  styleUrl: './instant-pause.component.scss',
})
export class InstantPauseComponent implements OnInit {
  private readonly clientsApi = inject(ClientsService);
  private readonly clientGroupsApi = inject(ClientGroupsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** The single client this panel targets. Exactly one of this and {@link clientGroupId} must be set. */
  @Input() clientId?: string;

  /** The single client group this panel targets. Exactly one of this and {@link clientId} must be set. */
  @Input() clientGroupId?: string;

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** The instant, in UTC, the target is currently paused until -- `null` when not currently paused. */
  readonly pausedUntilUtc = signal<string | null>(null);

  /** Whether {@link pausedUntilUtc} is currently in effect, as this panel's own last load/action reported it. */
  readonly isCurrentlyPaused = signal(false);

  /** The custom-duration form control the "Pause" button next to it submits. */
  readonly customMinutesControl = this.formBuilder.control(30, [
    Validators.required,
    Validators.min(1),
  ]);

  ngOnInit(): void {
    void this.load();
  }

  /** This method starts (or replaces) a pause for exactly {@link durationMinutes} minutes from now. */
  async pauseFor(durationMinutes: number): Promise<void> {
    if (this.submitting() || !Number.isFinite(durationMinutes) || durationMinutes <= 0) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const detail = this.clientId
        ? await firstValueFrom(
            this.clientsApi.apiClientsIdPausePost({
              id: this.clientId,
              instantPauseRequest: { durationMinutes },
            }),
          )
        : await firstValueFrom(
            this.clientGroupsApi.apiClientGroupsIdPausePost({
              id: this.clientGroupId ?? '',
              instantPauseRequest: { durationMinutes },
            }),
          );

      this.applyDetail(detail);
      this.notification.success('Paused.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not start the pause.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * This method pauses until the next 07:00 local time -- computing the resulting whole-minute
   * duration and submitting it through {@link pauseFor} exactly like any other quick-duration
   * button, since the server's own contract is always a duration, never an explicit timestamp
   * (see `InstantPauseRequest`'s own doc comment for why).
   */
  async pauseUntilTomorrowMorning(): Promise<void> {
    const now = new Date();
    const tomorrowMorning = new Date(
      now.getFullYear(),
      now.getMonth(),
      now.getDate() + 1,
      7,
      0,
      0,
      0,
    );
    const durationMinutes = Math.max(
      1,
      Math.round((tomorrowMorning.getTime() - now.getTime()) / 60000),
    );

    await this.pauseFor(durationMinutes);
  }

  /** This method submits {@link customMinutesControl}'s own current value. */
  async pauseCustomDuration(): Promise<void> {
    if (this.customMinutesControl.invalid) return;
    await this.pauseFor(this.customMinutesControl.value);
  }

  /** This method ends the pause immediately, regardless of how much time was left on it. */
  async resume(): Promise<void> {
    if (this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const detail = this.clientId
        ? await firstValueFrom(this.clientsApi.apiClientsIdResumePost({ id: this.clientId }))
        : await firstValueFrom(
            this.clientGroupsApi.apiClientGroupsIdResumePost({ id: this.clientGroupId ?? '' }),
          );

      this.applyDetail(detail);
      this.notification.success('Resumed.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not resume.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const detail = this.clientId
        ? await firstValueFrom(this.clientsApi.apiClientsIdGet({ id: this.clientId }))
        : await firstValueFrom(
            this.clientGroupsApi.apiClientGroupsIdGet({ id: this.clientGroupId ?? '' }),
          );

      this.applyDetail(detail);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load the current pause status.'));
    } finally {
      this.loading.set(false);
    }
  }

  private applyDetail(detail: ClientDetail | ClientGroupDetail): void {
    this.pausedUntilUtc.set(detail.pausedUntilUtc ?? null);
    this.isCurrentlyPaused.set(detail.isCurrentlyPaused ?? false);
  }
}

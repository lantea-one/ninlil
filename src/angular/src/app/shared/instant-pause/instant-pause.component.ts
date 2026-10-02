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
 * This component is the "instant pause" panel on the client and client group edit pages
 * (`ClientFormComponent`, `ClientGroupFormComponent`). It blocks every DNS query from the client,
 * or from every member of the group, for a set time. The pause overrides schedules, filter lists
 * and rewrites; `DnsClientScheduleEngine` enforces it.
 *
 * <p>Clients and groups have separate endpoints, so the panel calls
 * `POST /api/clients/{id}/pause` and `/resume`, or the same under `/api/client-groups/{id}`,
 * depending on which of {@link clientId} and {@link clientGroupId} is set. Both need
 * `clients:write` or `clientgroups:write`, which the edit route already requires. The current
 * state comes from the client or group `GET`.
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

  /** The client to pause. Set this or {@link clientGroupId}, not both. */
  @Input() clientId?: string;

  /** The client group to pause. Set this or {@link clientId}, not both. */
  @Input() clientGroupId?: string;

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** When the current pause ends (UTC), or `null` when none is set. */
  readonly pausedUntilUtc = signal<string | null>(null);

  /** Whether the pause is in effect, as of the last load or action. */
  readonly isCurrentlyPaused = signal(false);

  /** The custom duration in minutes, submitted by the "Pause" button beside it. */
  readonly customMinutesControl = this.formBuilder.control(30, [
    Validators.required,
    Validators.min(1),
  ]);

  ngOnInit(): void {
    void this.load();
  }

  /** This method starts or replaces a pause lasting `durationMinutes` from now. */
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
   * This method pauses until 07:00 tomorrow, browser local time. The API takes a duration, not
   * an end time, so it converts the gap to whole minutes and calls {@link pauseFor}.
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

  /** This method pauses for the minutes in {@link customMinutesControl}. */
  async pauseCustomDuration(): Promise<void> {
    if (this.customMinutesControl.invalid) return;
    await this.pauseFor(this.customMinutesControl.value);
  }

  /** This method ends the pause now. */
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

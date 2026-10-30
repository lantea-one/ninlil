import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
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
import { ClientGroupSchedulesService, DnsScheduleDayOfWeekEnum } from '../../../../api-client';
import { timeZoneOptions } from '../../../../shared/time-zone/time-zone.util';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';

/** The seven day checkbox controls, Sunday first; see `ClientScheduleFormComponent`. */
const DAY_CONTROL_NAMES = [
  'sunday',
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
] as const;

const DAY_LABELS: Record<(typeof DAY_CONTROL_NAMES)[number], string> = {
  sunday: 'Sun',
  monday: 'Mon',
  tuesday: 'Tue',
  wednesday: 'Wed',
  thursday: 'Thu',
  friday: 'Fri',
  saturday: 'Sat',
};

const DAY_FULL_NAMES: Record<(typeof DAY_CONTROL_NAMES)[number], string> = {
  sunday: 'Sunday',
  monday: 'Monday',
  tuesday: 'Tuesday',
  wednesday: 'Wednesday',
  thursday: 'Thursday',
  friday: 'Friday',
  saturday: 'Saturday',
};

/**
 * This component is the add and edit page for a client group's schedule
 * (`/client-groups/:groupId/schedules/new` and `.../schedules/:scheduleId/edit`,
 * `clientgroups:write`). It saves with `POST` or `PUT /api/client-groups/{groupId}/schedules/{id}`
 * and works like `ClientScheduleFormComponent`, which explains the `daysOfWeek` string and the
 * time zone list.
 */
@Component({
  selector: 'app-client-group-schedule-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
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
  templateUrl: './client-group-schedule-form.component.html',
  styleUrl: './client-group-schedule-form.component.scss',
})
export class ClientGroupScheduleFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly schedulesApi = inject(ClientGroupSchedulesService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly groupId = this.route.snapshot.paramMap.get('groupId') ?? '';
  private readonly scheduleId = this.route.snapshot.paramMap.get('scheduleId');

  /** Whether this instance is editing an existing schedule rather than adding a new one. */
  readonly isEditMode = this.scheduleId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly dayControlNames = DAY_CONTROL_NAMES;
  readonly dayLabels = DAY_LABELS;

  readonly timeZoneOptions = signal(timeZoneOptions());

  readonly form = this.formBuilder.group({
    sunday: [false],
    monday: [false],
    tuesday: [false],
    wednesday: [false],
    thursday: [false],
    friday: [false],
    saturday: [false],
    startTime: ['', [Validators.required]],
    endTime: ['', [Validators.required]],
    timeZoneId: ['UTC', [Validators.required]],
    isDisabled: [false],
    comments: [''],
  });

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingSchedule();
  }

  /**
   * This method checks that at least one day is chosen and that start and end differ, then adds
   * the schedule (`POST`) or replaces it (`PUT`) and returns to the schedule list.
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    const value = this.form.getRawValue();
    const daysOfWeek = DAY_CONTROL_NAMES.filter((name) => value[name])
      .map((name) => DAY_FULL_NAMES[name])
      .join(', ');

    if (!daysOfWeek) {
      this.errorMessage.set(
        "Select at least one day -- a schedule matching no day can't be saved.",
      );
      return;
    }

    if (value.startTime === value.endTime) {
      this.errorMessage.set(
        "'Start time' and 'end time' cannot be equal -- a zero-length window is meaningless.",
      );
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const request = {
      daysOfWeek: daysOfWeek as unknown as DnsScheduleDayOfWeekEnum,
      startTime: value.startTime,
      endTime: value.endTime,
      timeZoneId: value.timeZoneId,
      isDisabled: value.isDisabled,
      comments: value.comments || null,
    };

    try {
      if (this.isEditMode && this.scheduleId) {
        await firstValueFrom(
          this.schedulesApi.apiClientGroupsGroupIdSchedulesIdPut({
            groupId: this.groupId,
            id: this.scheduleId,
            clientGroupScheduleWriteRequest: request,
          }),
        );
        this.notification.success('Schedule saved.');
      } else {
        await firstValueFrom(
          this.schedulesApi.apiClientGroupsGroupIdSchedulesPost({
            groupId: this.groupId,
            clientGroupScheduleWriteRequest: request,
          }),
        );
        this.notification.success('Schedule added.');
      }

      await this.router.navigate(['/client-groups', this.groupId, 'schedules']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this schedule.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadExistingSchedule(): Promise<void> {
    if (!this.scheduleId) return;

    try {
      const schedule = await firstValueFrom(
        this.schedulesApi.apiClientGroupsGroupIdSchedulesIdGet({
          groupId: this.groupId,
          id: this.scheduleId,
        }),
      );

      const selectedDays = new Set(
        ((schedule.daysOfWeek as unknown as string) ?? '')
          .split(',')
          .map((day) => day.trim())
          .filter((day) => day.length > 0),
      );

      // A schedule created through the API may hold "All", which checks every box.
      // A stored id the browser does not list (an older alias) gets its own option.
      this.timeZoneOptions.set(timeZoneOptions(schedule.timeZoneId));
      this.form.patchValue({
        sunday: selectedDays.has('Sunday') || selectedDays.has('All'),
        monday: selectedDays.has('Monday') || selectedDays.has('All'),
        tuesday: selectedDays.has('Tuesday') || selectedDays.has('All'),
        wednesday: selectedDays.has('Wednesday') || selectedDays.has('All'),
        thursday: selectedDays.has('Thursday') || selectedDays.has('All'),
        friday: selectedDays.has('Friday') || selectedDays.has('All'),
        saturday: selectedDays.has('Saturday') || selectedDays.has('All'),
        startTime: (schedule.startTime ?? '').slice(0, 5),
        endTime: (schedule.endTime ?? '').slice(0, 5),
        timeZoneId: schedule.timeZoneId ?? 'UTC',
        isDisabled: schedule.isDisabled ?? false,
        comments: schedule.comments ?? '',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this schedule.'));
    } finally {
      this.loading.set(false);
    }
  }
}

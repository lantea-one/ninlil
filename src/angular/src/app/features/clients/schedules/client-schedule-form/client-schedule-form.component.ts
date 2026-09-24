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
import { ClientSchedulesService, DnsScheduleDayOfWeekEnum } from '../../../../api-client';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';

/**
 * This tuple names the seven ordinary form controls this component's own `daysOfWeek` checkbox
 * group is built from, in the same Sunday-first order the backend's own
 * `DnsScheduleDayOfWeekEnum` and `DnsScheduleWindowEvaluator` use.
 */
const DAY_CONTROL_NAMES = [
  'sunday',
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
] as const;

/** This record pairs each day control's own form-control name with its short display label. */
const DAY_LABELS: Record<(typeof DAY_CONTROL_NAMES)[number], string> = {
  sunday: 'Sun',
  monday: 'Mon',
  tuesday: 'Tue',
  wednesday: 'Wed',
  thursday: 'Thu',
  friday: 'Fri',
  saturday: 'Sat',
};

/**
 * This record pairs each day control's own form-control name with the full day name the wire's
 * own `daysOfWeek` string uses -- kept separate from {@link DAY_LABELS}' own short UI labels
 * since the two serve genuinely different purposes.
 */
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
 * This component is both the "add a schedule" and "edit a schedule" page for a known client's
 * own time-of-day parental-control schedule -- one component for both, distinguished by whether
 * the route supplies a `scheduleId` (see `app.routes.ts`), the same shape
 * `DhcpReservationFormComponent` already establishes for a "real child resource with its own
 * fields" nested under a parent.
 *
 * <p><strong>`daysOfWeek` is a `[Flags]` enum on the wire</strong> (a comma-separated string such
 * as `"Sunday, Monday"`), but OpenAPI/Swashbuckle has no native notion of a combined-flags value,
 * so the generated `DnsScheduleDayOfWeekEnum` TypeScript type is a plain single-value string
 * union -- it cannot type a combined value at all. This component therefore works with
 * `daysOfWeek` as a plain `string` throughout its own form model, building/parsing the
 * comma-separated wire value directly from/to seven ordinary checkbox controls
 * ({@link DAY_CONTROL_NAMES}), and casts only at the two points a real
 * `ClientScheduleWriteRequest` is actually built.</p>
 *
 * <p><strong>Time zone selection</strong> uses the real, complete IANA time zone database this
 * browser ships (`Intl.supportedValuesOf('timeZone')`) when available, falling back to a small
 * curated list of common zones on an older browser that lacks it -- either way, the backend's own
 * `TimeZoneInfo.FindSystemTimeZoneById` validation is the actual source of truth for whether a
 * submitted id is real, this list only narrows the operator's typical choice down to a dropdown.</p>
 */
@Component({
  selector: 'app-client-schedule-form',
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
  templateUrl: './client-schedule-form.component.html',
  styleUrl: './client-schedule-form.component.scss',
})
export class ClientScheduleFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly schedulesApi = inject(ClientSchedulesService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly clientId = this.route.snapshot.paramMap.get('clientId') ?? '';
  private readonly scheduleId = this.route.snapshot.paramMap.get('scheduleId');

  /** Whether this instance is editing an existing schedule rather than adding a new one. */
  readonly isEditMode = this.scheduleId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** The seven day-of-week checkbox controls' own names, for the template's own `*ngFor`-free repeat. */
  readonly dayControlNames = DAY_CONTROL_NAMES;
  readonly dayLabels = DAY_LABELS;

  /** The time zone ids this form's own `<select>` offers -- see this class's own doc comment. */
  readonly timeZoneOptions = resolveTimeZoneOptions();

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

  /** This method adds (`POST`) or fully replaces (`PUT`) the schedule, per {@link isEditMode}. */
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
          this.schedulesApi.apiClientsClientIdSchedulesIdPut({
            clientId: this.clientId,
            id: this.scheduleId,
            clientScheduleWriteRequest: request,
          }),
        );
        this.notification.success('Schedule saved.');
      } else {
        await firstValueFrom(
          this.schedulesApi.apiClientsClientIdSchedulesPost({
            clientId: this.clientId,
            clientScheduleWriteRequest: request,
          }),
        );
        this.notification.success('Schedule added.');
      }

      await this.router.navigate(['/clients', this.clientId, 'schedules']);
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
        this.schedulesApi.apiClientsClientIdSchedulesIdGet({
          clientId: this.clientId,
          id: this.scheduleId,
        }),
      );

      const selectedDays = new Set(
        ((schedule.daysOfWeek as unknown as string) ?? '')
          .split(',')
          .map((day) => day.trim())
          .filter((day) => day.length > 0),
      );

      // A stored value of "All" (DnsScheduleDayOfWeekEnum.All) is a real, valid combined value
      // this form's own UI never produces directly (it always submits the fully expanded day
      // list), but a row created some other way (a future bulk-apply feature, a direct API call)
      // could still hold it -- checked alongside each individual day name so loading one still
      // shows every box checked correctly.
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

/** This constant is the fallback time zone list used when {@link Intl.supportedValuesOf} is unavailable. */
const FALLBACK_TIME_ZONES = [
  'UTC',
  'America/New_York',
  'America/Chicago',
  'America/Denver',
  'America/Los_Angeles',
  'America/Sao_Paulo',
  'Europe/London',
  'Europe/Paris',
  'Europe/Berlin',
  'Europe/Moscow',
  'Africa/Cairo',
  'Africa/Johannesburg',
  'Asia/Dubai',
  'Asia/Kolkata',
  'Asia/Shanghai',
  'Asia/Tokyo',
  'Asia/Singapore',
  'Australia/Sydney',
  'Pacific/Auckland',
];

/**
 * This function resolves the real, complete IANA time zone list via
 * `Intl.supportedValuesOf('timeZone')` when this runtime supports it, falling back to
 * {@link FALLBACK_TIME_ZONES} otherwise -- see this file's own component doc comment.
 */
function resolveTimeZoneOptions(): string[] {
  const intlWithSupportedValuesOf = Intl as unknown as {
    supportedValuesOf?: (key: string) => string[];
  };

  try {
    const zones = intlWithSupportedValuesOf.supportedValuesOf?.('timeZone');
    if (zones && zones.length > 0) return zones;
  } catch {
    // Fall through to the curated fallback list below.
  }

  return FALLBACK_TIME_ZONES;
}

import { Component, OnInit, computed, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
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
import {
  DnsFilterListActionEnum,
  DnsFilterListKindEnum,
  DnsFilterListSourceEnum,
  FilterListsService,
} from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  FILTER_LIST_ACTION_OPTIONS,
  FILTER_LIST_KIND_OPTIONS,
  FILTER_LIST_SOURCE_OPTIONS,
  KNOWN_BLOCKLIST_OPTIONS,
  assignBulkLineNumbers,
  deriveBlocklistName,
} from '../filter-list.model';

/**
 * The pattern a non-empty refresh interval must match: the .NET `TimeSpan` "c" format,
 * `[d.]hh:mm:ss`. The interval can exceed 24 hours, so the days prefix is allowed.
 */
const REFRESH_INTERVAL_PATTERN = /^(\d{1,2}\.)?\d{1,2}:\d{2}:\d{2}$/;

/** The shortest refresh interval the backend accepts for a subscribed list, in seconds. */
const MINIMUM_REFRESH_INTERVAL_SECONDS = 3600;

/**
 * Rejects a refresh interval shorter than one hour with `belowMinimum`, matching the 400 the
 * filter-lists API returns for it. Blank (the 24-hour default) and text the pattern validator
 * already rejects pass here, so each mistake shows one message.
 */
function minimumRefreshIntervalValidator(
  control: AbstractControl<string>,
): ValidationErrors | null {
  const match = /^(?:(\d{1,2})\.)?(\d{1,2}):(\d{2}):(\d{2})$/.exec(control.value.trim());
  if (match === null) return null;
  const [, days, hours, minutes, seconds] = match;
  const totalSeconds =
    Number(days ?? 0) * 86400 + Number(hours) * 3600 + Number(minutes) * 60 + Number(seconds);
  return totalSeconds < MINIMUM_REFRESH_INTERVAL_SECONDS ? { belowMinimum: true } : null;
}

/** The request body of every filter list `POST` and `PUT` this page sends. */
interface FilterListWriteRequestValue {
  readonly name: string;
  readonly kind: DnsFilterListKindEnum;
  readonly action: DnsFilterListActionEnum;
  readonly source: DnsFilterListSourceEnum;
  readonly subscriptionUrl: string | null;
  readonly refreshInterval: string | null;
  readonly isDisabled: boolean;
  readonly comments: string | null;
}

/** A failed line of a bulk subscribe, with the backend's message. */
interface BulkCreateOutcome {
  readonly line: string;
  readonly message: string;
}

/**
 * This component is the create and edit page for a filter list (`/filter-lists/new` and
 * `/filter-lists/:listId/edit`, `filterlists:write`); edit mode is chosen by the `listId` route
 * parameter. It saves the list's settings with `POST /api/filter-lists` or
 * `PUT /api/filter-lists/{id}`; entries have their own pages.
 *
 * <p>The subscription URL and refresh interval show, and are required, only when the source is
 * {@link DnsFilterListSourceEnum.Subscribed}, as the backend requires.
 *
 * <p>When creating a `Subscribed` list, `subscriptionUrl` is a textarea that takes one URL per
 * line. A dropdown of public block lists (`../filter-list.model.ts`) and a manual entry box both
 * append lines ({@link appendLine}), and save creates one list per URL
 * ({@link saveByCreatingEachSubscription}); there is no bulk endpoint. A `Local` list, and any
 * list in edit mode, has a single URL field, since a list has one URL.
 */
@Component({
  selector: 'app-filter-list-form',
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
  templateUrl: './filter-list-form.component.html',
  styleUrl: './filter-list-form.component.scss',
})
export class FilterListFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly filterListsApi = inject(FilterListsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly listId = this.route.snapshot.paramMap.get('listId');

  /** Whether this instance is editing an existing filter list rather than creating a new one. */
  readonly isEditMode = this.listId !== null;

  readonly kindOptions = FILTER_LIST_KIND_OPTIONS;
  readonly actionOptions = FILTER_LIST_ACTION_OPTIONS;
  readonly sourceOptions = FILTER_LIST_SOURCE_OPTIONS;

  /** The public block lists the create-mode dropdown offers. */
  readonly knownBlocklistOptions = KNOWN_BLOCKLIST_OPTIONS;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);

  /**
   * The number each bulk line got on its first save in this visit, keyed by the line text. A
   * retried line reuses its number, so its name stays the one the first attempt would have given
   * it; see {@link assignBulkLineNumbers}.
   */
  private readonly bulkLineNumbers = new Map<string, number>();
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    kind: [FILTER_LIST_KIND_OPTIONS[0]],
    action: [FILTER_LIST_ACTION_OPTIONS[0]],
    source: [DnsFilterListSourceEnum.Local as DnsFilterListSourceEnum],
    subscriptionUrl: [''],
    refreshIntervalText: [
      '',
      [Validators.pattern(REFRESH_INTERVAL_PATTERN), minimumRefreshIntervalValidator],
    ],
    isDisabled: [false],
    comments: [''],
  });

  /** The selected source as a signal (the form's `valueChanges` is not one). */
  readonly selectedSource = signal<DnsFilterListSourceEnum>(DnsFilterListSourceEnum.Local);

  readonly showSubscriptionSection = computed(
    () => this.selectedSource() === DnsFilterListSourceEnum.Subscribed,
  );

  /**
   * Whether `subscriptionUrl` is a multi-line bulk field: only when creating a `Subscribed` list.
   */
  readonly isBulkSubscriptionMode = computed(
    () => !this.isEditMode && this.showSubscriptionSection(),
  );

  constructor() {
    this.form.controls.source.valueChanges.subscribe((source) => this.selectedSource.set(source));
  }

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingList();
  }

  /**
   * This method adds `value` as a new last line of the `subscriptionUrl` textarea. It skips a
   * blank value and a URL already present, since picking the same entry twice is more likely a
   * slip than a wish for two identical subscriptions. The dropdown and the manual "Add" button
   * both call it.
   */
  appendLine(value: string): void {
    const trimmed = value.trim();
    if (trimmed.length === 0) return;

    const control = this.form.controls.subscriptionUrl;
    const existingLines = control.value
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => line.length > 0);

    if (existingLines.includes(trimmed)) return;

    control.setValue([...existingLines, trimmed].join('\n'));
  }

  /** This method appends the block list picked in the dropdown and resets the dropdown to its placeholder. */
  onPickKnownBlocklist(value: string, selectElement: HTMLSelectElement): void {
    this.appendLine(value);
    selectElement.value = '';
  }

  /** This method appends the manual-entry box's current value as a new line, then clears the box. */
  addManualEntry(inputElement: HTMLInputElement): void {
    this.appendLine(inputElement.value);
    inputElement.value = '';
  }

  /** This method creates the list (one `POST` per line in bulk-subscription mode) or, in edit mode, replaces it (`PUT`). */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    if (this.isBulkSubscriptionMode()) {
      await this.saveByCreatingEachSubscription();
      return;
    }

    const value = this.form.getRawValue();

    if (
      value.source === DnsFilterListSourceEnum.Subscribed &&
      value.subscriptionUrl.trim().length === 0
    ) {
      this.errorMessage.set("'subscriptionUrl' is required when source is 'Subscribed'.");
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const request: FilterListWriteRequestValue = {
      name: value.name,
      kind: value.kind,
      action: value.action,
      source: value.source,
      subscriptionUrl:
        value.source === DnsFilterListSourceEnum.Subscribed ? value.subscriptionUrl : null,
      refreshInterval:
        value.source === DnsFilterListSourceEnum.Subscribed &&
        value.refreshIntervalText.trim().length > 0
          ? value.refreshIntervalText
          : null,
      isDisabled: value.isDisabled,
      comments: value.comments.trim().length > 0 ? value.comments : null,
    };

    try {
      if (this.isEditMode && this.listId) {
        await firstValueFrom(
          this.filterListsApi.apiFilterListsIdPut({
            id: this.listId,
            filterListWriteRequest: request,
          }),
        );
        this.notification.success('Filter list saved.');
      } else {
        await firstValueFrom(
          this.filterListsApi.apiFilterListsPost({ filterListWriteRequest: request }),
        );
        this.notification.success('Filter list created.');
      }

      await this.router.navigate(['/filter-lists']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this filter list.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * This method is the bulk-subscribe save: it splits `subscriptionUrl` into non-blank lines and
   * sends one `POST` per line, in order and one at a time, so a name clash with a list an earlier
   * line just created is reported by the backend. All lists share the form's kind, action,
   * refresh interval, disabled flag and comments; only name and URL vary.
   *
   * <p>If every line succeeds, the page returns to the list. Otherwise the textarea keeps only the
   * failed lines for a retry, each failure's message is shown, and a notification says how many
   * succeeded. A single-line save that fails shows the backend's message alone.
   */
  private async saveByCreatingEachSubscription(): Promise<void> {
    const value = this.form.getRawValue();
    const lines = value.subscriptionUrl
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => line.length > 0);

    if (lines.length === 0) {
      this.errorMessage.set("'subscriptionUrl' is required when source is 'Subscribed'.");
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const refreshInterval =
      value.refreshIntervalText.trim().length > 0 ? value.refreshIntervalText : null;
    const comments = value.comments.trim().length > 0 ? value.comments : null;

    const succeeded: string[] = [];
    const failed: BulkCreateOutcome[] = [];

    const lineNumbers = assignBulkLineNumbers(lines, this.bulkLineNumbers);
    const numberedLines = Math.max(lines.length, this.bulkLineNumbers.size);

    for (let index = 0; index < lines.length; index++) {
      const line = lines[index];
      const request: FilterListWriteRequestValue = {
        name: deriveBlocklistName(line, lineNumbers[index], numberedLines, value.name),
        kind: value.kind,
        action: value.action,
        source: DnsFilterListSourceEnum.Subscribed,
        subscriptionUrl: line,
        refreshInterval,
        isDisabled: value.isDisabled,
        comments,
      };

      try {
        await firstValueFrom(
          this.filterListsApi.apiFilterListsPost({ filterListWriteRequest: request }),
        );
        succeeded.push(line);
      } catch (error) {
        failed.push({
          line,
          message: extractErrorMessage(error, 'Could not create this filter list.'),
        });
      }
    }

    this.submitting.set(false);

    if (failed.length === 0) {
      this.notification.success(
        succeeded.length === 1
          ? 'Filter list created.'
          : `${succeeded.length} filter lists created.`,
      );
      await this.router.navigate(['/filter-lists']);
      return;
    }

    this.form.controls.subscriptionUrl.setValue(failed.map((outcome) => outcome.line).join('\n'));

    if (lines.length === 1) {
      this.errorMessage.set(failed[0].message);
    } else {
      const summary =
        succeeded.length > 0
          ? `${succeeded.length} of ${lines.length} filter lists created. ${failed.length} failed:`
          : `All ${failed.length} filter list(s) failed:`;
      this.errorMessage.set(
        `${summary} ${failed.map((outcome) => `'${outcome.line}' -- ${outcome.message}`).join(' ')}`,
      );
    }

    if (succeeded.length > 0) {
      this.notification.success(`${succeeded.length} of ${lines.length} filter lists created.`);
    }
  }

  private async loadExistingList(): Promise<void> {
    if (!this.listId) return;

    try {
      const list = await firstValueFrom(
        this.filterListsApi.apiFilterListsIdGet({ id: this.listId }),
      );

      this.form.patchValue({
        name: list.name ?? '',
        kind: list.kind ?? FILTER_LIST_KIND_OPTIONS[0],
        action: list.action ?? FILTER_LIST_ACTION_OPTIONS[0],
        source: list.source ?? DnsFilterListSourceEnum.Local,
        subscriptionUrl: list.subscriptionUrl ?? '',
        refreshIntervalText: list.refreshInterval ?? '',
        isDisabled: list.isDisabled ?? false,
        comments: list.comments ?? '',
      });
      this.selectedSource.set(list.source ?? DnsFilterListSourceEnum.Local);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this filter list.'));
    } finally {
      this.loading.set(false);
    }
  }
}

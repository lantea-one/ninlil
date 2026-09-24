import { Component, OnInit, computed, inject, signal } from '@angular/core';
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
  deriveBlocklistName,
} from '../filter-list.model';

/**
 * The pattern a non-empty refresh-interval field must match -- .NET's own `TimeSpan` "c" format
 * shape, `[d.]hh:mm:ss`. `DnsFilterListEntity.RefreshInterval` is a real `TimeSpan`, not bounded
 * to under 24 hours, so the optional days prefix must be accepted here too.
 */
const REFRESH_INTERVAL_PATTERN = /^(\d{1,2}\.)?\d{1,2}:\d{2}:\d{2}$/;

/** The single write request shape shared by every filter-list POST/PUT this component issues. */
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

/** One outcome of a single line's own `POST /api/filter-lists` call inside a bulk create. */
interface BulkCreateOutcome {
  readonly line: string;
  readonly message: string;
}

/**
 * This component is both the "create a filter list" and "edit a filter list" page -- one
 * component for both, distinguished by whether the route supplies a `listId` (see
 * `app.routes.ts`), the same shape `ZoneFormComponent` established. A list's own entries are
 * managed separately, through `FilterListEntryListComponent`/`FilterListEntryFormComponent`; this
 * page only ever touches `DnsFilterListEntity`'s own metadata.
 *
 * The subscription-specific fields (`subscriptionUrl`/`refreshInterval`) only show, and are only
 * required, when `source` is {@link DnsFilterListSourceEnum.Subscribed} -- mirroring
 * `FilterListsController.TryValidateSubscription`'s own real server-side rule as genuine UI
 * state, so a caller sees the requirement before submitting rather than after a `400`.
 *
 * **In create mode, with `source` set to `Subscribed` only**, `subscriptionUrl` doubles as a
 * multi-line bulk-create field: a dropdown of well-known public block lists (see
 * `../filter-list.model.ts`) and a small manual-entry box both append a new line to it (never
 * replacing existing content) via {@link appendLine}, and `save()` splits it on newlines, skips
 * blank lines, and issues one `POST /api/filter-lists` per remaining line via
 * {@link saveByCreatingEachSubscription} -- one new `DnsFilterListEntity` per URL, all sharing
 * this form's own `kind`/`action`/`refreshInterval`/`isDisabled`/`comments` fields, with no new
 * backend endpoint (`FilterListsController` gained no bulk route; this is a client-side loop
 * against its existing `POST`). Every other combination (a `Local` list, or **any** edit-mode
 * save, subscribed or not) keeps this form's original single-entity semantics deliberately -- a
 * `Local` list never had a URL to bulk-append in the first place, and an existing `Subscribed`
 * list is already 1:1 with its one `subscriptionUrl`, so a second textarea line has no sensible
 * target entity to apply to; both keep showing the original single-line `subscriptionUrl` input
 * untouched.
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

  /** Every well-known public block-list option the create-mode dropdown offers. */
  readonly knownBlocklistOptions = KNOWN_BLOCKLIST_OPTIONS;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    kind: [FILTER_LIST_KIND_OPTIONS[0]],
    action: [FILTER_LIST_ACTION_OPTIONS[0]],
    source: [DnsFilterListSourceEnum.Local as DnsFilterListSourceEnum],
    subscriptionUrl: [''],
    refreshIntervalText: ['', [Validators.pattern(REFRESH_INTERVAL_PATTERN)]],
    isDisabled: [false],
    comments: [''],
  });

  /** The source currently selected, tracked as a plain signal since `valueChanges` isn't one. */
  readonly selectedSource = signal<DnsFilterListSourceEnum>(DnsFilterListSourceEnum.Local);

  readonly showSubscriptionSection = computed(
    () => this.selectedSource() === DnsFilterListSourceEnum.Subscribed,
  );

  /**
   * Whether `subscriptionUrl` is currently behaving as a bulk multi-line field -- true only for a
   * new (not-yet-existing) `Subscribed` list, per this component's own class-level doc comment.
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
   * This method appends `value` as a new, trailing line of the `subscriptionUrl` textarea --
   * never replacing what is already there -- skipping a blank value and skipping a value already
   * present verbatim as one of the existing lines (a same-pick double-click is a much more likely
   * accident than a genuine intent to subscribe to the identical URL twice). Called both by the
   * known-block-list dropdown's own change handler and by the manual-entry box's "Add" button.
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

  /** This method handles the known-block-list `<select>`'s own change event, appending the picked option's value and resetting the select back to its placeholder. */
  onPickKnownBlocklist(value: string, selectElement: HTMLSelectElement): void {
    this.appendLine(value);
    selectElement.value = '';
  }

  /** This method appends the manual-entry box's current value as a new line, then clears the box. */
  addManualEntry(inputElement: HTMLInputElement): void {
    this.appendLine(inputElement.value);
    inputElement.value = '';
  }

  /** This method creates (`POST`, one call per non-blank line in bulk-subscription mode) or fully replaces (`PUT`) the filter list, per {@link isEditMode} and {@link isBulkSubscriptionMode}. */
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
   * This method implements bulk-subscription create mode's own save path: split
   * `subscriptionUrl` on newlines, skip blank lines, and `POST` one filter list per remaining
   * line, in order, awaiting each call before issuing the next (rather than firing them
   * concurrently) so that a mid-loop name collision against a list a prior line in the same save
   * just created is caught by the real backend, not raced past client-side. Every created list
   * shares this form's own `kind`/`action`/`refreshInterval`/`isDisabled`/`comments` fields --
   * only `name` and `subscriptionUrl` vary per line.
   * <br><br>
   * A total success navigates back to the list, exactly like the pre-existing single-create path
   * did. A partial or total failure never silently drops what went wrong: every failed line's own
   * backend error is kept, the textarea is narrowed down to just the failed lines so the operator
   * can fix and retry them without having to re-enter what already succeeded, and a follow-up
   * notification records how many lines did succeed when at least one did. A single-line save
   * that fails reports the raw backend message alone, unwrapped -- identical to this form's
   * original single-create error UX -- rather than the multi-line "N of M" summary, which only
   * appears once there is genuinely more than one line to summarize.
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

    for (let index = 0; index < lines.length; index++) {
      const line = lines[index];
      const request: FilterListWriteRequestValue = {
        name: deriveBlocklistName(line, index + 1, lines.length, value.name),
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

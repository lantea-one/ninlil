import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import {
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
  ValidationErrors,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, firstValueFrom, of, Subscription } from 'rxjs';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  SpinnerComponent,
} from '@coreui/angular';
import {
  DnsFilterEntryKindEnum,
  FilterListEntriesService,
  FilterListsService,
  FilterPatternPreviewMatch,
  FilterPatternPreviewService,
} from '../../../../api-client';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';
import {
  FILTER_ENTRY_KIND_OPTIONS,
  isPatternEntryKind,
  isSubscribedList,
  isValidEntryPattern,
} from '../../filter-list.model';

/**
 * This component is both the "add an entry" and "edit an entry" page for a filter list -- one
 * component for both, distinguished by whether the route supplies an `entryId` (see
 * `app.routes.ts`).
 *
 * A manual write against a subscribed list is blocked in the UI itself, not just left to fail
 * with a raw `409`: this component loads the owning list first and, if it is subscribed, replaces
 * the form entirely with an explanatory notice -- covering a direct navigation (a typed-in URL, a
 * stale bookmark) that `FilterListEntryListComponent`'s own disabled controls would otherwise not
 * prevent.
 */
@Component({
  selector: 'app-filter-list-entry-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    BadgeComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    SpinnerComponent,
  ],
  templateUrl: './filter-list-entry-form.component.html',
  styleUrl: './filter-list-entry-form.component.scss',
})
export class FilterListEntryFormComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly entriesApi = inject(FilterListEntriesService);
  private readonly filterListsApi = inject(FilterListsService);
  private readonly previewApi = inject(FilterPatternPreviewService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly listId = this.route.snapshot.paramMap.get('listId') ?? '';
  private readonly entryId = this.route.snapshot.paramMap.get('entryId');

  /** Whether this instance is editing an existing entry rather than creating a new one. */
  readonly isEditMode = this.entryId !== null;

  /** Whether the owning list is subscribed -- when `true`, this form is replaced by a blocking notice. */
  readonly isReadOnly = signal(false);

  /**
   * Whether the owning list is itself a `DnsFilterListKindEnum.Domain` list -- when `false`, the
   * `Wildcard`/`Regex` kind options are hidden entirely, mirroring
   * `FilterListEntriesController`'s own rejection of a pattern kind against a
   * `ResponseIp`/`ClientSource` list's own IP address/CIDR network entries.
   */
  readonly isDomainKindList = signal(true);

  readonly listName = signal<string | null>(null);
  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly kindOptions = FILTER_ENTRY_KIND_OPTIONS;

  readonly form = this.formBuilder.group({
    value: ['', [Validators.required, (control) => this.patternValidator(control.value)]],
    comment: [''],
    kind: [DnsFilterEntryKindEnum.Domain as DnsFilterEntryKindEnum],
  });

  /**
   * The kind currently selected, tracked as a plain signal since `valueChanges` isn't one --
   * the same pattern `ZoneFormComponent.selectedType` already establishes for a conditional
   * template section driven by a sibling form control's own live value.
   */
  private readonly selectedKind = signal<DnsFilterEntryKindEnum>(DnsFilterEntryKindEnum.Domain);

  /** Whether the currently selected kind is `Wildcard`/`Regex` -- drives the pattern-syntax hint. */
  readonly isPatternKind = computed(() => isPatternEntryKind(this.selectedKind()));

  /** The human-readable grammar name for the currently selected pattern kind's own error/hint text. */
  readonly patternKindLabel = computed(() =>
    this.selectedKind() === DnsFilterEntryKindEnum.Wildcard ? 'wildcard' : 'regular expression',
  );

  /** A representative example value for the currently selected kind, shown as the value field's own placeholder. */
  readonly valuePlaceholder = computed(() => {
    switch (this.selectedKind()) {
      case DnsFilterEntryKindEnum.Wildcard:
        return 'ads.*.example.com';
      case DnsFilterEntryKindEnum.Regex:
        return String.raw`^ads?\.example\.com$`;
      default:
        return 'bad.example';
    }
  });

  /**
   * The live "as I type" preview of which sample domains the current pattern would actually
   * match -- calls `FilterPatternPreviewController` directly rather than a second, JavaScript-side
   * regex engine, so the preview can never show a match the real server-side filter engine
   * wouldn't also make (see that controller's own doc comment for the full reasoning).
   */
  readonly previewResult = signal<{
    isValid: boolean;
    error: string | null;
    matches: FilterPatternPreviewMatch[];
  } | null>(null);

  private readonly previewSubscription: Subscription;

  constructor() {
    this.form.controls.kind.valueChanges.subscribe((kind) => {
      this.selectedKind.set(kind);
      // Switching kind changes which pattern grammar applies, so the value control's own
      // validity must be re-checked -- Angular does not do this automatically for a sibling
      // control's own change.
      this.form.controls.value.updateValueAndValidity();
      // A pattern that was valid under the old kind may not mean the same thing under the new
      // one -- re-run the preview immediately rather than waiting for the next keystroke.
      this.runPreview(this.form.controls.value.value, kind);
    });

    this.previewSubscription = this.form.controls.value.valueChanges
      .pipe(debounceTime(300))
      .subscribe((value) => this.runPreview(value, this.selectedKind()));
  }

  ngOnInit(): void {
    void this.load();
  }

  ngOnDestroy(): void {
    this.previewSubscription.unsubscribe();
  }

  /**
   * This method calls `FilterPatternPreviewController` for the current pattern, debounced by the
   * caller (see this component's own constructor) rather than on every raw keystroke. A blank
   * value clears the preview outright instead of calling the endpoint for nothing.
   */
  private runPreview(value: string, kind: DnsFilterEntryKindEnum): void {
    if (!value || value.trim().length === 0) {
      this.previewResult.set(null);
      return;
    }

    firstValueFrom(
      this.previewApi
        .apiFilterPatternPreviewPost({ filterPatternPreviewRequest: { kind, value } })
        .pipe(catchError(() => of(null))),
    ).then((result) => {
      if (!result) return;
      this.previewResult.set({
        isValid: result.isValid ?? false,
        error: result.error ?? null,
        matches: result.matches ?? [],
      });
    });
  }

  /** This method creates (`POST`) or fully replaces (`PUT`) the entry, per {@link isEditMode}. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting() || this.isReadOnly()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { value, comment, kind } = this.form.getRawValue();
    const request = { value, comment: comment.trim().length > 0 ? comment : null, kind };

    try {
      if (this.isEditMode && this.entryId) {
        await firstValueFrom(
          this.entriesApi.apiFilterListsListIdEntriesIdPut({
            listId: this.listId,
            id: this.entryId,
            filterListEntryWriteRequest: request,
          }),
        );
        this.notification.success('Entry saved.');
      } else {
        await firstValueFrom(
          this.entriesApi.apiFilterListsListIdEntriesPost({
            listId: this.listId,
            filterListEntryWriteRequest: request,
          }),
        );
        this.notification.success('Entry added.');
      }

      await this.router.navigate(['/filter-lists', this.listId, 'entries']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this entry.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const list = await firstValueFrom(
        this.filterListsApi.apiFilterListsIdGet({ id: this.listId }),
      );
      this.listName.set(list.name ?? null);
      this.isReadOnly.set(isSubscribedList(list.source));
      this.isDomainKindList.set(list.kind === 'Domain');

      if (this.isReadOnly()) return;

      if (this.isEditMode && this.entryId) {
        const entry = await firstValueFrom(
          this.entriesApi.apiFilterListsListIdEntriesIdGet({
            listId: this.listId,
            id: this.entryId,
          }),
        );
        this.form.patchValue({
          value: entry.value ?? '',
          comment: entry.comment ?? '',
          kind: entry.kind ?? DnsFilterEntryKindEnum.Domain,
        });
      }
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this entry.'));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * This validator mirrors (never replaces) `FilterListEntriesController`'s own real
   * compile-before-persist validation -- a cheap, immediate rejection of an obviously malformed
   * pattern before ever submitting it. Reads the sibling `kind` control's own current value at
   * validation time, rather than being bound once at form construction, so switching kind
   * re-evaluates correctly (see this component's own constructor).
   */
  private patternValidator(value: string): ValidationErrors | null {
    const kind = this.form?.controls.kind.value ?? DnsFilterEntryKindEnum.Domain;

    return isValidEntryPattern(kind, value ?? '') ? null : { invalidPattern: true };
  }
}

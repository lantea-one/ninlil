import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import {
  FormControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
  ValidationErrors,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, firstValueFrom, merge, of, Subscription } from 'rxjs';
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
 * This component is the add and edit page for a filter list entry
 * (`/filter-lists/:listId/entries/new` and `.../entries/:entryId/edit`, `filterlists:write`);
 * edit mode is chosen by the `entryId` route parameter. It saves with `POST` or
 * `PUT /api/filter-lists/{listId}/entries/{id}`.
 *
 * <p>The page loads the list first. For a subscribed list, whose entries the backend refuses to
 * change by hand (409), it shows a notice instead of the form; the entry list disables its links
 * already, so this covers a typed-in URL or bookmark. While a `Wildcard` or `Regex` pattern is
 * typed, a preview shows which sample domains it matches.
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

  /** Whether the list is subscribed; then the form is replaced by a notice. */
  readonly isReadOnly = signal(false);

  /**
   * Whether the list is a `Domain` list. Otherwise the `Wildcard` and `Regex` kinds are hidden,
   * since the backend refuses them for address and network lists.
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
   * The selected kind as a signal (the form's `valueChanges` is not one), for the parts of the
   * template that depend on it.
   */
  private readonly selectedKind = signal<DnsFilterEntryKindEnum>(DnsFilterEntryKindEnum.Domain);

  /** Whether the selected kind is `Wildcard` or `Regex`; shows the pattern hint. */
  readonly isPatternKind = computed(() => isPatternEntryKind(this.selectedKind()));

  /** The name of the selected pattern grammar, for the hint and error text. */
  readonly patternKindLabel = computed(() =>
    this.selectedKind() === DnsFilterEntryKindEnum.Wildcard ? 'wildcard' : 'regular expression',
  );

  /** An example value for the selected kind, used as the value field's placeholder. */
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
   * The live preview of which sample domains the pattern matches. It comes from
   * `POST /api/filter-pattern-preview` (`filterlists:read`) rather than a JavaScript regex, so it
   * shows what the server's filter engine would match. A failed request leaves the last
   * preview in place.
   */
  readonly previewResult = signal<{
    isValid: boolean;
    error: string | null;
    matches: FilterPatternPreviewMatch[];
  } | null>(null);

  /**
   * Domains to test the pattern against, separated by spaces, commas or new lines. It is outside
   * {@link form} because it is only for the preview and is never saved. When empty, the server
   * uses its built-in samples.
   */
  readonly sampleDomainsControl = new FormControl('', { nonNullable: true });

  /** The cap {@link parseSampleDomains} applies, shown in the field's hint. */
  readonly maxSampleDomains = MAX_SAMPLE_DOMAINS;

  private readonly previewSubscription: Subscription;

  constructor() {
    this.form.controls.kind.valueChanges.subscribe((kind) => {
      this.selectedKind.set(kind);
      // The value's validator depends on the kind, and Angular does not re-run it when a
      // sibling control changes.
      this.form.controls.value.updateValueAndValidity();
      // The same text can match differently under another kind, so preview again now.
      this.runPreview(this.form.controls.value.value, kind);
    });

    this.previewSubscription = merge(
      this.form.controls.value.valueChanges,
      this.sampleDomainsControl.valueChanges,
    )
      .pipe(debounceTime(300))
      .subscribe(() => this.runPreview(this.form.controls.value.value, this.selectedKind()));
  }

  ngOnInit(): void {
    void this.load();
  }

  ngOnDestroy(): void {
    this.previewSubscription.unsubscribe();
  }

  /**
   * This method requests a preview for the pattern. The constructor debounces calls by 300 ms. A
   * blank value clears the preview without a request.
   */
  private runPreview(value: string, kind: DnsFilterEntryKindEnum): void {
    if (!value || value.trim().length === 0) {
      this.previewResult.set(null);
      return;
    }

    const sampleDomains = parseSampleDomains(this.sampleDomainsControl.value);

    firstValueFrom(
      this.previewApi
        .apiFilterPatternPreviewPost({
          filterPatternPreviewRequest: {
            kind,
            value,
            sampleDomains: sampleDomains.length > 0 ? sampleDomains : null,
          },
        })
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

  /** This method adds the entry (`POST`) or replaces it (`PUT`), then returns to the entry list. */
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
   * This validator rejects an obviously malformed pattern before submit; the backend still
   * compiles it on save. It reads the `kind` control at validation time, so it follows a kind
   * change (see the constructor).
   */
  private patternValidator(value: string): ValidationErrors | null {
    const kind = this.form?.controls.kind.value ?? DnsFilterEntryKindEnum.Domain;

    return isValidEntryPattern(kind, value ?? '') ? null : { invalidPattern: true };
  }
}

/** The most sample domains one preview sends, enough to test a pattern without a huge request. */
export const MAX_SAMPLE_DOMAINS = 20;

/**
 * This function turns the operator's free-text sample list into distinct, trimmed domains, split
 * on spaces, commas and new lines, keeping at most {@link MAX_SAMPLE_DOMAINS}.
 */
export function parseSampleDomains(text: string): string[] {
  const domains = text
    .split(/[\s,]+/)
    .map((domain) => domain.trim())
    .filter((domain) => domain.length > 0);
  return [...new Set(domains)].slice(0, MAX_SAMPLE_DOMAINS);
}

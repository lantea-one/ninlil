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
import { ForwardersService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { KNOWN_FORWARDER_OPTIONS, deriveForwarderName } from '../forwarder.model';

/** The single write request shape shared by every forwarder POST/PUT this component issues. */
interface ForwarderWriteRequestValue {
  readonly name: string;
  readonly definitionString: string;
  readonly sortOrder: number;
  readonly isDisabled: boolean;
  readonly comments: string | null;
}

/** One outcome of a single line's own `POST /api/forwarders` call inside a bulk create. */
interface BulkCreateOutcome {
  readonly line: string;
  readonly message: string;
}

/**
 * This component is both the "create a forwarder" and "edit a forwarder" page -- one component
 * for both, distinguished by whether the route supplies an `id` (see `app.routes.ts`).
 *
 * `definitionString` is a plain text field, not a structured builder -- matching this project's
 * own established "accept a string, let the real backend parser validate it" pattern (the same
 * one `RecordsController`'s own `data` field and `DnsCryptStampParser` already follow). Any
 * rejection `ForwardersController`'s own `TryValidateDefinitionString` returns (an unparseable
 * plain address, `udp://`/`tcp://`/`tls://`/`https://`/`quic://` URL, or `sdns://` stamp) is
 * surfaced here verbatim via {@link extractErrorMessage}, not re-validated client-side.
 *
 * In **create mode only**, `definitionString` doubles as a multi-line bulk-create field: a
 * dropdown of well-known public resolvers (see `../forwarder.model.ts`) and a small manual-entry
 * box both append a new line to it (never replacing existing content) via {@link appendLine}, and
 * `save()` splits it on newlines, skips blank lines, and issues one `POST /api/forwarders` per
 * remaining line via {@link saveByCreatingEachLine} -- turning what was a single-entity create
 * into a genuine bulk create, with no new backend endpoint (`ForwardersController` gained no bulk
 * route; this is a client-side loop against its existing `POST`). **Edit mode keeps the original
 * single-value semantics deliberately** -- an existing forwarder is one entity with one `Id`, and
 * `PUT` fully replaces it, so there is no sensible way for a second textarea line to map onto the
 * same `PUT` call; the dropdown/append affordance and bulk-save path are therefore both gated
 * behind {@link isEditMode} being `false`; an edit-mode instance still shows the original
 * single-line `definitionString` input untouched.
 */
@Component({
  selector: 'app-forwarder-form',
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
  templateUrl: './forwarder-form.component.html',
  styleUrl: './forwarder-form.component.scss',
})
export class ForwarderFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly forwardersApi = inject(ForwardersService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly forwarderId = this.route.snapshot.paramMap.get('id');

  /** Whether this instance is editing an existing forwarder rather than creating a new one. */
  readonly isEditMode = this.forwarderId !== null;

  /** Every well-known public resolver option the create-mode dropdown offers, grouped by provider. */
  readonly knownForwarderOptions = KNOWN_FORWARDER_OPTIONS;

  /** The distinct {@link KnownForwarderOption.provider} values, in first-seen order, used as this dropdown's own `<optgroup>` labels. */
  readonly providerGroups: readonly string[] = [
    ...new Set(KNOWN_FORWARDER_OPTIONS.map((option) => option.provider)),
  ];

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    definitionString: ['', [Validators.required]],
    sortOrder: [0],
    isDisabled: [false],
    comments: [''],
  });

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingForwarder();
  }

  /**
   * This method appends `value` as a new, trailing line of the `definitionString` textarea --
   * never replacing what is already there -- skipping a blank value and skipping a value already
   * present verbatim as one of the existing lines (a same-pick double-click is a much more likely
   * accident than a genuine intent to create two forwarders with the identical definition
   * string). Called both by the known-resolver dropdown's own change handler and by the
   * manual-entry box's "Add" button.
   */
  appendLine(value: string): void {
    const trimmed = value.trim();
    if (trimmed.length === 0) return;

    const control = this.form.controls.definitionString;
    const existingLines = control.value
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => line.length > 0);

    if (existingLines.includes(trimmed)) return;

    control.setValue([...existingLines, trimmed].join('\n'));
  }

  /** This method handles the known-resolver `<select>`'s own change event, appending the picked option's value and resetting the select back to its placeholder. */
  onPickKnownResolver(value: string, selectElement: HTMLSelectElement): void {
    this.appendLine(value);
    selectElement.value = '';
  }

  /** This method appends the manual-entry box's current value as a new line, then clears the box. */
  addManualEntry(inputElement: HTMLInputElement): void {
    this.appendLine(inputElement.value);
    inputElement.value = '';
  }

  /** This method creates (`POST`, one call per non-blank line) or fully replaces (`PUT`) the forwarder, per {@link isEditMode}. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    if (!this.isEditMode || !this.forwarderId) {
      await this.saveByCreatingEachLine();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const request: ForwarderWriteRequestValue = {
      name: value.name,
      definitionString: value.definitionString,
      sortOrder: value.sortOrder,
      isDisabled: value.isDisabled,
      comments: value.comments.trim().length > 0 ? value.comments : null,
    };

    try {
      await firstValueFrom(
        this.forwardersApi.apiForwardersIdPut({
          id: this.forwarderId,
          forwarderWriteRequest: request,
        }),
      );
      this.notification.success('Forwarder saved.');
      await this.router.navigate(['/forwarders']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this forwarder.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * This method implements create mode's own bulk-save path: split `definitionString` on
   * newlines, skip blank lines, and `POST` one forwarder per remaining line, in order, awaiting
   * each call before issuing the next (rather than firing them concurrently) so that a mid-loop
   * name collision against a forwarder a prior line in the same save just created is caught by
   * the real backend, not raced past client-side.
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
  private async saveByCreatingEachLine(): Promise<void> {
    const value = this.form.getRawValue();
    const lines = value.definitionString
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => line.length > 0);

    if (lines.length === 0) {
      this.errorMessage.set("'definitionString' is required.");
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const succeeded: string[] = [];
    const failed: BulkCreateOutcome[] = [];

    for (let index = 0; index < lines.length; index++) {
      const line = lines[index];
      const request: ForwarderWriteRequestValue = {
        name: deriveForwarderName(line, index + 1, lines.length, value.name),
        definitionString: line,
        sortOrder: value.sortOrder,
        isDisabled: value.isDisabled,
        comments: value.comments.trim().length > 0 ? value.comments : null,
      };

      try {
        await firstValueFrom(
          this.forwardersApi.apiForwardersPost({ forwarderWriteRequest: request }),
        );
        succeeded.push(line);
      } catch (error) {
        failed.push({
          line,
          message: extractErrorMessage(error, 'Could not create this forwarder.'),
        });
      }
    }

    this.submitting.set(false);

    if (failed.length === 0) {
      this.notification.success(
        succeeded.length === 1 ? 'Forwarder created.' : `${succeeded.length} forwarders created.`,
      );
      await this.router.navigate(['/forwarders']);
      return;
    }

    this.form.controls.definitionString.setValue(failed.map((outcome) => outcome.line).join('\n'));

    if (lines.length === 1) {
      this.errorMessage.set(failed[0].message);
    } else {
      const summary =
        succeeded.length > 0
          ? `${succeeded.length} of ${lines.length} forwarders created. ${failed.length} failed:`
          : `All ${failed.length} forwarder(s) failed:`;
      this.errorMessage.set(
        `${summary} ${failed.map((outcome) => `'${outcome.line}' -- ${outcome.message}`).join(' ')}`,
      );
    }

    if (succeeded.length > 0) {
      this.notification.success(`${succeeded.length} of ${lines.length} forwarders created.`);
    }
  }

  private async loadExistingForwarder(): Promise<void> {
    if (!this.forwarderId) return;

    try {
      const forwarder = await firstValueFrom(
        this.forwardersApi.apiForwardersIdGet({ id: this.forwarderId }),
      );

      this.form.patchValue({
        name: forwarder.name ?? '',
        definitionString: forwarder.definitionString ?? '',
        sortOrder: forwarder.sortOrder ?? 0,
        isDisabled: forwarder.isDisabled ?? false,
        comments: forwarder.comments ?? '',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this forwarder.'));
    } finally {
      this.loading.set(false);
    }
  }
}

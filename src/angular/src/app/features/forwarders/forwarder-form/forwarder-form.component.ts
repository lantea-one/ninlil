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
import {
  KNOWN_FORWARDER_OPTIONS,
  assignBulkLineNumbers,
  deriveForwarderName,
} from '../forwarder.model';

/** The request body of every forwarder `POST` and `PUT` this page sends. */
interface ForwarderWriteRequestValue {
  readonly name: string;
  readonly definitionString: string;
  readonly sortOrder: number;
  readonly isDisabled: boolean;
  readonly comments: string | null;
}

/** A failed line of a bulk create, with the backend's message. */
interface BulkCreateOutcome {
  readonly line: string;
  readonly message: string;
}

/**
 * This component is the create and edit page for an upstream forwarder (`/forwarders/new` and
 * `/forwarders/:id/edit`, `forwarders:write`); edit mode is chosen by the `id` route parameter.
 * It saves with `POST /api/forwarders` or `PUT /api/forwarders/{id}`.
 *
 * <p>`definitionString` is plain text: a plain address, a `udp://`, `tcp://`, `tls://`,
 * `https://` or `quic://` URL, or an `sdns://` stamp. The backend parses it, and its error is
 * shown as returned; the page does not validate it further.
 *
 * <p>In create mode only, `definitionString` is a textarea that takes one forwarder per line. A
 * dropdown of public resolvers (`../forwarder.model.ts`) and a manual entry box both append lines
 * ({@link appendLine}), and save creates one forwarder per line ({@link saveByCreatingEachLine});
 * there is no bulk endpoint. Edit mode shows a single line, because `PUT` replaces one forwarder.
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

  /** The public resolvers the create-mode dropdown offers. */
  readonly knownForwarderOptions = KNOWN_FORWARDER_OPTIONS;

  /** The providers, in first-seen order, used as the dropdown's `<optgroup>` labels. */
  readonly providerGroups: readonly string[] = [
    ...new Set(KNOWN_FORWARDER_OPTIONS.map((option) => option.provider)),
  ];

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
    definitionString: ['', [Validators.required]],
    sortOrder: [0],
    isDisabled: [false],
    comments: [''],
  });

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingForwarder();
  }

  /**
   * This method adds `value` as a new last line of the `definitionString` textarea. It skips a
   * blank value and a value already present as a line, since picking the same entry twice is more
   * likely a slip than a wish for two identical forwarders. The resolver dropdown and the manual
   * "Add" button both call it.
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

  /** This method appends the resolver picked in the dropdown and resets the dropdown to its placeholder. */
  onPickKnownResolver(value: string, selectElement: HTMLSelectElement): void {
    this.appendLine(value);
    selectElement.value = '';
  }

  /** This method appends the manual-entry box's current value as a new line, then clears the box. */
  addManualEntry(inputElement: HTMLInputElement): void {
    this.appendLine(inputElement.value);
    inputElement.value = '';
  }

  /** This method creates one forwarder per non-blank line (`POST`) or, in edit mode, replaces the forwarder (`PUT`). */
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
   * This method is the create-mode save: it splits `definitionString` into non-blank lines and
   * sends one `POST` per line, in order and one at a time, so a name clash with a forwarder an
   * earlier line just created is reported by the backend rather than lost in a race.
   *
   * <p>If every line succeeds, the page returns to the list. Otherwise the textarea keeps only the
   * failed lines, so they can be fixed and retried, each failure's message is shown, and a
   * notification says how many lines succeeded. A single-line save that fails shows the backend's
   * message alone instead of the "N of M" summary.
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

    const lineNumbers = assignBulkLineNumbers(lines, this.bulkLineNumbers);
    const numberedLines = Math.max(lines.length, this.bulkLineNumbers.size);

    for (let index = 0; index < lines.length; index++) {
      const line = lines[index];
      const request: ForwarderWriteRequestValue = {
        name: deriveForwarderName(line, lineNumbers[index], numberedLines, value.name),
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

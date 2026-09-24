import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import {
  AppearanceSettingsService as AppearanceApiService,
  UiThemeModeEnum,
  UiThemePaletteEnum,
} from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { MODE_OPTIONS, PALETTE_OPTIONS } from '../../../core/theme/theme-palette.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component reads and writes this host's own *global default* appearance -- the palette and
 * light/dark/system mode every signed-in user without a personal override of their own inherits
 * -- via `GET`/`PATCH /api/appearance-settings` (`appearance:read`/`appearance:write`).
 *
 * <p>This is deliberately distinct from the header's own {@link ThemeSwitcherComponent}, which
 * edits the *current, signed-in caller's own personal override* on top of this default (via
 * `GET`/`PATCH /api/appearance-settings/me`, gated by nothing more than a valid session). Both
 * reuse the same {@link PALETTE_OPTIONS}/{@link MODE_OPTIONS} catalog {@link ThemeService} itself
 * is built on, so the two pickers' own labels/swatches/icons never drift apart -- but this page's
 * own form (a plain settings-style `select[cSelect]` pair inside a settings card, matching every
 * other settings sub-page in this shell) is visually distinct from the switcher's own dropdown-
 * with-swatch-buttons, so an operator can never mistake "I am changing everyone's default" for
 * "I am changing my own preference."
 *
 * <p>Per `AppearanceSettingsController.Patch`'s own doc comment, `AppearanceOptions` is this
 * codebase's first runtime settings class bound via `IOptionsMonitor` rather than the usual,
 * restart-requiring `IOptions` -- so, unlike every other settings sub-page in this shell, a
 * successful save here takes effect immediately, with no restart, and the backend's own
 * `RuntimeSettingsWriteResult.HotReloadedNote` (surfaced verbatim in the success notification,
 * matching every other settings sub-page's own "never suppress the backend's own note" contract)
 * says so explicitly.
 */
@Component({
  selector: 'app-appearance-settings',
  imports: [
    IfPermissionDirective,
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    SpinnerComponent,
  ],
  templateUrl: './appearance-settings.component.html',
  styleUrl: './appearance-settings.component.scss',
})
export class AppearanceSettingsComponent implements OnInit {
  private readonly appearanceApi = inject(AppearanceApiService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** Every palette this picker offers, reused from {@link ThemeService}'s own shared catalog. */
  readonly paletteOptions = PALETTE_OPTIONS;

  /** Every mode this picker offers, reused from {@link ThemeService}'s own shared catalog. */
  readonly modeOptions = MODE_OPTIONS;

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    palette: [UiThemePaletteEnum.Teal as UiThemePaletteEnum],
    mode: [UiThemeModeEnum.System as UiThemeModeEnum],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /**
   * This method returns the swatch color of the currently-selected (not-yet-saved) palette, for
   * a live preview next to the picker -- a plain method rather than a `computed()`, since its own
   * dependency is a reactive-forms `FormControl.value`, not a signal; Angular's zoneless change
   * detection still re-invokes it on every change-detection pass a template event binding (the
   * select's own `(change)`, wired implicitly by `formControlName`) schedules.
   */
  selectedSwatch(): string {
    const selected = this.paletteOptions.find(
      (option) => option.value === this.form.controls.palette.value,
    );
    return selected?.swatch ?? this.paletteOptions[0].swatch;
  }

  /** This method PATCHes both fields this form holds, then surfaces the backend's own hot-reload note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.appearanceApi.apiAppearanceSettingsPatch({ body: value }),
      );
      this.notification.success(result.note ?? 'Global default appearance saved.');
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not save the global default appearance.'),
      );
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.appearanceApi.apiAppearanceSettingsGet());
      this.form.patchValue({
        palette: settings.palette ?? UiThemePaletteEnum.Teal,
        mode: settings.mode ?? UiThemeModeEnum.System,
      });
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load the global default appearance.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}

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
 * This component is the "Appearance" settings page (`/settings/appearance`). It edits the
 * server-wide default palette and light/dark/system mode, which every user without a personal
 * choice inherits, through `GET` and `PATCH /api/appearance-settings` (`appearance:read`,
 * `appearance:write`).
 *
 * <p>The header's `ThemeSwitcherComponent` is different: through {@link ThemeService} it edits the
 * signed-in user's personal override (`/api/appearance-settings/me`, open to any signed-in user).
 * Both use the {@link PALETTE_OPTIONS} and {@link MODE_OPTIONS} catalogs so their labels and
 * swatches match, but this page uses plain selects in a settings card so it is not mistaken for
 * the personal picker.
 *
 * <p>The setting is hot-reloaded: a save applies at once, and the backend's note (shown in the
 * success notification) says so.
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

  /** The palettes offered, from the catalog {@link ThemeService} uses. */
  readonly paletteOptions = PALETTE_OPTIONS;

  /** The modes offered, from the catalog {@link ThemeService} uses. */
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
   * This method returns the swatch color of the selected, unsaved palette for the preview beside
   * the picker. It is a method, not a `computed()`, because it reads a `FormControl` value rather
   * than a signal; the select's change event triggers change detection, which calls it again.
   */
  selectedSwatch(): string {
    const selected = this.paletteOptions.find(
      (option) => option.value === this.form.controls.palette.value,
    );
    return selected?.swatch ?? this.paletteOptions[0].swatch;
  }

  /** This method PATCHes both fields, then shows the backend's note. */
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

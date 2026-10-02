import { Component, computed, inject, input, signal } from '@angular/core';
import {
  ButtonDirective,
  DropdownComponent,
  DropdownDividerDirective,
  DropdownHeaderDirective,
  DropdownItemDirective,
  DropdownMenuDirective,
  DropdownToggleDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { UiThemeModeEnum, UiThemePaletteEnum } from '../../api-client';
import { MODE_OPTIONS, PALETTE_OPTIONS } from '../../core/theme/theme-palette.model';
import { ThemeService } from '../../core/theme/theme.service';

/**
 * This component is the appearance dropdown in the app shell header ({@link AppShellComponent}).
 * It offers the five palettes and the Light, Dark and System modes. Choosing one calls
 * {@link ThemeService.setPalette} or {@link ThemeService.setMode}, which saves the signed-in
 * user's preference through `PATCH /api/appearance-settings/me` and applies it at once.
 *
 * <p>The trigger button shows the selected mode's icon (sun, moon or contrast for System).
 */
@Component({
  selector: 'app-theme-switcher',
  imports: [
    ButtonDirective,
    DropdownComponent,
    DropdownDividerDirective,
    DropdownHeaderDirective,
    DropdownItemDirective,
    DropdownMenuDirective,
    DropdownToggleDirective,
    IconDirective,
  ],
  templateUrl: './theme-switcher.component.html',
  styleUrl: './theme-switcher.component.scss',
})
export class ThemeSwitcherComponent {
  /** Which edge of the trigger button the dropdown menu opens against. */
  readonly alignment = input<'start' | 'end'>('end');

  private readonly themeService = inject(ThemeService);

  /** Every palette this picker offers, with its display label and swatch color. */
  readonly paletteOptions = PALETTE_OPTIONS;

  /** Every mode this picker offers, with its display label and icon. */
  readonly modeOptions = MODE_OPTIONS;

  /** The palette currently in effect. */
  readonly activePalette = this.themeService.palette;

  /** The mode currently in effect (possibly `System`). */
  readonly activeMode = this.themeService.mode;

  /** The icon of the selected mode, shown on the trigger button. */
  readonly triggerIcon = computed(
    () =>
      this.modeOptions.find((option) => option.value === this.activeMode())?.icon ?? 'cilContrast',
  );

  /** Whether a change is in flight; every option is disabled meanwhile, so requests cannot race. */
  readonly pending = signal(false);

  /** This method saves a newly selected palette. A failed save is not reported to the user. */
  async selectPalette(palette: UiThemePaletteEnum): Promise<void> {
    if (this.pending() || palette === this.activePalette()) return;

    this.pending.set(true);
    try {
      await this.themeService.setPalette(palette);
    } finally {
      this.pending.set(false);
    }
  }

  /** This method saves a newly selected mode. A failed save is not reported to the user. */
  async selectMode(mode: UiThemeModeEnum): Promise<void> {
    if (this.pending() || mode === this.activeMode()) return;

    this.pending.set(true);
    try {
      await this.themeService.setMode(mode);
    } finally {
      this.pending.set(false);
    }
  }
}

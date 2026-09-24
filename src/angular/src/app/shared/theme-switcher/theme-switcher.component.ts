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
 * This component is this app's own reusable appearance switcher: a single dropdown, hosted in the
 * app shell's own header nav ({@link AppShellComponent}), offering both the 5-palette picker and
 * the light/dark/system mode picker in one place. It reads and writes exclusively
 * through {@link ThemeService} -- selecting an option calls
 * {@link ThemeService.setPalette}/{@link ThemeService.setMode} directly, and the resulting theme
 * change is reflected immediately, with no page reload, through {@link ThemeService}'s own
 * signal-based reactivity.
 *
 * <p>The trigger button itself shows the currently-resolved mode's own icon (sun/moon/contrast),
 * so the switcher communicates the active state even before it is opened.
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

  /** The `@coreui/icons` icon name for whichever mode is currently in effect -- shown on the trigger button. */
  readonly triggerIcon = computed(
    () =>
      this.modeOptions.find((option) => option.value === this.activeMode())?.icon ?? 'cilContrast',
  );

  /** Whether a palette/mode change is currently in flight, disabling every option to prevent a racing double-click. */
  readonly pending = signal(false);

  /** This method applies a newly-selected palette, guarding against overlapping in-flight requests. */
  async selectPalette(palette: UiThemePaletteEnum): Promise<void> {
    if (this.pending() || palette === this.activePalette()) return;

    this.pending.set(true);
    try {
      await this.themeService.setPalette(palette);
    } finally {
      this.pending.set(false);
    }
  }

  /** This method applies a newly-selected mode, guarding against overlapping in-flight requests. */
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

import { UiThemeModeEnum, UiThemePaletteEnum } from '../../api-client';

/** One selectable entry in the palette picker: a palette value, its display label, and a swatch color. */
export interface PaletteOption {
  /** The palette value this option selects, via {@link ThemeService.setPalette}. */
  readonly value: UiThemePaletteEnum;
  /** The human-readable label shown next to this option's swatch. */
  readonly label: string;
  /**
   * The color swatch shown for this option -- this palette's own light-mode `--cui-primary`
   * value from `src/styles/themes/`, kept in sync with that CSS by hand since a CSS custom
   * property cannot itself be read back into a plain TypeScript constant at build time.
   */
  readonly swatch: string;
}

/**
 * This constant lists every palette the switcher UI's palette picker offers, in the same order
 * `UiThemePaletteEnum` declares them in. Each entry's {@link PaletteOption.swatch} must be kept
 * in sync by hand with that palette's own light-mode `--cui-primary` value in
 * `src/styles/themes/_<palette>.scss` if either is ever re-tuned.
 */
export const PALETTE_OPTIONS: readonly PaletteOption[] = [
  { value: UiThemePaletteEnum.Teal, label: 'Teal', swatch: '#0f766e' },
  { value: UiThemePaletteEnum.Indigo, label: 'Indigo', swatch: '#4338ca' },
  { value: UiThemePaletteEnum.Amber, label: 'Amber', swatch: '#92400e' },
  { value: UiThemePaletteEnum.Rose, label: 'Rose', swatch: '#be123c' },
  { value: UiThemePaletteEnum.Emerald, label: 'Emerald', swatch: '#047857' },
];

/** One selectable entry in the mode picker: a mode value, its display label, and a CoreUI icon name. */
export interface ModeOption {
  /** The mode value this option selects, via {@link ThemeService.setMode}. */
  readonly value: UiThemeModeEnum;
  /** The human-readable label shown next to this option's icon. */
  readonly label: string;
  /** The `@coreui/icons` icon (from `app.config.ts`'s own registered icon set) shown for this option. */
  readonly icon: string;
}

/**
 * This constant lists every mode the switcher UI's mode picker offers, in the same order
 * `UiThemeModeEnum` declares them in.
 */
export const MODE_OPTIONS: readonly ModeOption[] = [
  { value: UiThemeModeEnum.Light, label: 'Light', icon: 'cilSun' },
  { value: UiThemeModeEnum.Dark, label: 'Dark', icon: 'cilMoon' },
  { value: UiThemeModeEnum.System, label: 'System', icon: 'cilContrast' },
];

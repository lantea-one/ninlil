import { UiThemeModeEnum, UiThemePaletteEnum } from '../../api-client';

/** One selectable entry in the palette picker: a palette value, its display label, and a swatch color. */
export interface PaletteOption {
  /** The palette value this option selects, via {@link ThemeService.setPalette}. */
  readonly value: UiThemePaletteEnum;
  /** The human-readable label shown next to this option's swatch. */
  readonly label: string;
  /**
   * The color swatch shown for this option: the palette's light-mode `--cui-primary` from
   * `src/styles/themes/`. It is copied by hand, since TypeScript cannot read a CSS custom
   * property at build time.
   */
  readonly swatch: string;
}

/**
 * This constant lists every palette the theme switcher offers, in `UiThemePaletteEnum` order.
 * Each {@link PaletteOption.swatch} must match the light-mode `--cui-primary` in
 * `src/styles/themes/_<palette>.scss`; change both together.
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
  /** The `@coreui/icons` icon shown for this option; it must be registered in `app.config.ts`. */
  readonly icon: string;
}

/** This constant lists every mode the theme switcher offers, in `UiThemeModeEnum` order. */
export const MODE_OPTIONS: readonly ModeOption[] = [
  { value: UiThemeModeEnum.Light, label: 'Light', icon: 'cilSun' },
  { value: UiThemeModeEnum.Dark, label: 'Dark', icon: 'cilMoon' },
  { value: UiThemeModeEnum.System, label: 'System', icon: 'cilContrast' },
];

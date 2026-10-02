import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  AppearanceSettingsService as AppearanceApiService,
  UiThemeModeEnum,
  UiThemePaletteEnum,
} from '../../api-client';
import { ThemeService } from './theme.service';

/**
 * This class stands in for the `MediaQueryList` that `matchMedia('(prefers-color-scheme: dark)')`
 * returns, so a test can set the initial scheme and simulate a change. A headless browser does
 * not let a test control the OS color scheme.
 */
class FakeMediaQueryList {
  matches = false;
  private readonly listeners: Array<(event: { matches: boolean }) => void> = [];

  addEventListener(_type: string, listener: (event: { matches: boolean }) => void): void {
    this.listeners.push(listener);
  }

  removeEventListener(): void {
    // Not used: the service never unsubscribes.
  }

  /** Simulates the operating system's color-scheme preference changing. */
  dispatchChange(matches: boolean): void {
    this.matches = matches;
    for (const listener of this.listeners) listener({ matches });
  }
}

/**
 * A hand-written fake for the generated `AppearanceApiService`. The `AuthService` spec explains
 * why it is a plain object rather than a `jasmine.SpyObj`.
 */
interface FakeAppearanceApiService {
  apiAppearanceSettingsMeGet: jasmine.Spy;
  apiAppearanceSettingsMePatch: jasmine.Spy;
}

/**
 * These tests cover `ThemeService`: the fallback before sign-in, applying a preference, setting
 * and clearing overrides, explicit and System modes (including a live system change) and the
 * document root attributes. TestBed with a fake `AppearanceApiService` and a fake
 * `MediaQueryList` swapped in for `window.matchMedia`.
 */
describe('ThemeService', () => {
  let service: ThemeService;
  let appearanceApiSpy: FakeAppearanceApiService;
  let fakeMediaQueryList: FakeMediaQueryList;

  beforeEach(() => {
    fakeMediaQueryList = new FakeMediaQueryList();
    spyOn(window, 'matchMedia').and.returnValue(fakeMediaQueryList as unknown as MediaQueryList);

    appearanceApiSpy = {
      apiAppearanceSettingsMeGet: jasmine.createSpy('apiAppearanceSettingsMeGet'),
      apiAppearanceSettingsMePatch: jasmine.createSpy('apiAppearanceSettingsMePatch'),
    };

    TestBed.configureTestingModule({
      providers: [ThemeService, { provide: AppearanceApiService, useValue: appearanceApiSpy }],
    });

    service = TestBed.inject(ThemeService);
  });

  afterEach(() => {
    // Remove the theming attributes so they do not leak into the next spec.
    document.documentElement.removeAttribute('data-coreui-theme');
    document.documentElement.removeAttribute('data-ninlil-palette');
  });

  it("starts with this app's own compiled-in default, not yet ready", () => {
    expect(service.palette()).toBe(UiThemePaletteEnum.Teal);
    expect(service.mode()).toBe(UiThemeModeEnum.System);
    expect(service.ready()).toBeFalse();
  });

  it('initialize() applies the compiled-in fallback when GET /me fails (e.g. an anonymous caller)', async () => {
    appearanceApiSpy.apiAppearanceSettingsMeGet.and.returnValue(
      throwError(() => new Error('401 Unauthorized')),
    );

    await service.initialize();

    expect(service.palette()).toBe(UiThemePaletteEnum.Teal);
    expect(service.mode()).toBe(UiThemeModeEnum.System);
    expect(service.hasPaletteOverride()).toBeFalse();
    expect(service.hasModeOverride()).toBeFalse();
    expect(service.ready()).toBeTrue();
  });

  it("initialize() applies the signed-in caller's own resolved preference on success", async () => {
    appearanceApiSpy.apiAppearanceSettingsMeGet.and.returnValue(
      of({
        palette: UiThemePaletteEnum.Rose,
        mode: null,
        effectivePalette: UiThemePaletteEnum.Rose,
        effectiveMode: UiThemeModeEnum.Dark,
      }),
    );

    await service.initialize();

    expect(service.palette()).toBe(UiThemePaletteEnum.Rose);
    expect(service.mode()).toBe(UiThemeModeEnum.Dark);
    expect(service.hasPaletteOverride()).toBeTrue();
    expect(service.hasModeOverride()).toBeFalse();
    expect(service.ready()).toBeTrue();
  });

  it('setPalette() PATCHes /me and applies the response immediately, with no reload', async () => {
    appearanceApiSpy.apiAppearanceSettingsMePatch.and.returnValue(
      of({
        palette: UiThemePaletteEnum.Indigo,
        mode: null,
        effectivePalette: UiThemePaletteEnum.Indigo,
        effectiveMode: UiThemeModeEnum.System,
      }),
    );

    await service.setPalette(UiThemePaletteEnum.Indigo);

    expect(appearanceApiSpy.apiAppearanceSettingsMePatch).toHaveBeenCalledWith({
      body: { palette: UiThemePaletteEnum.Indigo },
    });
    expect(service.palette()).toBe(UiThemePaletteEnum.Indigo);
    expect(service.hasPaletteOverride()).toBeTrue();
  });

  it('setPalette(null) clears the override, reverting to the resolved global default', async () => {
    appearanceApiSpy.apiAppearanceSettingsMePatch.and.returnValue(
      of({
        palette: null,
        mode: null,
        effectivePalette: UiThemePaletteEnum.Teal,
        effectiveMode: UiThemeModeEnum.System,
      }),
    );

    await service.setPalette(null);

    expect(appearanceApiSpy.apiAppearanceSettingsMePatch).toHaveBeenCalledWith({
      body: { palette: null },
    });
    expect(service.hasPaletteOverride()).toBeFalse();
    expect(service.palette()).toBe(UiThemePaletteEnum.Teal);
  });

  it('setMode() PATCHes /me and applies the response immediately', async () => {
    appearanceApiSpy.apiAppearanceSettingsMePatch.and.returnValue(
      of({
        palette: null,
        mode: UiThemeModeEnum.Dark,
        effectivePalette: UiThemePaletteEnum.Teal,
        effectiveMode: UiThemeModeEnum.Dark,
      }),
    );

    await service.setMode(UiThemeModeEnum.Dark);

    expect(appearanceApiSpy.apiAppearanceSettingsMePatch).toHaveBeenCalledWith({
      body: { mode: UiThemeModeEnum.Dark },
    });
    expect(service.mode()).toBe(UiThemeModeEnum.Dark);
    expect(service.resolvedColorScheme()).toBe('dark');
  });

  it('resolves an explicit Light/Dark mode without consulting prefers-color-scheme at all', async () => {
    appearanceApiSpy.apiAppearanceSettingsMePatch.and.returnValue(
      of({
        palette: null,
        mode: UiThemeModeEnum.Light,
        effectivePalette: UiThemePaletteEnum.Teal,
        effectiveMode: UiThemeModeEnum.Light,
      }),
    );
    fakeMediaQueryList.matches = true; // The OS prefers dark; Light must still win.

    await service.setMode(UiThemeModeEnum.Light);

    expect(service.resolvedColorScheme()).toBe('light');
  });

  it('System mode tracks a live prefers-color-scheme change, not just its value at load', async () => {
    appearanceApiSpy.apiAppearanceSettingsMeGet.and.returnValue(
      of({
        palette: null,
        mode: null,
        effectivePalette: UiThemePaletteEnum.Teal,
        effectiveMode: UiThemeModeEnum.System,
      }),
    );
    await service.initialize();

    expect(service.resolvedColorScheme()).toBe('light');

    fakeMediaQueryList.dispatchChange(true);
    expect(service.resolvedColorScheme()).toBe('dark');

    fakeMediaQueryList.dispatchChange(false);
    expect(service.resolvedColorScheme()).toBe('light');
  });

  it("applies the resolved palette/mode to the document root's own data attributes", async () => {
    appearanceApiSpy.apiAppearanceSettingsMePatch.and.returnValue(
      of({
        palette: UiThemePaletteEnum.Emerald,
        mode: UiThemeModeEnum.Dark,
        effectivePalette: UiThemePaletteEnum.Emerald,
        effectiveMode: UiThemeModeEnum.Dark,
      }),
    );

    await service.setPalette(UiThemePaletteEnum.Emerald);
    await service.setMode(UiThemeModeEnum.Dark);
    TestBed.flushEffects();

    expect(document.documentElement.getAttribute('data-ninlil-palette')).toBe('Emerald');
    expect(document.documentElement.getAttribute('data-coreui-theme')).toBe('dark');
  });
});

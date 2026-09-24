import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import {
  AppearanceSettingsService as AppearanceApiService,
  UiThemeModeEnum,
  UiThemePaletteEnum,
} from '../../api-client';
import { ThemeSwitcherComponent } from './theme-switcher.component';

/** See `ThemeService`'s own spec for why a plain Jasmine-spy fake is used here instead of `jasmine.SpyObj`. */
interface FakeAppearanceApiService {
  apiAppearanceSettingsMeGet: jasmine.Spy;
  apiAppearanceSettingsMePatch: jasmine.Spy;
}

describe('ThemeSwitcherComponent', () => {
  let appearanceApiSpy: FakeAppearanceApiService;

  async function createComponent() {
    appearanceApiSpy = {
      apiAppearanceSettingsMeGet: jasmine.createSpy('apiAppearanceSettingsMeGet'),
      apiAppearanceSettingsMePatch: jasmine.createSpy('apiAppearanceSettingsMePatch'),
    };

    await TestBed.configureTestingModule({
      imports: [ThemeSwitcherComponent],
      providers: [{ provide: AppearanceApiService, useValue: appearanceApiSpy }],
    }).compileComponents();

    const fixture = TestBed.createComponent(ThemeSwitcherComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders one option per palette and one per mode', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.paletteOptions.length).toBe(5);
    expect(fixture.componentInstance.modeOptions.length).toBe(3);

    const buttons = fixture.nativeElement.querySelectorAll('[cDropdownItem]');
    expect(buttons.length).toBe(8);
  });

  it('selectPalette() calls ThemeService.setPalette and reflects the change through activePalette', async () => {
    const fixture = await createComponent();
    appearanceApiSpy.apiAppearanceSettingsMePatch.and.returnValue(
      of({
        palette: UiThemePaletteEnum.Amber,
        mode: null,
        effectivePalette: UiThemePaletteEnum.Amber,
        effectiveMode: UiThemeModeEnum.System,
      }),
    );

    await fixture.componentInstance.selectPalette(UiThemePaletteEnum.Amber);

    expect(appearanceApiSpy.apiAppearanceSettingsMePatch).toHaveBeenCalledWith({
      body: { palette: UiThemePaletteEnum.Amber },
    });
    expect(fixture.componentInstance.activePalette()).toBe(UiThemePaletteEnum.Amber);
    expect(fixture.componentInstance.pending()).toBeFalse();
  });

  it('selectPalette() is a no-op when the palette clicked is already active', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.selectPalette(fixture.componentInstance.activePalette());

    expect(appearanceApiSpy.apiAppearanceSettingsMePatch).not.toHaveBeenCalled();
  });

  it('selectMode() calls ThemeService.setMode and updates triggerIcon() to match', async () => {
    const fixture = await createComponent();
    appearanceApiSpy.apiAppearanceSettingsMePatch.and.returnValue(
      of({
        palette: null,
        mode: UiThemeModeEnum.Dark,
        effectivePalette: UiThemePaletteEnum.Teal,
        effectiveMode: UiThemeModeEnum.Dark,
      }),
    );

    await fixture.componentInstance.selectMode(UiThemeModeEnum.Dark);

    expect(appearanceApiSpy.apiAppearanceSettingsMePatch).toHaveBeenCalledWith({
      body: { mode: UiThemeModeEnum.Dark },
    });
    expect(fixture.componentInstance.triggerIcon()).toBe('cilMoon');
  });
});

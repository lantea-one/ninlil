import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import {
  AppearanceSettingsService as AppearanceApiService,
  UiThemeModeEnum,
  UiThemePaletteEnum,
} from '../../api-client';
import { NotificationService } from '../notification/notification.service';
import { ThemeSwitcherComponent } from './theme-switcher.component';

/** A plain spy object rather than `jasmine.SpyObj`; the `AuthService` spec explains why. */
interface FakeAppearanceApiService {
  apiAppearanceSettingsMeGet: jasmine.Spy;
  apiAppearanceSettingsMePatch: jasmine.Spy;
}

/**
 * These tests cover the theme switcher: the options it lists and choosing a palette or mode,
 * through the real `ThemeService` backed by a fake appearance API.
 */
describe('ThemeSwitcherComponent', () => {
  let appearanceApiSpy: FakeAppearanceApiService;
  let notificationSpy: { error: jasmine.Spy };

  async function createComponent() {
    appearanceApiSpy = {
      apiAppearanceSettingsMeGet: jasmine.createSpy('apiAppearanceSettingsMeGet'),
      apiAppearanceSettingsMePatch: jasmine.createSpy('apiAppearanceSettingsMePatch'),
    };

    notificationSpy = { error: jasmine.createSpy('error') };

    await TestBed.configureTestingModule({
      imports: [ThemeSwitcherComponent],
      providers: [
        { provide: AppearanceApiService, useValue: appearanceApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
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

  /** A 500 response with an RFC 7807 problem body, as the API sends for a failed save. */
  function problem500(): HttpErrorResponse {
    return new HttpErrorResponse({
      status: 500,
      error: { title: 'Server error', detail: 'The preference could not be stored.' },
    });
  }

  it('selectPalette() reports a failed save and keeps the previous palette', async () => {
    const fixture = await createComponent();
    const before = fixture.componentInstance.activePalette();
    appearanceApiSpy.apiAppearanceSettingsMePatch.and.returnValue(throwError(() => problem500()));

    await fixture.componentInstance.selectPalette(UiThemePaletteEnum.Amber);

    expect(notificationSpy.error).toHaveBeenCalledWith('The preference could not be stored.');
    expect(fixture.componentInstance.pending()).toBeFalse();
    expect(fixture.componentInstance.activePalette()).toBe(before);
  });

  it('selectMode() reports a failed save and keeps the previous mode', async () => {
    const fixture = await createComponent();
    const before = fixture.componentInstance.activeMode();
    appearanceApiSpy.apiAppearanceSettingsMePatch.and.returnValue(throwError(() => problem500()));

    await fixture.componentInstance.selectMode(UiThemeModeEnum.Dark);

    expect(notificationSpy.error).toHaveBeenCalledWith('The preference could not be stored.');
    expect(fixture.componentInstance.pending()).toBeFalse();
    expect(fixture.componentInstance.activeMode()).toBe(before);
  });
});

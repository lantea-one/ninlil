import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import {
  AppearanceSettingsService as AppearanceApiService,
  UiThemeModeEnum,
  UiThemePaletteEnum,
} from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { AppearanceSettingsComponent } from './appearance-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Appearance page: loading, saving with the backend note, a save error and
 * the swatch preview. TestBed with a spy-backed `AppearanceSettingsService` and
 * `NotificationService`.
 */
describe('AppearanceSettingsComponent', () => {
  let apiSpy: {
    apiAppearanceSettingsGet: jasmine.Spy;
    apiAppearanceSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('AppearanceApiService', [
      'apiAppearanceSettingsGet',
      'apiAppearanceSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiAppearanceSettingsGet.and.returnValue(
      of({ palette: UiThemePaletteEnum.Teal, mode: UiThemeModeEnum.System }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [AppearanceSettingsComponent],
      providers: [
        { provide: AppearanceApiService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(AppearanceSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current global default appearance on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiAppearanceSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.palette.value).toBe(UiThemePaletteEnum.Teal);
    expect(fixture.componentInstance.form.controls.mode.value).toBe(UiThemeModeEnum.System);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes both fields, then surfaces the backend own hot-reload note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiAppearanceSettingsPatch.and.returnValue(
        of({ note: 'it takes effect immediately -- no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ palette: UiThemePaletteEnum.Rose });
    await fixture.componentInstance.save();

    expect(apiSpy.apiAppearanceSettingsPatch).toHaveBeenCalledWith({
      body: { palette: UiThemePaletteEnum.Rose, mode: UiThemeModeEnum.System },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiAppearanceSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad palette' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad palette');
  });

  it('selectedSwatch() reflects the currently-selected (not-yet-saved) palette', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.selectedSwatch()).toBe('#0f766e');

    fixture.componentInstance.form.patchValue({ palette: UiThemePaletteEnum.Rose });

    expect(fixture.componentInstance.selectedSwatch()).toBe('#be123c');
  });
});

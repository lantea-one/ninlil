import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { TsigSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { TsigSettingsComponent } from './tsig-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('TsigSettingsComponent', () => {
  let apiSpy: {
    apiTsigSettingsGet: jasmine.Spy;
    apiTsigSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('TsigSettingsService', [
      'apiTsigSettingsGet',
      'apiTsigSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiTsigSettingsGet.and.returnValue(of({ defaultFudgeSeconds: 300 }));
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [TsigSettingsComponent],
      providers: [
        { provide: TsigSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(TsigSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiTsigSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.defaultFudgeSeconds.value).toBe(300);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reloaded note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTsigSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ defaultFudgeSeconds: 600 });
    await fixture.componentInstance.save();

    expect(apiSpy.apiTsigSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ defaultFudgeSeconds: 600 }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTsigSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});

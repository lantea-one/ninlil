import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { RewriteSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { RewriteSettingsComponent } from './rewrite-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('RewriteSettingsComponent', () => {
  let apiSpy: {
    apiRewriteSettingsGet: jasmine.Spy;
    apiRewriteSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('RewriteSettingsService', [
      'apiRewriteSettingsGet',
      'apiRewriteSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiRewriteSettingsGet.and.returnValue(of({ enable: false, timeToLiveSeconds: 300 }));
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [RewriteSettingsComponent],
      providers: [
        { provide: RewriteSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RewriteSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiRewriteSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enable.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.timeToLiveSeconds.value).toBe(300);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reload note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRewriteSettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({ enable: true });
    await fixture.componentInstance.save();

    expect(apiSpy.apiRewriteSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ enable: true, timeToLiveSeconds: 300 }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('takes effect immediately'),
    );
  });

  it('rejects an out-of-range TTL', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ timeToLiveSeconds: -1 });

    expect(fixture.componentInstance.form.controls.timeToLiveSeconds.valid).toBeFalse();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRewriteSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});

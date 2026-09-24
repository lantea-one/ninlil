import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { SelfUpdateService, SelfUpdateSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { SelfUpdateSettingsComponent } from './self-update-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('SelfUpdateSettingsComponent', () => {
  let settingsApiSpy: {
    apiSelfUpdateSettingsGet: jasmine.Spy;
    apiSelfUpdateSettingsPatch: jasmine.Spy;
  };
  let statusApiSpy: {
    apiSelfUpdateGet: jasmine.Spy;
    apiSelfUpdateCheckPost: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    settingsApiSpy = jasmine.createSpyObj('SelfUpdateSettingsService', [
      'apiSelfUpdateSettingsGet',
      'apiSelfUpdateSettingsPatch',
    ]);
    statusApiSpy = jasmine.createSpyObj('SelfUpdateService', [
      'apiSelfUpdateGet',
      'apiSelfUpdateCheckPost',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    settingsApiSpy.apiSelfUpdateSettingsGet.and.returnValue(
      of({
        enableAutomaticCheck: true,
        releaseFeedUrl: '',
        checkInterval: '1.00:00:00',
      }),
    );
    statusApiSpy.apiSelfUpdateGet.and.returnValue(
      of({
        configured: false,
        currentVersion: '1.0.0.0',
        latestKnownVersion: null,
        updateAvailable: false,
        releaseUrl: null,
        releaseNotes: null,
        lastCheckedAtUtc: null,
        lastCheckError: null,
        updateInstructions: 'No release feed is configured yet.',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [SelfUpdateSettingsComponent],
      providers: [
        { provide: SelfUpdateSettingsService, useValue: settingsApiSpy },
        { provide: SelfUpdateService, useValue: statusApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SelfUpdateSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings and status on init', async () => {
    const fixture = await createComponent();

    expect(settingsApiSpy.apiSelfUpdateSettingsGet).toHaveBeenCalled();
    expect(statusApiSpy.apiSelfUpdateGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enableAutomaticCheck.value).toBeTrue();
    expect(fixture.componentInstance.form.controls.releaseFeedUrl.value).toBe('');
    expect(fixture.componentInstance.currentVersion()).toBe('1.0.0.0');
    expect(fixture.componentInstance.updateAvailable()).toBeFalse();
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the form', async () => {
    const fixture = await createComponent(() => {
      settingsApiSpy.apiSelfUpdateSettingsPatch.and.returnValue(
        of({ note: 'takes effect on the very next check tick -- no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({
      releaseFeedUrl: 'https://example.com/releases.json',
    });
    await fixture.componentInstance.save();

    expect(settingsApiSpy.apiSelfUpdateSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        releaseFeedUrl: 'https://example.com/releases.json',
      }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('no restart'));
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      settingsApiSpy.apiSelfUpdateSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad url' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad url');
  });

  it('checkNow() triggers a real check and updates the status card', async () => {
    const fixture = await createComponent(() => {
      statusApiSpy.apiSelfUpdateCheckPost.and.returnValue(
        of({
          configured: true,
          currentVersion: '1.0.0.0',
          latestKnownVersion: '2.0.0.0',
          updateAvailable: true,
          releaseUrl: 'https://example.com/2.0.0',
          releaseNotes: 'Notes.',
          lastCheckedAtUtc: '2026-09-04T00:00:00Z',
          lastCheckError: null,
          updateInstructions: 'Version 2.0.0.0 is available.',
        }),
      );
    });

    await fixture.componentInstance.checkNow();

    expect(statusApiSpy.apiSelfUpdateCheckPost).toHaveBeenCalled();
    expect(fixture.componentInstance.updateAvailable()).toBeTrue();
    expect(fixture.componentInstance.latestKnownVersion()).toBe('2.0.0.0');
    expect(notificationSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('2.0.0.0'));
  });

  it('shows an error message when checking now fails', async () => {
    const fixture = await createComponent(() => {
      statusApiSpy.apiSelfUpdateCheckPost.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 502, error: 'unreachable' })),
      );
    });

    await fixture.componentInstance.checkNow();

    expect(fixture.componentInstance.errorMessage()).toBe('unreachable');
  });
});

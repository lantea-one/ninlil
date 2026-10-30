import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { PtrSyncSettingsService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { PtrSyncSettingsComponent } from './ptr-sync-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the PTR Sync page: loading, saving, prefix-length validation and a save error.
 * TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('PtrSyncSettingsComponent', () => {
  let apiSpy: {
    apiPtrSyncSettingsGet: jasmine.Spy;
    apiPtrSyncSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('PtrSyncSettingsService', [
      'apiPtrSyncSettingsGet',
      'apiPtrSyncSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiPtrSyncSettingsGet.and.returnValue(
      of({
        defaultEnabled: false,
        defaultIpv4ReverseZonePrefixBits: 24,
        defaultIpv6ReverseZonePrefixBits: 64,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [PtrSyncSettingsComponent],
      providers: [
        { provide: PtrSyncSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    // An administrator's view: the write permission is held, so the Save button renders.
    spyOn(TestBed.inject(AuthService), 'hasPermission').and.returnValue(true);
    const fixture = TestBed.createComponent(PtrSyncSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective defaults on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiPtrSyncSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.defaultEnabled.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.defaultIpv4ReverseZonePrefixBits.value).toBe(24);
    expect(fixture.componentInstance.form.controls.defaultIpv6ReverseZonePrefixBits.value).toBe(64);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the toggle and prefix lengths, then surfaces the hot-reloaded note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiPtrSyncSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({
      defaultEnabled: true,
      defaultIpv4ReverseZonePrefixBits: 26,
    });
    await fixture.componentInstance.save();

    expect(apiSpy.apiPtrSyncSettingsPatch).toHaveBeenCalledWith({
      body: {
        defaultEnabled: true,
        defaultIpv4ReverseZonePrefixBits: 26,
        defaultIpv6ReverseZonePrefixBits: 64,
      },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('rejects an IPv4 prefix length outside 24-30', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.controls.defaultIpv4ReverseZonePrefixBits.setValue(31);

    expect(
      fixture.componentInstance.form.controls.defaultIpv4ReverseZonePrefixBits.hasError('max'),
    ).toBeTrue();
  });

  it('rejects an IPv6 prefix length outside 16-124', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.controls.defaultIpv6ReverseZonePrefixBits.setValue(8);

    expect(
      fixture.componentInstance.form.controls.defaultIpv6ReverseZonePrefixBits.hasError(
        'ptrIpv6PrefixLength',
      ),
    ).toBeTrue();
  });

  it('rejects an IPv6 prefix length that is not a multiple of 4', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.controls.defaultIpv6ReverseZonePrefixBits.setValue(62);

    expect(
      fixture.componentInstance.form.controls.defaultIpv6ReverseZonePrefixBits.hasError(
        'ptrIpv6PrefixLength',
      ),
    ).toBeTrue();
  });

  it('save() sends nothing while the form is invalid', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.controls.defaultIpv6ReverseZonePrefixBits.setValue(62);
    await fixture.componentInstance.save();

    expect(apiSpy.apiPtrSyncSettingsPatch).not.toHaveBeenCalled();
  });

  it('disables the Save button while the form is invalid and shows the multiple-of-4 error', async () => {
    const fixture = await createComponent();
    const element: HTMLElement = fixture.nativeElement;
    const saveButton = (): HTMLButtonElement | null =>
      element.querySelector('c-card-footer button');

    expect(saveButton()?.disabled).toBeFalse();

    fixture.componentInstance.form.controls.defaultIpv6ReverseZonePrefixBits.setValue(62);
    fixture.detectChanges();

    expect(element.textContent).toContain('multiple of 4');
    expect(saveButton()?.disabled).toBeTrue();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiPtrSyncSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});

import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { BrandingSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { BrandingSettingsComponent } from './branding-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * This function builds a synthetic DOM change event carrying a real, browser-native `File` --
 * {@link BrandingSettingsComponent.onLogoSelected} only ever reads `event.target.files` and
 * resets `event.target.value`, so a plain object shaped like the relevant slice of
 * `HTMLInputElement` is enough to exercise the real method without rendering an actual
 * `<input type="file">` and trying to script a real file selection into it (not possible from a
 * test at all, browser security restrictions aside).
 */
function fileSelectedEvent(file: File | null): Event {
  return { target: { files: file ? [file] : [], value: '' } } as unknown as Event;
}

describe('BrandingSettingsComponent', () => {
  let apiSpy: {
    apiBrandingSettingsGet: jasmine.Spy;
    apiBrandingSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('BrandingSettingsService', [
      'apiBrandingSettingsGet',
      'apiBrandingSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiBrandingSettingsGet.and.returnValue(
      of({
        siteTitle: null,
        logoDataUri: null,
        hostnameString: 'dns-server',
        versionString: 'Ninlil',
        enableChaosVersionQueries: false,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [BrandingSettingsComponent],
      providers: [
        { provide: BrandingSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(BrandingSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiBrandingSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.hostnameString.value).toBe('dns-server');
    expect(fixture.componentInstance.form.controls.enableChaosVersionQueries.value).toBeFalse();
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reload note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiBrandingSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... takes effect immediately.' }),
      );
    });

    fixture.componentInstance.form.patchValue({
      siteTitle: 'Acme Corp DNS',
      hostnameString: 'acme-ns1',
      enableChaosVersionQueries: true,
    });
    await fixture.componentInstance.save();

    expect(apiSpy.apiBrandingSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        siteTitle: 'Acme Corp DNS',
        hostnameString: 'acme-ns1',
        enableChaosVersionQueries: true,
      }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('takes effect immediately'),
    );
  });

  it('save() sends an explicit null siteTitle when the field is left blank', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiBrandingSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ siteTitle: '   ' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiBrandingSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ siteTitle: null }),
    });
  });

  it('rejects a blank hostname', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ hostnameString: '' });

    expect(fixture.componentInstance.form.controls.hostnameString.valid).toBeFalse();
    expect(fixture.componentInstance.form.invalid).toBeTrue();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiBrandingSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });

  describe('logo upload', () => {
    it('onLogoSelected() reads a real, valid image file into a data URI and writes it onto the form', async () => {
      const fixture = await createComponent();
      const file = new File(['fake-png-bytes'], 'logo.png', { type: 'image/png' });

      await fixture.componentInstance.onLogoSelected(fileSelectedEvent(file));

      const logoDataUri = fixture.componentInstance.form.controls.logoDataUri.value;
      expect(logoDataUri).toContain('data:image/png;base64,');
      expect(fixture.componentInstance.form.controls.logoDataUri.dirty).toBeTrue();
      expect(fixture.componentInstance.logoFileError()).toBeNull();
    });

    it('onLogoSelected() rejects a non-image file without touching the form', async () => {
      const fixture = await createComponent();
      const file = new File(['hello'], 'notes.txt', { type: 'text/plain' });

      await fixture.componentInstance.onLogoSelected(fileSelectedEvent(file));

      expect(fixture.componentInstance.form.controls.logoDataUri.value).toBeNull();
      expect(fixture.componentInstance.logoFileError()).toContain('not an image');
    });

    it('onLogoSelected() does nothing when no file was actually selected', async () => {
      const fixture = await createComponent();

      await fixture.componentInstance.onLogoSelected(fileSelectedEvent(null));

      expect(fixture.componentInstance.form.controls.logoDataUri.value).toBeNull();
      expect(fixture.componentInstance.logoFileError()).toBeNull();
    });

    it('removeLogo() clears a previously set logo and marks the control dirty', async () => {
      const fixture = await createComponent();
      fixture.componentInstance.form.controls.logoDataUri.setValue('data:image/png;base64,AAAA');

      fixture.componentInstance.removeLogo();

      expect(fixture.componentInstance.form.controls.logoDataUri.value).toBeNull();
      expect(fixture.componentInstance.form.controls.logoDataUri.dirty).toBeTrue();
    });
  });
});

import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { FilteringSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { FilteringSettingsComponent } from './filtering-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Filtering page: loading, saving, the CNAME-cloaking toggle and its default,
 * sinkhole validation and a save error. TestBed with a spy-backed settings service and
 * `NotificationService`.
 */
describe('FilteringSettingsComponent', () => {
  let apiSpy: {
    apiFilteringSettingsGet: jasmine.Spy;
    apiFilteringSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('FilteringSettingsService', [
      'apiFilteringSettingsGet',
      'apiFilteringSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiFilteringSettingsGet.and.returnValue(
      of({
        enable: false,
        blockResponseMode: 'NxDomain',
        sinkholeAddressV4: '0.0.0.0',
        sinkholeAddressV6: '::',
        sinkholeTimeToLiveSeconds: 60,
        precedence: 'AllowWins',
        blockCnameCloaking: true,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [FilteringSettingsComponent],
      providers: [
        { provide: FilteringSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(FilteringSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiFilteringSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enable.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.sinkholeTimeToLiveSeconds.value).toBe(60);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reload note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiFilteringSettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({ enable: true });
    await fixture.componentInstance.save();

    expect(apiSpy.apiFilteringSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ enable: true, precedence: 'AllowWins' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('takes effect immediately'),
    );
  });

  it('loads and saves the CNAME-cloaking toggle', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiFilteringSettingsGet.and.returnValue(
        of({ enable: true, precedence: 'AllowWins', blockCnameCloaking: false }),
      );
      apiSpy.apiFilteringSettingsPatch.and.returnValue(of({ note: 'Saved.' }));
    });

    expect(fixture.componentInstance.form.controls.blockCnameCloaking.value).toBeFalse();

    const checkbox: HTMLInputElement = fixture.nativeElement.querySelector(
      '#filtering-block-cname-cloaking',
    );
    checkbox.click();
    fixture.detectChanges();
    await fixture.componentInstance.save();

    expect(apiSpy.apiFilteringSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ blockCnameCloaking: true }),
    });
  });

  it('defaults the CNAME-cloaking toggle to on when the server omits it', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiFilteringSettingsGet.and.returnValue(of({ enable: false }));
    });

    expect(fixture.componentInstance.form.controls.blockCnameCloaking.value).toBeTrue();
  });

  it('rejects an out-of-range sinkhole TTL', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ sinkholeTimeToLiveSeconds: 4294967296 });

    expect(fixture.componentInstance.form.controls.sinkholeTimeToLiveSeconds.valid).toBeFalse();
    expect(fixture.componentInstance.form.invalid).toBeTrue();
  });

  it('rejects a malformed sinkhole IPv4 address', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ sinkholeAddressV4: 'not-an-ip' });

    expect(fixture.componentInstance.form.controls.sinkholeAddressV4.valid).toBeFalse();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiFilteringSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});

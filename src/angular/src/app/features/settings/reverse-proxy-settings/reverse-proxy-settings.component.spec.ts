import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ReverseProxySettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ReverseProxySettingsComponent } from './reverse-proxy-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ReverseProxySettingsComponent', () => {
  let apiSpy: {
    apiReverseProxySettingsGet: jasmine.Spy;
    apiReverseProxySettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('ReverseProxySettingsService', [
      'apiReverseProxySettingsGet',
      'apiReverseProxySettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiReverseProxySettingsGet.and.returnValue(
      of({
        enable: false,
        knownProxies: ['203.0.113.10'],
        knownNetworks: ['10.0.0.0/24'],
        sharedSecretHeaderName: 'X-Ninlil-Proxy-Secret',
        sharedSecretConfigured: false,
        dnsOverTls: {
          enable: false,
          knownProxies: ['198.51.100.5'],
          knownNetworks: ['172.16.0.0/16'],
        },
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [ReverseProxySettingsComponent],
      providers: [
        { provide: ReverseProxySettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ReverseProxySettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init, splitting each list into one line each', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiReverseProxySettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.knownProxiesText.value).toBe('203.0.113.10');
    expect(fixture.componentInstance.form.controls.knownNetworksText.value).toBe('10.0.0.0/24');
    expect(fixture.componentInstance.sharedSecretConfigured()).toBeFalse();
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('loads the DNS-over-TLS PROXY protocol settings on init into their own dedicated fields', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.form.controls.dnsOverTlsEnable.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.dnsOverTlsKnownProxiesText.value).toBe(
      '198.51.100.5',
    );
    expect(fixture.componentInstance.form.controls.dnsOverTlsKnownNetworksText.value).toBe(
      '172.16.0.0/16',
    );
  });

  it('loads the DNS-over-TLS section with empty defaults when the backend omits it entirely', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiReverseProxySettingsGet.and.returnValue(
        of({
          enable: false,
          knownProxies: [],
          knownNetworks: [],
          sharedSecretHeaderName: 'X-Ninlil-Proxy-Secret',
          sharedSecretConfigured: false,
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.dnsOverTlsEnable.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.dnsOverTlsKnownProxiesText.value).toBe('');
    expect(fixture.componentInstance.form.controls.dnsOverTlsKnownNetworksText.value).toBe('');
  });

  it('loads sharedSecretConfigured as true when the backend reports one is configured', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiReverseProxySettingsGet.and.returnValue(
        of({
          enable: true,
          knownProxies: [],
          knownNetworks: [],
          sharedSecretHeaderName: 'X-Ninlil-Proxy-Secret',
          sharedSecretConfigured: true,
        }),
      );
    });

    expect(fixture.componentInstance.sharedSecretConfigured()).toBeTrue();
  });

  it('save() joins each textarea back into an array and PATCHes it, surfacing the restart-required note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiReverseProxySettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... a restart is required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({
      enable: true,
      knownProxiesText: '203.0.113.10\n203.0.113.11',
    });
    await fixture.componentInstance.save();

    expect(apiSpy.apiReverseProxySettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        enable: true,
        knownProxies: ['203.0.113.10', '203.0.113.11'],
        knownNetworks: ['10.0.0.0/24'],
      }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('a restart is required'),
    );
  });

  it('save() always includes the dnsOverTls sub-object, joining its own two textareas back into arrays', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiReverseProxySettingsPatch.and.returnValue(of({ note: 'saved' }));
    });

    fixture.componentInstance.form.patchValue({
      dnsOverTlsEnable: true,
      dnsOverTlsKnownProxiesText: '198.51.100.5\n198.51.100.6',
      dnsOverTlsKnownNetworksText: '172.16.0.0/16',
    });
    await fixture.componentInstance.save();

    expect(apiSpy.apiReverseProxySettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        dnsOverTls: {
          enable: true,
          knownProxies: ['198.51.100.5', '198.51.100.6'],
          knownNetworks: ['172.16.0.0/16'],
        },
      }),
    });
  });

  it('save() with a blank shared secret omits the sharedSecret key entirely, leaving the stored value untouched', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiReverseProxySettingsPatch.and.returnValue(of({ note: 'saved' }));
    });

    await fixture.componentInstance.save();

    const body = apiSpy.apiReverseProxySettingsPatch.calls.mostRecent().args[0].body;
    expect('sharedSecret' in body).toBeFalse();
  });

  it('save() with a non-blank shared secret includes it in the patch body, then clears the field back to blank', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiReverseProxySettingsPatch.and.returnValue(of({ note: 'saved' }));
    });

    fixture.componentInstance.form.patchValue({ sharedSecret: 'new-secret' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiReverseProxySettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ sharedSecret: 'new-secret' }),
    });
    expect(fixture.componentInstance.form.controls.sharedSecret.value).toBe('');
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiReverseProxySettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });

  it('shows an error message when the initial load fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiReverseProxySettingsGet.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
      );
    });

    expect(fixture.componentInstance.errorMessage()).toBe('boom');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });
});

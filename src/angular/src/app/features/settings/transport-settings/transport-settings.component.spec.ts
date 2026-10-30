import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { TransportSettingsService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { TransportSettingsComponent } from './transport-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Transports page: loading every card, each card's PATCH and note, the
 * certificate fields and PFX password, listen addresses and errors. TestBed with a spy-backed
 * `TransportSettingsService` and `NotificationService`.
 */
describe('TransportSettingsComponent', () => {
  let apiSpy: {
    apiTransportSettingsPlainGet: jasmine.Spy;
    apiTransportSettingsPlainPatch: jasmine.Spy;
    apiTransportSettingsDnsOverTlsGet: jasmine.Spy;
    apiTransportSettingsDnsOverTlsPatch: jasmine.Spy;
    apiTransportSettingsDnsOverHttpsGet: jasmine.Spy;
    apiTransportSettingsDnsOverHttpsPatch: jasmine.Spy;
    apiTransportSettingsDnsOverQuicGet: jasmine.Spy;
    apiTransportSettingsDnsOverQuicPatch: jasmine.Spy;
    apiTransportSettingsDnscryptGet: jasmine.Spy;
    apiTransportSettingsDnscryptPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('TransportSettingsService', [
      'apiTransportSettingsPlainGet',
      'apiTransportSettingsPlainPatch',
      'apiTransportSettingsDnsOverTlsGet',
      'apiTransportSettingsDnsOverTlsPatch',
      'apiTransportSettingsDnsOverHttpsGet',
      'apiTransportSettingsDnsOverHttpsPatch',
      'apiTransportSettingsDnsOverQuicGet',
      'apiTransportSettingsDnsOverQuicPatch',
      'apiTransportSettingsDnscryptGet',
      'apiTransportSettingsDnscryptPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiTransportSettingsPlainGet.and.returnValue(
      of({
        port: 53,
        listenAddresses: ['127.0.0.1', '::1'],
        tcpIdleTimeout: '00:00:30',
        maxConcurrentTcpConnections: 512,
      }),
    );
    apiSpy.apiTransportSettingsDnsOverTlsGet.and.returnValue(
      of({
        enable: false,
        port: 853,
        idleTimeout: '00:00:30',
        maxConcurrentConnections: 512,
        certificate: {
          pfxPath: '',
          pfxPasswordConfigured: false,
          selfSignedSubjectAlternativeNames: ['localhost'],
          certificateReloadPollInterval: '00:05:00',
          allowedProtocols: [],
        },
      }),
    );
    apiSpy.apiTransportSettingsDnsOverHttpsGet.and.returnValue(
      of({ enable: false, path: '/dns-query', maxMessageSizeBytes: 65535 }),
    );
    apiSpy.apiTransportSettingsDnsOverQuicGet.and.returnValue(
      of({
        enable: false,
        port: 853,
        idleTimeout: '00:00:30',
        maxConcurrentBidirectionalStreams: 100,
        maxConcurrentConnections: 512,
        certificate: {
          pfxPath: '',
          pfxPasswordConfigured: false,
          selfSignedSubjectAlternativeNames: ['localhost'],
          certificateReloadPollInterval: '00:05:00',
          allowedProtocols: [],
        },
      }),
    );
    apiSpy.apiTransportSettingsDnscryptGet.and.returnValue(
      of({
        enable: false,
        port: 5443,
        providerName: '',
        tcpIdleTimeout: '00:00:30',
        maxConcurrentTcpConnections: 512,
        enabledEsVersions: ['XChaCha20Poly1305', 'XSalsa20Poly1305'],
        certificateRotationInterval: '1.00:00:00',
        certificateValidityDuration: '2.00:00:00',
        certificateClockSkewTolerance: '00:05:00',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [TransportSettingsComponent],
      providers: [
        { provide: TransportSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    // The write footer sits behind `*appIfPermission`, so the specs that read the Save button need
    // the permission granted before the first render.
    spyOn(TestBed.inject(AuthService), 'hasPermission').and.returnValue(true);

    const fixture = TestBed.createComponent(TransportSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads every transport on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiTransportSettingsPlainGet).toHaveBeenCalled();
    expect(apiSpy.apiTransportSettingsDnsOverTlsGet).toHaveBeenCalled();
    expect(apiSpy.apiTransportSettingsDnsOverHttpsGet).toHaveBeenCalled();
    expect(apiSpy.apiTransportSettingsDnsOverQuicGet).toHaveBeenCalled();
    expect(apiSpy.apiTransportSettingsDnscryptGet).toHaveBeenCalled();
    expect(fixture.componentInstance.plainForm.controls.port.value).toBe(53);
    expect(fixture.componentInstance.dnsOverTlsForm.controls.enable.value).toBeFalse();
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('savePlain() PATCHes the restart-required fields, then surfaces the note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsPlainPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. This setting is bound via IOptions, not IOptionsMonitor, so a restart is required before this change takes effect.',
        }),
      );
    });

    fixture.componentInstance.plainForm.patchValue({ maxConcurrentTcpConnections: 256 });
    await fixture.componentInstance.savePlain();

    expect(apiSpy.apiTransportSettingsPlainPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ port: 53, maxConcurrentTcpConnections: 256 }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('restart is required'),
    );
  });

  it('saveDnsOverTls() PATCHes only enable when it alone changed, and shows the hot note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnsOverTlsPatch.and.returnValue(
        of({
          note: "Written to the configuration file, and the underlying transport listener was started or stopped to match -- this transport's enabled/disabled state took effect immediately, no restart required.",
        }),
      );
    });

    fixture.componentInstance.dnsOverTlsForm.patchValue({ enable: true });
    await fixture.componentInstance.saveDnsOverTls();

    // The controller names a restart whenever a restart-requiring field is present, so the
    // untouched port, limits and certificate must stay out of the body.
    expect(apiSpy.apiTransportSettingsDnsOverTlsPatch).toHaveBeenCalledWith({
      body: { enable: true },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('took effect immediately'),
    );
  });

  it('saveDnsOverTls() sends the whole certificate when one certificate field changed, without a blank pfxPassword', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnsOverTlsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.dnsOverTlsForm.patchValue({
      allowedProtocolTls12: true,
      allowedProtocolTls13: true,
    });
    await fixture.componentInstance.saveDnsOverTls();

    expect(apiSpy.apiTransportSettingsDnsOverTlsPatch).toHaveBeenCalledWith({
      body: {
        certificate: {
          pfxPath: '',
          selfSignedSubjectAlternativeNames: ['localhost'],
          certificateReloadPollInterval: '00:05:00',
          allowedProtocols: ['Tls12', 'Tls13'],
        },
      },
    });

    // The blank pfxPassword is left out of the certificate, not sent as an empty string that
    // would clear the stored password.
    const body = apiSpy.apiTransportSettingsDnsOverTlsPatch.calls.mostRecent().args[0].body;
    expect('pfxPassword' in body.certificate).toBeFalse();
  });

  it('saveDnsOverTls() includes pfxPassword when the operator actually typed one', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnsOverTlsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.dnsOverTlsForm.patchValue({ pfxPassword: 'a-real-password' });
    await fixture.componentInstance.saveDnsOverTls();

    const body = apiSpy.apiTransportSettingsDnsOverTlsPatch.calls.mostRecent().args[0].body;
    expect(Object.keys(body)).toEqual(['certificate']);
    expect(body.certificate.pfxPassword).toBe('a-real-password');
  });

  it('saveDnsOverTls() clears the typed password and moves the baseline, so it is not sent again', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnsOverTlsPatch.and.returnValue(of({ note: 'ok' }));
    });
    const component = fixture.componentInstance;

    component.dnsOverTlsForm.patchValue({ pfxPassword: 'a-real-password' });
    await component.saveDnsOverTls();

    expect(component.dnsOverTlsForm.controls.pfxPassword.value).toBe('');
    expect(component.dnsOverTlsHasChanges()).toBeFalse();

    component.dnsOverTlsForm.patchValue({ port: 8853 });
    await component.saveDnsOverTls();

    expect(apiSpy.apiTransportSettingsDnsOverTlsPatch.calls.mostRecent().args[0]).toEqual({
      body: { port: 8853 },
    });
  });

  it('saveDnsOverHttps() PATCHes only the edited field', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnsOverHttpsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.dnsOverHttpsForm.patchValue({ path: '/doh' });
    await fixture.componentInstance.saveDnsOverHttps();

    expect(apiSpy.apiTransportSettingsDnsOverHttpsPatch).toHaveBeenCalledWith({
      body: { path: '/doh' },
    });
    expect(fixture.componentInstance.dnsOverHttpsHasChanges()).toBeFalse();
  });

  it('saveDnsOverQuic() loads the connection limit and sends only an edited value, and rejects 0', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnsOverQuicPatch.and.returnValue(of({ note: 'ok' }));
    });

    expect(fixture.componentInstance.dnsOverQuicForm.controls.maxConcurrentConnections.value).toBe(
      512,
    );

    fixture.componentInstance.dnsOverQuicForm.patchValue({ maxConcurrentConnections: 0 });
    expect(fixture.componentInstance.dnsOverQuicForm.invalid).toBeTrue();

    fixture.componentInstance.dnsOverQuicForm.patchValue({ maxConcurrentConnections: 64 });
    await fixture.componentInstance.saveDnsOverQuic();

    expect(apiSpy.apiTransportSettingsDnsOverQuicPatch).toHaveBeenCalledWith({
      body: { maxConcurrentConnections: 64 },
    });
  });

  it('saveDnsOverQuic() sends enable alone so the note is the hot one', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnsOverQuicPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.dnsOverQuicForm.patchValue({ enable: true });
    await fixture.componentInstance.saveDnsOverQuic();

    expect(apiSpy.apiTransportSettingsDnsOverQuicPatch).toHaveBeenCalledWith({
      body: { enable: true },
    });
  });

  it('saveDnsCrypt() sends the whole cipher list when one checkbox changed', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnscryptPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.dnsCryptForm.patchValue({ esVersionXSalsa20Poly1305: false });
    await fixture.componentInstance.saveDnsCrypt();

    // A merge patch replaces an array as one value (RFC 7396 section 2).
    expect(apiSpy.apiTransportSettingsDnscryptPatch).toHaveBeenCalledWith({
      body: { enabledEsVersions: ['XChaCha20Poly1305'] },
    });
  });

  it('saveDnsCrypt() sends a scalar edit alone', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnscryptPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.dnsCryptForm.patchValue({ providerName: '2.dnscrypt-cert.example' });
    await fixture.componentInstance.saveDnsCrypt();

    expect(apiSpy.apiTransportSettingsDnscryptPatch).toHaveBeenCalledWith({
      body: { providerName: '2.dnscrypt-cert.example' },
    });
  });

  it('the four changed-only cards do nothing and disable Save while nothing changed', async () => {
    const fixture = await createComponent();
    const component = fixture.componentInstance;
    const saveButton = (id: string): HTMLButtonElement =>
      (fixture.nativeElement as HTMLElement)
        .querySelector(`#${id}`)!
        .closest('c-card')!
        .querySelector('c-card-footer button')!;

    await component.saveDnsOverTls();
    await component.saveDnsOverHttps();
    await component.saveDnsOverQuic();
    await component.saveDnsCrypt();

    expect(apiSpy.apiTransportSettingsDnsOverTlsPatch).not.toHaveBeenCalled();
    expect(apiSpy.apiTransportSettingsDnsOverHttpsPatch).not.toHaveBeenCalled();
    expect(apiSpy.apiTransportSettingsDnsOverQuicPatch).not.toHaveBeenCalled();
    expect(apiSpy.apiTransportSettingsDnscryptPatch).not.toHaveBeenCalled();

    for (const id of ['dot-port', 'doh-path', 'doq-port', 'dnscrypt-port']) {
      const button = saveButton(id);
      expect(button.disabled).withContext(id).toBeTrue();
      expect(button.title).withContext(id).toBe('No changes to save');
    }

    component.dnsCryptForm.patchValue({ port: 5444 });
    fixture.detectChanges();
    expect(saveButton('dnscrypt-port').disabled).toBeFalse();
    expect(saveButton('doh-path').disabled).toBeTrue();
  });

  it('shows an error message when loading fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsPlainGet.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
      );
    });

    expect(fixture.componentInstance.loadErrorMessage()).toBe('boom');
  });

  it('shows a per-card error message when saving DNSCrypt fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnscryptPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad provider name' })),
      );
    });

    fixture.componentInstance.dnsCryptForm.patchValue({ providerName: 'bad' });
    await fixture.componentInstance.saveDnsCrypt();

    expect(fixture.componentInstance.dnsCryptErrorMessage()).toBe('bad provider name');
  });
  it("loads each transport's listen addresses one per line, and sends them back as an array", async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsPlainPatch.and.returnValue(of({ note: 'Saved.' }));
    });
    const component = fixture.componentInstance;

    expect(component.plainForm.controls.listenAddresses.value).toBe('127.0.0.1\n::1');

    component.plainForm.patchValue({ listenAddresses: '192.168.1.1, ::1\n\n' });
    await component.savePlain();

    expect(apiSpy.apiTransportSettingsPlainPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ listenAddresses: ['192.168.1.1', '::1'] }),
    });
  });

  it('sends an empty list, meaning every address, when the field is cleared', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnsOverTlsGet.and.returnValue(
        of({ enable: false, port: 853, listenAddresses: ['192.0.2.1'] }),
      );
      apiSpy.apiTransportSettingsDnsOverTlsPatch.and.returnValue(of({ note: 'Saved.' }));
    });

    fixture.componentInstance.dnsOverTlsForm.patchValue({ listenAddresses: '   ' });
    await fixture.componentInstance.saveDnsOverTls();

    expect(apiSpy.apiTransportSettingsDnsOverTlsPatch).toHaveBeenCalledWith({
      body: { listenAddresses: [] },
    });
  });
});

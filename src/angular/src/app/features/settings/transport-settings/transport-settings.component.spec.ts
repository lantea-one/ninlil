import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { TransportSettingsService } from '../../../api-client';
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
        port: 5353,
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

  it('saveDnsOverTls() PATCHes enable + certificate, omitting a blank pfxPassword', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnsOverTlsPatch.and.returnValue(
        of({
          note: "Written to the configuration file, and the underlying transport listener was started or stopped to match -- this transport's enabled/disabled state took effect immediately, no restart required.",
        }),
      );
    });

    fixture.componentInstance.dnsOverTlsForm.patchValue({
      enable: true,
      allowedProtocolTls12: true,
      allowedProtocolTls13: true,
    });
    await fixture.componentInstance.saveDnsOverTls();

    expect(apiSpy.apiTransportSettingsDnsOverTlsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        enable: true,
        certificate: jasmine.objectContaining({
          allowedProtocols: ['Tls12', 'Tls13'],
          selfSignedSubjectAlternativeNames: ['localhost'],
        }),
      }),
    });

    // The blank pfxPassword is left out of the certificate, not sent as an empty string that
    // would clear the stored password.
    const body = apiSpy.apiTransportSettingsDnsOverTlsPatch.calls.mostRecent().args[0].body;
    expect('pfxPassword' in body.certificate).toBeFalse();

    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('took effect immediately'),
    );
  });

  it('saveDnsOverTls() includes pfxPassword when the operator actually typed one', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnsOverTlsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.dnsOverTlsForm.patchValue({ pfxPassword: 'a-real-password' });
    await fixture.componentInstance.saveDnsOverTls();

    const body = apiSpy.apiTransportSettingsDnsOverTlsPatch.calls.mostRecent().args[0].body;
    expect(body.certificate.pfxPassword).toBe('a-real-password');
  });

  it('saveDnsCrypt() builds enabledEsVersions from the checked ciphers', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiTransportSettingsDnscryptPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.dnsCryptForm.patchValue({
      esVersionXChaCha20Poly1305: true,
      esVersionXSalsa20Poly1305: false,
    });
    await fixture.componentInstance.saveDnsCrypt();

    expect(apiSpy.apiTransportSettingsDnscryptPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ enabledEsVersions: ['XChaCha20Poly1305'] }),
    });
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
      apiSpy.apiTransportSettingsDnsOverTlsPatch.and.returnValue(of({ note: 'Saved.' }));
    });

    fixture.componentInstance.dnsOverTlsForm.patchValue({ listenAddresses: '   ' });
    await fixture.componentInstance.saveDnsOverTls();

    expect(apiSpy.apiTransportSettingsDnsOverTlsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ listenAddresses: [] }),
    });
  });
});

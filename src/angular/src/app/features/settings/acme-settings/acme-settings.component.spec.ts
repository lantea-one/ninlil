import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { AcmeSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { AcmeSettingsComponent } from './acme-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('AcmeSettingsComponent', () => {
  let apiSpy: {
    apiAcmeSettingsGet: jasmine.Spy;
    apiAcmeSettingsPatch: jasmine.Spy;
    apiAcmeSettingsCertificatesGet: jasmine.Spy;
    apiAcmeSettingsCertificatesDomainIssuePost: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('AcmeSettingsService', [
      'apiAcmeSettingsGet',
      'apiAcmeSettingsPatch',
      'apiAcmeSettingsCertificatesGet',
      'apiAcmeSettingsCertificatesDomainIssuePost',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiAcmeSettingsGet.and.returnValue(
      of({
        enable: false,
        directoryUri: 'https://acme-v02.api.letsencrypt.org/directory',
        contactEmails: [],
        domains: [],
        renewBeforeExpiration: '30.00:00:00',
        pollInterval: '12:00:00',
        accountStateFilePath: '/data/acme-account.json',
        certificateDirectoryPath: '/data/acme-certificates',
        csrOrganization: 'Ninlil',
        caaAutoManagement: { defaultEnabled: true, defaultIssuerDomain: '' },
      }),
    );
    apiSpy.apiAcmeSettingsCertificatesGet.and.returnValue(of([]));
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [AcmeSettingsComponent],
      providers: [
        { provide: AcmeSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(AcmeSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings and certificate inventory on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiAcmeSettingsGet).toHaveBeenCalled();
    expect(apiSpy.apiAcmeSettingsCertificatesGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enable.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.csrOrganization.value).toBe('Ninlil');
    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.componentInstance.certificatesLoading()).toBeFalse();
  });

  it('save() splits the domains/contact-emails textareas into arrays and PATCHes, then surfaces the note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiAcmeSettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({
      enable: true,
      domainsText: 'dns.example.com\n\ndns2.example.com\n',
      contactEmailsText: 'admin@example.com',
    });
    await fixture.componentInstance.save();

    expect(apiSpy.apiAcmeSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        enable: true,
        domains: ['dns.example.com', 'dns2.example.com'],
        contactEmails: ['admin@example.com'],
      }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('takes effect immediately'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiAcmeSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });

  it('issueOrRenewNow() drives the manual trigger, refreshes the inventory, and reports a real failure', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiAcmeSettingsCertificatesDomainIssuePost.and.returnValue(
        of({
          domain: 'dns.example.com',
          hasCertificate: false,
          issuedAtUtc: null,
          expiresAtUtc: null,
          isDueForRenewal: true,
          lastFailureAtUtc: '2026-09-02T00:00:00Z',
          lastFailureMessage: 'Connection refused (127.0.0.1:1)',
        }),
      );
    });

    await fixture.componentInstance.issueOrRenewNow('dns.example.com');

    expect(apiSpy.apiAcmeSettingsCertificatesDomainIssuePost).toHaveBeenCalledWith({
      domain: 'dns.example.com',
    });
    expect(notificationSpy.error).toHaveBeenCalledWith(
      jasmine.stringContaining('Connection refused'),
    );
    expect(apiSpy.apiAcmeSettingsCertificatesGet).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.issuingDomain()).toBeNull();
  });

  it('shows an error message when the certificate inventory fails to load', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiAcmeSettingsCertificatesGet.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
      );
    });

    expect(fixture.componentInstance.certificatesErrorMessage()).toBe('boom');
  });
  it('shows certificate dates formatted for reading, and when the last failure happened', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiAcmeSettingsCertificatesGet.and.returnValue(
        of([
          {
            domain: 'dns.example.com',
            hasCertificate: true,
            issuedAtUtc: '2026-06-01T08:00:00Z',
            expiresAtUtc: '2026-08-30T08:00:00Z',
            isDueForRenewal: true,
            lastFailureAtUtc: '2026-08-01T08:00:00Z',
            lastFailureMessage: 'Rate limited by the CA',
          },
        ]),
      );
    });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Rate limited by the CA');
    expect(fixture.nativeElement.querySelector('.failure-time')).not.toBeNull();
    expect(text).not.toContain('2026-06-01T08:00:00Z');
    expect(text).not.toContain('2026-08-30T08:00:00Z');
  });
});

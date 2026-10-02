import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  SmtpSettingsService as SmtpSettingsApiService,
  TransportSettingsService as TransportSettingsApiService,
  UsersService as UsersApiService,
  ZonesService as ZonesApiService,
} from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { GettingStartedChecklistService } from './getting-started-checklist.service';

/**
 * These tests cover each checklist row's completion rule, leaving out rows without permission,
 * and a failed check marking only its row incomplete. TestBed with spy-backed API services and a
 * fake `AuthService`.
 */
describe('GettingStartedChecklistService', () => {
  let service: GettingStartedChecklistService;
  let authServiceSpy: { hasPermission: jasmine.Spy };
  let zonesApiSpy: { apiZonesGet: jasmine.Spy };
  let usersApiSpy: { apiUsersGet: jasmine.Spy };
  let transportSettingsApiSpy: {
    apiTransportSettingsDnsOverTlsGet: jasmine.Spy;
    apiTransportSettingsDnsOverHttpsGet: jasmine.Spy;
    apiTransportSettingsDnsOverQuicGet: jasmine.Spy;
    apiTransportSettingsDnscryptGet: jasmine.Spy;
  };
  let smtpSettingsApiSpy: { apiSmtpSettingsGet: jasmine.Spy };

  /** Grants all four of the checklist's permissions unless a test overrides it. */
  function allowAllPermissions(): void {
    authServiceSpy.hasPermission.and.returnValue(true);
  }

  /** Makes every transport GET report `enable: false` unless a test overrides one. */
  function stubAllTransportsDisabled(): void {
    transportSettingsApiSpy.apiTransportSettingsDnsOverTlsGet.and.returnValue(
      of({ enable: false }),
    );
    transportSettingsApiSpy.apiTransportSettingsDnsOverHttpsGet.and.returnValue(
      of({ enable: false }),
    );
    transportSettingsApiSpy.apiTransportSettingsDnsOverQuicGet.and.returnValue(
      of({ enable: false }),
    );
    transportSettingsApiSpy.apiTransportSettingsDnscryptGet.and.returnValue(of({ enable: false }));
  }

  beforeEach(() => {
    authServiceSpy = { hasPermission: jasmine.createSpy('hasPermission') };
    zonesApiSpy = { apiZonesGet: jasmine.createSpy('apiZonesGet') };
    usersApiSpy = { apiUsersGet: jasmine.createSpy('apiUsersGet') };
    transportSettingsApiSpy = {
      apiTransportSettingsDnsOverTlsGet: jasmine.createSpy('apiTransportSettingsDnsOverTlsGet'),
      apiTransportSettingsDnsOverHttpsGet: jasmine.createSpy('apiTransportSettingsDnsOverHttpsGet'),
      apiTransportSettingsDnsOverQuicGet: jasmine.createSpy('apiTransportSettingsDnsOverQuicGet'),
      apiTransportSettingsDnscryptGet: jasmine.createSpy('apiTransportSettingsDnscryptGet'),
    };
    smtpSettingsApiSpy = { apiSmtpSettingsGet: jasmine.createSpy('apiSmtpSettingsGet') };

    TestBed.configureTestingModule({
      providers: [
        GettingStartedChecklistService,
        { provide: AuthService, useValue: authServiceSpy },
        { provide: ZonesApiService, useValue: zonesApiSpy },
        { provide: UsersApiService, useValue: usersApiSpy },
        { provide: TransportSettingsApiService, useValue: transportSettingsApiSpy },
        { provide: SmtpSettingsApiService, useValue: smtpSettingsApiSpy },
      ],
    });

    service = TestBed.inject(GettingStartedChecklistService);
  });

  it('omits a row entirely when the session lacks its underlying permission', async () => {
    authServiceSpy.hasPermission.and.returnValue(false);

    const items = await service.fetchItems();

    expect(items).toEqual([]);
    expect(zonesApiSpy.apiZonesGet).not.toHaveBeenCalled();
  });

  it('marks "Add your first zone" complete once the zone count is above 0, linking to /zones', async () => {
    allowAllPermissions();
    zonesApiSpy.apiZonesGet.and.returnValue(of({ totalCount: 1 }));
    usersApiSpy.apiUsersGet.and.returnValue(of({ totalCount: 1 }));
    stubAllTransportsDisabled();
    smtpSettingsApiSpy.apiSmtpSettingsGet.and.returnValue(of({ smtpHost: null }));

    const items = await service.fetchItems();

    const zoneItem = items.find((item) => item.id === 'add-zone');
    expect(zoneItem).toEqual({
      id: 'add-zone',
      label: 'Add your first zone',
      complete: true,
      routerLink: '/zones',
    });
    expect(zonesApiSpy.apiZonesGet).toHaveBeenCalledWith({ skip: 0, take: 1 });
  });

  it('leaves "Add your first zone" incomplete when the zone count is 0', async () => {
    allowAllPermissions();
    zonesApiSpy.apiZonesGet.and.returnValue(of({ totalCount: 0 }));
    usersApiSpy.apiUsersGet.and.returnValue(of({ totalCount: 1 }));
    stubAllTransportsDisabled();
    smtpSettingsApiSpy.apiSmtpSettingsGet.and.returnValue(of({ smtpHost: null }));

    const items = await service.fetchItems();

    expect(items.find((item) => item.id === 'add-zone')?.complete).toBeFalse();
  });

  it('requires more than 1 user (not just the bootstrap admin) for "Create a non-admin user"', async () => {
    allowAllPermissions();
    zonesApiSpy.apiZonesGet.and.returnValue(of({ totalCount: 0 }));
    stubAllTransportsDisabled();
    smtpSettingsApiSpy.apiSmtpSettingsGet.and.returnValue(of({ smtpHost: null }));

    usersApiSpy.apiUsersGet.and.returnValue(of({ totalCount: 1 }));
    let items = await service.fetchItems();
    expect(items.find((item) => item.id === 'create-user')?.complete).toBeFalse();

    usersApiSpy.apiUsersGet.and.returnValue(of({ totalCount: 2 }));
    items = await service.fetchItems();
    expect(items.find((item) => item.id === 'create-user')?.complete).toBeTrue();
  });

  it('marks "Enable a DNS transport beyond Plain DNS" complete when any one of DoT/DoH/DoQ/DNSCrypt is enabled', async () => {
    allowAllPermissions();
    zonesApiSpy.apiZonesGet.and.returnValue(of({ totalCount: 0 }));
    usersApiSpy.apiUsersGet.and.returnValue(of({ totalCount: 1 }));
    smtpSettingsApiSpy.apiSmtpSettingsGet.and.returnValue(of({ smtpHost: null }));

    stubAllTransportsDisabled();
    transportSettingsApiSpy.apiTransportSettingsDnsOverHttpsGet.and.returnValue(
      of({ enable: true }),
    );

    const items = await service.fetchItems();

    const transportItem = items.find((item) => item.id === 'enable-transport');
    expect(transportItem?.complete).toBeTrue();
    expect(transportItem?.routerLink).toBe('/settings/transports');
  });

  it('leaves "Enable a DNS transport beyond Plain DNS" incomplete when every transport is disabled', async () => {
    allowAllPermissions();
    zonesApiSpy.apiZonesGet.and.returnValue(of({ totalCount: 0 }));
    usersApiSpy.apiUsersGet.and.returnValue(of({ totalCount: 1 }));
    smtpSettingsApiSpy.apiSmtpSettingsGet.and.returnValue(of({ smtpHost: null }));
    stubAllTransportsDisabled();

    const items = await service.fetchItems();

    expect(items.find((item) => item.id === 'enable-transport')?.complete).toBeFalse();
  });

  it('marks "Configure SMTP for password-reset e-mails" complete once smtpHost is a non-blank value', async () => {
    allowAllPermissions();
    zonesApiSpy.apiZonesGet.and.returnValue(of({ totalCount: 0 }));
    usersApiSpy.apiUsersGet.and.returnValue(of({ totalCount: 1 }));
    stubAllTransportsDisabled();

    smtpSettingsApiSpy.apiSmtpSettingsGet.and.returnValue(of({ smtpHost: 'smtp.example.com' }));
    let items = await service.fetchItems();
    expect(items.find((item) => item.id === 'configure-smtp')?.complete).toBeTrue();

    smtpSettingsApiSpy.apiSmtpSettingsGet.and.returnValue(of({ smtpHost: '   ' }));
    items = await service.fetchItems();
    expect(items.find((item) => item.id === 'configure-smtp')?.complete).toBeFalse();

    smtpSettingsApiSpy.apiSmtpSettingsGet.and.returnValue(of({ smtpHost: null }));
    items = await service.fetchItems();
    expect(items.find((item) => item.id === 'configure-smtp')?.complete).toBeFalse();
  });

  it('marks a row incomplete, rather than rejecting the whole batch, when its own request fails', async () => {
    allowAllPermissions();
    zonesApiSpy.apiZonesGet.and.returnValue(throwError(() => new Error('403 Forbidden')));
    usersApiSpy.apiUsersGet.and.returnValue(of({ totalCount: 5 }));
    stubAllTransportsDisabled();
    smtpSettingsApiSpy.apiSmtpSettingsGet.and.returnValue(of({ smtpHost: null }));

    const items = await service.fetchItems();

    expect(items.find((item) => item.id === 'add-zone')?.complete).toBeFalse();
    expect(items.find((item) => item.id === 'create-user')?.complete).toBeTrue();
  });
});

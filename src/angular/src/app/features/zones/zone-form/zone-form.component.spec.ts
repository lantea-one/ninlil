import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import {
  CloudProviderCredentialsService,
  DnssecAlgorithmEnum,
  TsigKeysService,
  ZonesService,
  ZoneTypeEnum,
} from '../../../api-client';
import { SIGNING_PROHIBITED_DNSSEC_ALGORITHMS } from '../dnssec-keys/dnssec-key.model';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ZoneFormComponent } from './zone-form.component';
import { AuthService } from '../../../core/auth/auth.service';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the zone create and edit form: field validation, the type-dependent sections,
 * the request save() builds, the TSIG key and cloud credential pickers (including missing
 * permissions and failed loads), catalog membership and on-demand CAA/PTR reconciliation. They use
 * TestBed with spy-backed API services, a stubbed ActivatedRoute for the `zoneId` parameter and a
 * stubbed `AuthService.hasPermission`.
 */
describe('ZoneFormComponent', () => {
  // The permissions the signed-in test user lacks; it holds every other one, so each test sees
  // the page as an administrator would unless it denies something first.
  let deniedPermissions: string[] = [];
  beforeEach(() => (deniedPermissions = []));

  // Hand-shaped spy types; see zone-list.component.spec.ts for why not `jasmine.SpyObj`.
  let zonesApiSpy: {
    apiZonesIdGet: jasmine.Spy;
    apiZonesPost: jasmine.Spy;
    apiZonesIdPut: jasmine.Spy;
    apiZonesGet: jasmine.Spy;
    apiZonesIdCatalogMembersGet: jasmine.Spy;
    apiZonesIdCatalogMembershipPost: jasmine.Spy;
    apiZonesIdCatalogMembershipDelete: jasmine.Spy;
    apiZonesIdReconcileCaaPost: jasmine.Spy;
    apiZonesIdReconcilePtrPost: jasmine.Spy;
  };
  let tsigKeysApiSpy: { apiTsigKeysGet: jasmine.Spy };
  let cloudCredentialsApiSpy: { apiCloudProviderCredentialsGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  /**
   * This helper builds fresh spies, applies `configureApis` and then creates the component.
   * `configureApis` must run before creation because `ngOnInit` calls the APIs at once, and a spy
   * from an earlier call is discarded.
   */
  async function createComponent(zoneId: string | null, configureApis?: () => void) {
    zonesApiSpy = jasmine.createSpyObj('ZonesService', [
      'apiZonesIdGet',
      'apiZonesPost',
      'apiZonesIdPut',
      'apiZonesGet',
      'apiZonesIdCatalogMembersGet',
      'apiZonesIdCatalogMembershipPost',
      'apiZonesIdCatalogMembershipDelete',
      'apiZonesIdReconcileCaaPost',
      'apiZonesIdReconcilePtrPost',
    ]);
    tsigKeysApiSpy = jasmine.createSpyObj('TsigKeysService', ['apiTsigKeysGet']);
    cloudCredentialsApiSpy = jasmine.createSpyObj('CloudProviderCredentialsService', [
      'apiCloudProviderCredentialsGet',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    // Empty picker lists by default; tests that need options set them in configureApis.
    tsigKeysApiSpy.apiTsigKeysGet.and.returnValue(
      of({ items: [], skip: 0, take: 500, totalCount: 0 }),
    );
    cloudCredentialsApiSpy.apiCloudProviderCredentialsGet.and.returnValue(
      of({ items: [], skip: 0, take: 500, totalCount: 0 }),
    );
    // Empty catalog data by default; tests that need catalogs or members set them in
    // configureApis.
    zonesApiSpy.apiZonesGet.and.returnValue(of({ items: [], skip: 0, take: 500, totalCount: 0 }));
    zonesApiSpy.apiZonesIdCatalogMembersGet.and.returnValue(of({ memberZoneNames: [] }));
    zonesApiSpy.apiZonesIdCatalogMembershipPost.and.returnValue(of({}));
    zonesApiSpy.apiZonesIdCatalogMembershipDelete.and.returnValue(of({}));

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [ZoneFormComponent],
      providers: [
        provideRouter([]),
        { provide: ZonesService, useValue: zonesApiSpy },
        { provide: TsigKeysService, useValue: tsigKeysApiSpy },
        { provide: CloudProviderCredentialsService, useValue: cloudCredentialsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap(zoneId ? { zoneId } : {}) },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    spyOn(TestBed.inject(AuthService), 'hasPermission').and.callFake(
      (permission: string) => !deniedPermissions.includes(permission),
    );
    const fixture = TestBed.createComponent(ZoneFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('rejects an nsec3Iterations value above 65535, the real 16-bit unsigned backend bound', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.controls.nsec3Iterations.setValue(65536);
    expect(fixture.componentInstance.form.controls.nsec3Iterations.hasError('max')).toBeTrue();

    fixture.componentInstance.form.controls.nsec3Iterations.setValue(65535);
    expect(fixture.componentInstance.form.controls.nsec3Iterations.hasError('max')).toBeFalse();
  });

  it('accepts a cloudSyncIntervalText value with a days prefix, a real TimeSpan "c" format shape the backend accepts', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.controls.cloudSyncIntervalText.setValue('1.00:05:00');
    expect(
      fixture.componentInstance.form.controls.cloudSyncIntervalText.hasError('pattern'),
    ).toBeFalse();
  });

  it('offers none of the algorithms the server refuses to sign with', async () => {
    const fixture = await createComponent(null);

    for (const algorithm of SIGNING_PROHIBITED_DNSSEC_ALGORITHMS) {
      expect(fixture.componentInstance.dnssecAlgorithms).not.toContain(algorithm);
    }
    expect(fixture.componentInstance.dnssecAlgorithms).toContain(
      DnssecAlgorithmEnum.Ecdsap256Sha256,
    );
  });

  it('create mode: defaults to type Primary, with no owning zone to load', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(fixture.componentInstance.selectedType()).toBe(ZoneTypeEnum.Primary);
    expect(zonesApiSpy.apiZonesIdGet).not.toHaveBeenCalled();
  });

  it('showTransferSection() is true only for a transfer-in zone type', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.showTransferSection()).toBeFalse();
    fixture.componentInstance.form.controls.type.setValue(ZoneTypeEnum.Secondary);
    expect(fixture.componentInstance.showTransferSection()).toBeTrue();
    expect(fixture.componentInstance.showOriginationSections()).toBeFalse();
  });

  it('edit mode: loads the existing zone and populates every field, including array fields as newline text', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({
          id: 'zone-1',
          name: 'example.com',
          type: ZoneTypeEnum.Secondary,
          isDnssecSigned: false,
          isDisabled: false,
          primaryServerAddresses: ['192.0.2.1', '192.0.2.2'],
          forwarderAddresses: [],
          notifyAddresses: [],
          dynamicUpdateEnabled: false,
          cloudSyncInterval: '00:05:00',
          autoManageCaaRecords: null,
          autoManagePtrRecords: null,
        }),
      );
    });

    expect(fixture.componentInstance.isEditMode).toBeTrue();
    expect(fixture.componentInstance.selectedType()).toBe(ZoneTypeEnum.Secondary);
    expect(fixture.componentInstance.form.controls.primaryServerAddressesText.value).toBe(
      '192.0.2.1\n192.0.2.2',
    );
  });

  it('edit mode: shows the default algorithm for a zone stored with one the server no longer signs with', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({
          id: 'zone-1',
          name: 'example.com',
          type: ZoneTypeEnum.Primary,
          isDnssecSigned: true,
          dnssecAlgorithm: DnssecAlgorithmEnum.Rsasha1,
          isDisabled: false,
          primaryServerAddresses: [],
          forwarderAddresses: [],
          notifyAddresses: [],
          dynamicUpdateEnabled: false,
          cloudSyncInterval: '00:05:00',
          autoManageCaaRecords: null,
          autoManagePtrRecords: null,
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.dnssecAlgorithm.value).toBe(
      DnssecAlgorithmEnum.Ecdsap256Sha256,
    );
  });

  it("save() in create mode POSTs the built request and navigates to the new zone's own records", async () => {
    const fixture = await createComponent(null, () => {
      zonesApiSpy.apiZonesPost.and.returnValue(of({ id: 'new-zone', name: 'example.com' }));
    });

    fixture.componentInstance.form.patchValue({ name: 'example.com' });
    await fixture.componentInstance.save();

    expect(zonesApiSpy.apiZonesPost).toHaveBeenCalledWith({
      zoneWriteRequest: jasmine.objectContaining({
        name: 'example.com',
        type: ZoneTypeEnum.Primary,
      }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/zones', 'new-zone', 'records']);
  });

  it('save() splits a multi-line address textarea into a trimmed array, dropping blank lines', async () => {
    const fixture = await createComponent(null, () => {
      zonesApiSpy.apiZonesPost.and.returnValue(of({ id: 'new-zone' }));
    });

    fixture.componentInstance.form.patchValue({
      name: 'example.com',
      type: ZoneTypeEnum.Secondary,
      primaryServerAddressesText: '192.0.2.1\n\n192.0.2.2\n',
    });
    await fixture.componentInstance.save();

    expect(zonesApiSpy.apiZonesPost).toHaveBeenCalledWith({
      zoneWriteRequest: jasmine.objectContaining({
        primaryServerAddresses: ['192.0.2.1', '192.0.2.2'],
      }),
    });
  });

  it('save() in edit mode PUTs the built request and navigates back to the zone list', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'zone-1', name: 'example.com', type: ZoneTypeEnum.Primary }),
      );
      zonesApiSpy.apiZonesIdPut.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.save();

    expect(zonesApiSpy.apiZonesIdPut).toHaveBeenCalledWith({
      id: 'zone-1',
      zoneWriteRequest: jasmine.objectContaining({ name: 'example.com' }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/zones']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      zonesApiSpy.apiZonesPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 409,
              error: 'A zone named example.com already exists.',
            }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'example.com' });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe(
      'A zone named example.com already exists.',
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ name: '' });

    await fixture.componentInstance.save();

    expect(zonesApiSpy.apiZonesPost).not.toHaveBeenCalled();
  });

  it('TSIG key dropdown: loads the live key list and offers it as select options', async () => {
    const fixture = await createComponent(null, () => {
      tsigKeysApiSpy.apiTsigKeysGet.and.returnValue(
        of({
          items: [
            { id: 'key-1', name: 'transfer-key' },
            { id: 'key-2', name: 'update-key' },
          ],
          skip: 0,
          take: 500,
          totalCount: 2,
        }),
      );
    });

    expect(tsigKeysApiSpy.apiTsigKeysGet).toHaveBeenCalledWith({ skip: 0, take: 500 });
    expect(fixture.componentInstance.tsigKeys().map((key) => key.name)).toEqual([
      'transfer-key',
      'update-key',
    ]);
  });

  it('TSIG key dropdown: leaves cluster keys out of the options', async () => {
    const fixture = await createComponent(null, () => {
      tsigKeysApiSpy.apiTsigKeysGet.and.returnValue(
        of({
          items: [
            { id: 'key-1', name: 'transfer-key', isClusterKey: false },
            { id: 'key-2', name: 'cluster-abc', isClusterKey: true },
          ],
          skip: 0,
          take: 500,
          totalCount: 2,
        }),
      );
    });

    expect(fixture.componentInstance.tsigKeys().map((key) => key.name)).toEqual(['transfer-key']);
  });

  it('TSIG key dropdown: selecting a known key writes its name into tsigKeyName and hides the custom field', async () => {
    const fixture = await createComponent(null, () => {
      tsigKeysApiSpy.apiTsigKeysGet.and.returnValue(
        of({
          items: [{ id: 'key-1', name: 'transfer-key' }],
          skip: 0,
          take: 500,
          totalCount: 1,
        }),
      );
    });

    fixture.componentInstance.form.controls.tsigKeySelection.setValue('transfer-key');

    expect(fixture.componentInstance.form.controls.tsigKeyName.value).toBe('transfer-key');
    expect(fixture.componentInstance.showCustomTsigKeyField()).toBeFalse();
  });

  it('TSIG key dropdown: selecting "enter a custom name" reveals the free-text field without clobbering it', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.controls.tsigKeySelection.setValue(
      fixture.componentInstance.customTsigKeyOption,
    );
    fixture.componentInstance.form.controls.tsigKeyName.setValue('not-yet-created-key');

    expect(fixture.componentInstance.showCustomTsigKeyField()).toBeTrue();
    expect(fixture.componentInstance.form.controls.tsigKeyName.value).toBe('not-yet-created-key');
  });

  it('TSIG key dropdown: edit mode selects the matching key from the list when the zone already references one', async () => {
    const fixture = await createComponent('zone-1', () => {
      tsigKeysApiSpy.apiTsigKeysGet.and.returnValue(
        of({
          items: [{ id: 'key-1', name: 'transfer-key' }],
          skip: 0,
          take: 500,
          totalCount: 1,
        }),
      );
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({
          id: 'zone-1',
          name: 'example.com',
          type: ZoneTypeEnum.Primary,
          tsigKeyName: 'transfer-key',
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.tsigKeySelection.value).toBe('transfer-key');
    expect(fixture.componentInstance.showCustomTsigKeyField()).toBeFalse();
  });

  it('still loads the zone without tsigkeys:read or cloudprovidercredentials:read, skipping both lists', async () => {
    deniedPermissions = ['tsigkeys:read', 'cloudprovidercredentials:read'];
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({
          id: 'zone-1',
          name: 'example.com',
          type: ZoneTypeEnum.Primary,
          tsigKeyName: 'transfer-key',
        }),
      );
    });

    expect(tsigKeysApiSpy.apiTsigKeysGet).not.toHaveBeenCalled();
    expect(cloudCredentialsApiSpy.apiCloudProviderCredentialsGet).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toBeNull();
    expect(fixture.componentInstance.form.controls.name.value).toBe('example.com');
    expect(fixture.componentInstance.tsigKeysUnavailable()).toBeTrue();
    expect(fixture.componentInstance.cloudCredentialsUnavailable()).toBeTrue();
    // The existing key is kept through the picker's custom-name entry.
    expect(fixture.componentInstance.form.controls.tsigKeyName.value).toBe('transfer-key');
    expect(fixture.componentInstance.showCustomTsigKeyField()).toBeTrue();
  });

  it('still loads the zone when the TSIG key list request itself fails', async () => {
    const fixture = await createComponent('zone-1', () => {
      tsigKeysApiSpy.apiTsigKeysGet.and.returnValue(throwError(() => new Error('forbidden')));
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'zone-1', name: 'example.com', type: ZoneTypeEnum.Primary }),
      );
    });

    expect(fixture.componentInstance.errorMessage()).toBeNull();
    expect(fixture.componentInstance.tsigKeysUnavailable()).toBeTrue();
    expect(fixture.componentInstance.cloudCredentialsUnavailable()).toBeFalse();
  });

  it('TSIG key dropdown: edit mode falls back to the custom field when the zone references a key not in the visible list', async () => {
    const fixture = await createComponent('zone-1', () => {
      tsigKeysApiSpy.apiTsigKeysGet.and.returnValue(
        of({ items: [], skip: 0, take: 500, totalCount: 0 }),
      );
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({
          id: 'zone-1',
          name: 'example.com',
          type: ZoneTypeEnum.Primary,
          tsigKeyName: 'deleted-key',
        }),
      );
    });

    expect(fixture.componentInstance.showCustomTsigKeyField()).toBeTrue();
    expect(fixture.componentInstance.form.controls.tsigKeyName.value).toBe('deleted-key');
  });

  it('save() submits the selected known TSIG key name', async () => {
    const fixture = await createComponent(null, () => {
      tsigKeysApiSpy.apiTsigKeysGet.and.returnValue(
        of({
          items: [{ id: 'key-1', name: 'transfer-key' }],
          skip: 0,
          take: 500,
          totalCount: 1,
        }),
      );
      zonesApiSpy.apiZonesPost.and.returnValue(of({ id: 'new-zone' }));
    });

    fixture.componentInstance.form.patchValue({ name: 'example.com' });
    fixture.componentInstance.form.controls.tsigKeySelection.setValue('transfer-key');
    await fixture.componentInstance.save();

    expect(zonesApiSpy.apiZonesPost).toHaveBeenCalledWith({
      zoneWriteRequest: jasmine.objectContaining({ tsigKeyName: 'transfer-key' }),
    });
  });

  it('save() submits a custom-entered TSIG key name', async () => {
    const fixture = await createComponent(null, () => {
      zonesApiSpy.apiZonesPost.and.returnValue(of({ id: 'new-zone' }));
    });

    fixture.componentInstance.form.patchValue({ name: 'example.com' });
    fixture.componentInstance.form.controls.tsigKeySelection.setValue(
      fixture.componentInstance.customTsigKeyOption,
    );
    fixture.componentInstance.form.controls.tsigKeyName.setValue('not-yet-created-key');
    await fixture.componentInstance.save();

    expect(zonesApiSpy.apiZonesPost).toHaveBeenCalledWith({
      zoneWriteRequest: jasmine.objectContaining({ tsigKeyName: 'not-yet-created-key' }),
    });
  });

  it('cloud provider credential dropdown: loads the live credential list and offers it as select options', async () => {
    const fixture = await createComponent(null, () => {
      cloudCredentialsApiSpy.apiCloudProviderCredentialsGet.and.returnValue(
        of({
          items: [
            { id: 'cred-1', name: 'Cloudflare prod' },
            { id: 'cred-2', name: 'Route53 staging' },
          ],
          skip: 0,
          take: 500,
          totalCount: 2,
        }),
      );
    });

    expect(cloudCredentialsApiSpy.apiCloudProviderCredentialsGet).toHaveBeenCalledWith({
      skip: 0,
      take: 500,
    });
    expect(
      fixture.componentInstance.cloudCredentials().map((credential) => credential.name),
    ).toEqual(['Cloudflare prod', 'Route53 staging']);
  });

  it('cloud provider credential dropdown: selecting a known credential writes its ID into cloudProviderCredentialId and hides the custom field', async () => {
    const fixture = await createComponent(null, () => {
      cloudCredentialsApiSpy.apiCloudProviderCredentialsGet.and.returnValue(
        of({
          items: [{ id: 'cred-1', name: 'Cloudflare prod' }],
          skip: 0,
          take: 500,
          totalCount: 1,
        }),
      );
    });

    fixture.componentInstance.form.controls.cloudProviderCredentialSelection.setValue('cred-1');

    expect(fixture.componentInstance.form.controls.cloudProviderCredentialId.value).toBe('cred-1');
    expect(fixture.componentInstance.showCustomCloudCredentialField()).toBeFalse();
  });

  it('cloud provider credential dropdown: selecting "enter a custom ID" reveals the free-text field without clobbering it', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.controls.cloudProviderCredentialSelection.setValue(
      fixture.componentInstance.customCloudCredentialOption,
    );
    fixture.componentInstance.form.controls.cloudProviderCredentialId.setValue(
      'not-yet-created-cred',
    );

    expect(fixture.componentInstance.showCustomCloudCredentialField()).toBeTrue();
    expect(fixture.componentInstance.form.controls.cloudProviderCredentialId.value).toBe(
      'not-yet-created-cred',
    );
  });

  it('cloud provider credential dropdown: edit mode selects the matching credential from the list when the zone already references one', async () => {
    const fixture = await createComponent('zone-1', () => {
      cloudCredentialsApiSpy.apiCloudProviderCredentialsGet.and.returnValue(
        of({
          items: [{ id: 'cred-1', name: 'Cloudflare prod' }],
          skip: 0,
          take: 500,
          totalCount: 1,
        }),
      );
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({
          id: 'zone-1',
          name: 'example.com',
          type: ZoneTypeEnum.CloudPrimary,
          cloudProviderCredentialId: 'cred-1',
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.cloudProviderCredentialSelection.value).toBe(
      'cred-1',
    );
    expect(fixture.componentInstance.showCustomCloudCredentialField()).toBeFalse();
  });

  it('cloud provider credential dropdown: edit mode falls back to the custom field when the zone references a credential not in the visible list', async () => {
    const fixture = await createComponent('zone-1', () => {
      cloudCredentialsApiSpy.apiCloudProviderCredentialsGet.and.returnValue(
        of({ items: [], skip: 0, take: 500, totalCount: 0 }),
      );
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({
          id: 'zone-1',
          name: 'example.com',
          type: ZoneTypeEnum.CloudPrimary,
          cloudProviderCredentialId: 'deleted-cred',
        }),
      );
    });

    expect(fixture.componentInstance.showCustomCloudCredentialField()).toBeTrue();
    expect(fixture.componentInstance.form.controls.cloudProviderCredentialId.value).toBe(
      'deleted-cred',
    );
  });

  it('save() submits the selected known cloud provider credential ID', async () => {
    const fixture = await createComponent(null, () => {
      cloudCredentialsApiSpy.apiCloudProviderCredentialsGet.and.returnValue(
        of({
          items: [{ id: 'cred-1', name: 'Cloudflare prod' }],
          skip: 0,
          take: 500,
          totalCount: 1,
        }),
      );
      zonesApiSpy.apiZonesPost.and.returnValue(of({ id: 'new-zone' }));
    });

    fixture.componentInstance.form.patchValue({ name: 'example.com' });
    fixture.componentInstance.form.controls.cloudProviderCredentialSelection.setValue('cred-1');
    await fixture.componentInstance.save();

    expect(zonesApiSpy.apiZonesPost).toHaveBeenCalledWith({
      zoneWriteRequest: jasmine.objectContaining({ cloudProviderCredentialId: 'cred-1' }),
    });
  });

  it('save() submits a custom-entered cloud provider credential ID', async () => {
    const fixture = await createComponent(null, () => {
      zonesApiSpy.apiZonesPost.and.returnValue(of({ id: 'new-zone' }));
    });

    fixture.componentInstance.form.patchValue({ name: 'example.com' });
    fixture.componentInstance.form.controls.cloudProviderCredentialSelection.setValue(
      fixture.componentInstance.customCloudCredentialOption,
    );
    fixture.componentInstance.form.controls.cloudProviderCredentialId.setValue(
      'not-yet-created-cred',
    );
    await fixture.componentInstance.save();

    expect(zonesApiSpy.apiZonesPost).toHaveBeenCalledWith({
      zoneWriteRequest: jasmine.objectContaining({
        cloudProviderCredentialId: 'not-yet-created-cred',
      }),
    });
  });

  it('showCatalogMembershipSection() is false in create mode, even for a locally-originated type', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.showCatalogMembershipSection()).toBeFalse();
  });

  it('showCatalogMembershipSection() is true in edit mode for a Primary zone, and loads the live Catalog zone list', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'zone-1', name: 'example.com', type: ZoneTypeEnum.Primary }),
      );
      zonesApiSpy.apiZonesGet.and.returnValue(
        of({
          items: [
            { id: 'catalog-1', name: 'catalog.invalid', type: ZoneTypeEnum.Catalog },
            { id: 'zone-2', name: 'other.example.com', type: ZoneTypeEnum.Secondary },
          ],
          skip: 0,
          take: 500,
          totalCount: 2,
        }),
      );
    });

    expect(fixture.componentInstance.showCatalogMembershipSection()).toBeTrue();
    expect(fixture.componentInstance.showCatalogMembersListSection()).toBeFalse();
    // Only the Catalog zone is offered, not the Secondary zone in the same response.
    expect(fixture.componentInstance.catalogOptions().map((zone) => zone.id)).toEqual([
      'catalog-1',
    ]);
    expect(fixture.componentInstance.memberOfCatalogZoneId()).toBeNull();
  });

  it('edit mode: an already-joined Primary zone shows its own current catalog membership', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({
          id: 'zone-1',
          name: 'example.com',
          type: ZoneTypeEnum.Primary,
          memberOfCatalogZoneId: 'catalog-1',
        }),
      );
      zonesApiSpy.apiZonesGet.and.returnValue(
        of({
          items: [{ id: 'catalog-1', name: 'catalog.invalid', type: ZoneTypeEnum.Catalog }],
          skip: 0,
          take: 500,
          totalCount: 1,
        }),
      );
    });

    expect(fixture.componentInstance.memberOfCatalogZoneId()).toBe('catalog-1');
    expect(fixture.componentInstance.memberOfCatalogZoneName()).toBe('catalog.invalid');
  });

  it('joinCatalog() POSTs the selected catalog and updates memberOfCatalogZoneId on success', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'zone-1', name: 'example.com', type: ZoneTypeEnum.Primary }),
      );
    });

    fixture.componentInstance.selectedCatalogZoneId.set('catalog-1');
    await fixture.componentInstance.joinCatalog();

    expect(zonesApiSpy.apiZonesIdCatalogMembershipPost).toHaveBeenCalledWith({
      id: 'zone-1',
      zoneCatalogMembershipRequest: { catalogZoneId: 'catalog-1' },
    });
    expect(fixture.componentInstance.memberOfCatalogZoneId()).toBe('catalog-1');
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('joinCatalog() surfaces a failure via catalogActionError rather than throwing', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'zone-1', name: 'example.com', type: ZoneTypeEnum.Primary }),
      );
      zonesApiSpy.apiZonesIdCatalogMembershipPost.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'not a catalog zone' })),
      );
    });

    fixture.componentInstance.selectedCatalogZoneId.set('catalog-1');
    await fixture.componentInstance.joinCatalog();

    expect(fixture.componentInstance.catalogActionError()).toBeTruthy();
    expect(fixture.componentInstance.memberOfCatalogZoneId()).toBeNull();
  });

  it('leaveCatalog() DELETEs the membership and clears memberOfCatalogZoneId on success', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({
          id: 'zone-1',
          name: 'example.com',
          type: ZoneTypeEnum.Primary,
          memberOfCatalogZoneId: 'catalog-1',
        }),
      );
    });

    expect(fixture.componentInstance.memberOfCatalogZoneId()).toBe('catalog-1');

    await fixture.componentInstance.leaveCatalog();

    expect(zonesApiSpy.apiZonesIdCatalogMembershipDelete).toHaveBeenCalledWith({ id: 'zone-1' });
    expect(fixture.componentInstance.memberOfCatalogZoneId()).toBeNull();
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('showCatalogMembersListSection() is true for a Catalog zone in edit mode, and loads its own member list', async () => {
    const fixture = await createComponent('catalog-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'catalog-1', name: 'catalog.invalid', type: ZoneTypeEnum.Catalog }),
      );
      zonesApiSpy.apiZonesIdCatalogMembersGet.and.returnValue(
        of({ memberZoneNames: ['branch-office.example.com', 'example.net'] }),
      );
    });

    expect(fixture.componentInstance.showCatalogMembersListSection()).toBeTrue();
    expect(fixture.componentInstance.showCatalogMembershipSection()).toBeFalse();
    expect(fixture.componentInstance.catalogMembers()).toEqual([
      'branch-office.example.com',
      'example.net',
    ]);
    expect(zonesApiSpy.apiZonesIdCatalogMembersGet).toHaveBeenCalledWith({ id: 'catalog-1' });
  });

  it('showCatalogMembersListSection() is also true for a SecondaryCatalog zone in edit mode', async () => {
    const fixture = await createComponent('catalog-2', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'catalog-2', name: 'catalog.invalid', type: ZoneTypeEnum.SecondaryCatalog }),
      );
    });

    expect(fixture.componentInstance.showCatalogMembersListSection()).toBeTrue();
  });

  it('rejects a ptrIpv4PrefixLength value outside 24-30', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.controls.ptrIpv4PrefixLength.setValue(31);
    expect(fixture.componentInstance.form.controls.ptrIpv4PrefixLength.hasError('max')).toBeTrue();

    fixture.componentInstance.form.controls.ptrIpv4PrefixLength.setValue(23);
    expect(fixture.componentInstance.form.controls.ptrIpv4PrefixLength.hasError('min')).toBeTrue();

    fixture.componentInstance.form.controls.ptrIpv4PrefixLength.setValue(26);
    expect(fixture.componentInstance.form.controls.ptrIpv4PrefixLength.valid).toBeTrue();
  });

  it('rejects a ptrIpv6PrefixLength value outside 16-124', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.controls.ptrIpv6PrefixLength.setValue(8);
    expect(fixture.componentInstance.form.controls.ptrIpv6PrefixLength.hasError('min')).toBeTrue();

    fixture.componentInstance.form.controls.ptrIpv6PrefixLength.setValue(128);
    expect(fixture.componentInstance.form.controls.ptrIpv6PrefixLength.hasError('max')).toBeTrue();
  });

  it('showReconcileNowSection() is false in create mode (no saved zone id to act on)', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.showReconcileNowSection()).toBeFalse();
  });

  it('showReconcileNowSection() is true in edit mode for an origination-eligible zone type', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'zone-1', name: 'example.com', type: ZoneTypeEnum.Primary }),
      );
    });

    expect(fixture.componentInstance.showReconcileNowSection()).toBeTrue();
  });

  it('reconcileCaaNow() posts to the reconcile-caa endpoint and summarizes a real change', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'zone-1', name: 'example.com', type: ZoneTypeEnum.Primary }),
      );
      zonesApiSpy.apiZonesIdReconcileCaaPost.and.returnValue(
        of({ recordsAdded: 1, recordsRemoved: 0, skipped: false, skipReason: null }),
      );
    });

    await fixture.componentInstance.reconcileCaaNow();

    expect(zonesApiSpy.apiZonesIdReconcileCaaPost).toHaveBeenCalledWith({ id: 'zone-1' });
    expect(fixture.componentInstance.reconcileCaaResult()).toBe(
      'Reconciled: 1 record(s) added, 0 removed.',
    );
    expect(fixture.componentInstance.reconcileCaaPending()).toBeFalse();
  });

  it('reconcileCaaNow() surfaces a skip reason without treating it as an error', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'zone-1', name: 'example.com', type: ZoneTypeEnum.Primary }),
      );
      zonesApiSpy.apiZonesIdReconcileCaaPost.and.returnValue(
        of({
          recordsAdded: 0,
          recordsRemoved: 0,
          skipped: true,
          skipReason: 'CAA auto-management is not enabled for this zone.',
        }),
      );
    });

    await fixture.componentInstance.reconcileCaaNow();

    expect(fixture.componentInstance.reconcileCaaResult()).toBe(
      'CAA auto-management is not enabled for this zone.',
    );
  });

  it('reconcilePtrNow() posts to the reconcile-ptr endpoint and summarizes a real change', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'zone-1', name: 'example.com', type: ZoneTypeEnum.Primary }),
      );
      zonesApiSpy.apiZonesIdReconcilePtrPost.and.returnValue(
        of({ recordsAdded: 2, recordsRemoved: 0, reverseZonesTouched: 2, skipped: false }),
      );
    });

    await fixture.componentInstance.reconcilePtrNow();

    expect(zonesApiSpy.apiZonesIdReconcilePtrPost).toHaveBeenCalledWith({ id: 'zone-1' });
    expect(fixture.componentInstance.reconcilePtrResult()).toBe(
      'Reconciled: 2 record(s) added, 0 removed, across 2 reverse zone(s).',
    );
    expect(fixture.componentInstance.reconcilePtrPending()).toBeFalse();
  });

  it('reconcilePtrNow() surfaces an error message when the call fails', async () => {
    const fixture = await createComponent('zone-1', () => {
      zonesApiSpy.apiZonesIdGet.and.returnValue(
        of({ id: 'zone-1', name: 'example.com', type: ZoneTypeEnum.Primary }),
      );
      zonesApiSpy.apiZonesIdReconcilePtrPost.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
      );
    });

    await fixture.componentInstance.reconcilePtrNow();

    expect(fixture.componentInstance.reconcilePtrResult()).toBe('boom');
    expect(fixture.componentInstance.reconcilePtrPending()).toBeFalse();
  });
});

import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import {
  DhcpOptionCatalogService,
  DhcpOptionValueTypeEnum,
  DhcpScopeCustomOptionsService,
  DhcpScopeKindEnum,
  DhcpScopesService,
  InternetProtocolVersionEnum,
  ZonesService,
} from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { DhcpScopeFormComponent } from './dhcp-scope-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the DHCP scope form: create and edit, the DNS zone choices, PXE and zone
 * fields, the IPv6 kind and prefix length rules, and the options table (adding, catalog choice,
 * encoding, server address, errors and the create/update/delete calls). TestBed with spy-backed
 * scope, option, catalog and zone APIs and a stubbed route.
 */
describe('DhcpScopeFormComponent', () => {
  let scopesApiSpy: {
    apiDhcpScopesGet: jasmine.Spy;
    apiDhcpScopesIdGet: jasmine.Spy;
    apiDhcpScopesPost: jasmine.Spy;
    apiDhcpScopesIdPut: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let zonesApiSpy: { apiZonesGet: jasmine.Spy };

  async function createComponent(scopeId: string | null) {
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', [
      'apiDhcpScopesGet',
      'apiDhcpScopesIdGet',
      'apiDhcpScopesPost',
      'apiDhcpScopesIdPut',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    zonesApiSpy = jasmine.createSpyObj('ZonesService', ['apiZonesGet']);
    zonesApiSpy.apiZonesGet.and.returnValue(of({ items: [], skip: 0, take: 500, totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [DhcpScopeFormComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: ZonesService, useValue: zonesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap(scopeId ? { scopeId } : {}) },
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpScopeFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('is in create mode with no scopeId route param', async () => {
    const fixture = await createComponent(null);
    expect(fixture.componentInstance.isEditMode).toBeFalse();
  });

  it('posts a new scope and navigates away on save', async () => {
    const fixture = await createComponent(null);
    scopesApiSpy.apiDhcpScopesPost.and.returnValue(of({ id: 'scope-1' }));

    fixture.componentInstance.form.patchValue({
      name: 'lan',
      rangeStartAddress: '192.168.1.100',
      rangeEndAddress: '192.168.1.200',
    });

    await fixture.componentInstance.save();

    expect(scopesApiSpy.apiDhcpScopesPost).toHaveBeenCalled();
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('loads an existing scope in edit mode and PUTs on save', async () => {
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', [
      'apiDhcpScopesIdGet',
      'apiDhcpScopesIdPut',
    ]);
    scopesApiSpy.apiDhcpScopesIdGet.and.returnValue(
      of({
        id: 'scope-1',
        name: 'lan',
        protocolVersion: 'IPv4',
        rangeStartAddress: '192.168.1.100',
        rangeEndAddress: '192.168.1.200',
        subnetPrefixLength: 24,
        defaultLeaseDuration: '1.00:00:00',
        maxLeaseDuration: '1.00:00:00',
        isDisabled: false,
        gatewayAddresses: ['192.168.1.1'],
        dnsServerAddresses: [],
        comments: null,
      }),
    );
    scopesApiSpy.apiDhcpScopesIdPut.and.returnValue(of(undefined));
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    zonesApiSpy = jasmine.createSpyObj('ZonesService', ['apiZonesGet']);
    zonesApiSpy.apiZonesGet.and.returnValue(of({ items: [], skip: 0, take: 500, totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [DhcpScopeFormComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: ZonesService, useValue: zonesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ scopeId: 'scope-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpScopeFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.isEditMode).toBeTrue();
    expect(fixture.componentInstance.form.controls.name.value).toBe('lan');

    await fixture.componentInstance.save();

    expect(scopesApiSpy.apiDhcpScopesIdPut).toHaveBeenCalledWith(
      jasmine.objectContaining({ id: 'scope-1' }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('narrows its own DNS zone dropdown to Primary/CloudPrimary zones only', async () => {
    const fixture = await createComponent(null);
    zonesApiSpy.apiZonesGet.and.returnValue(
      of({
        items: [
          { id: 'zone-primary', name: 'primary.example.com', type: 'Primary' },
          { id: 'zone-cloud-primary', name: 'cloud.example.com', type: 'CloudPrimary' },
          { id: 'zone-secondary', name: 'secondary.example.com', type: 'Secondary' },
        ],
        skip: 0,
        take: 500,
        totalCount: 3,
      }),
    );

    // Run the zone fetch from ngOnInit again with the new return value.
    await fixture.componentInstance['loadEligibleZones']();

    const zoneIds = fixture.componentInstance.eligibleZones().map((zone) => zone.id);
    expect(zoneIds).toEqual(['zone-primary', 'zone-cloud-primary']);
  });

  it('includes bootFileName/nextServerAddress in the write request, or null when left unset', async () => {
    const fixture = await createComponent(null);
    scopesApiSpy.apiDhcpScopesPost.and.returnValue(of({ id: 'scope-1' }));

    fixture.componentInstance.form.patchValue({
      name: 'lan',
      rangeStartAddress: '192.168.1.100',
      rangeEndAddress: '192.168.1.200',
      bootFileName: 'pxelinux.0',
      nextServerAddress: '192.168.1.10',
    });

    await fixture.componentInstance.save();

    expect(scopesApiSpy.apiDhcpScopesPost).toHaveBeenCalledWith(
      jasmine.objectContaining({
        dhcpScopeWriteRequest: jasmine.objectContaining({
          bootFileName: 'pxelinux.0',
          nextServerAddress: '192.168.1.10',
        }),
      }),
    );
  });

  it('includes dnsZoneId in the write request, or null when left unset', async () => {
    const fixture = await createComponent(null);
    scopesApiSpy.apiDhcpScopesPost.and.returnValue(of({ id: 'scope-1' }));

    fixture.componentInstance.form.patchValue({
      name: 'lan',
      rangeStartAddress: '192.168.1.100',
      rangeEndAddress: '192.168.1.200',
      dnsZoneId: 'zone-1',
    });

    await fixture.componentInstance.save();

    expect(scopesApiSpy.apiDhcpScopesPost).toHaveBeenCalledWith(
      jasmine.objectContaining({
        dhcpScopeWriteRequest: jasmine.objectContaining({ dnsZoneId: 'zone-1' }),
      }),
    );
  });

  it('defaults to AddressAssignment and hides the Kind field for an IPv4 scope', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.showKindField()).toBeFalse();
    expect(fixture.componentInstance.form.controls.kind.value).toBe('AddressAssignment');
  });

  it('shows the Kind field once IPv6 is selected, and the delegated prefix length field once Kind is PrefixDelegation', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.controls.protocolVersion.setValue(
      InternetProtocolVersionEnum.Ipv6,
    );
    expect(fixture.componentInstance.showKindField()).toBeTrue();
    expect(fixture.componentInstance.showDelegatedPrefixLengthField()).toBeFalse();

    fixture.componentInstance.form.controls.kind.setValue(DhcpScopeKindEnum.PrefixDelegation);
    expect(fixture.componentInstance.showDelegatedPrefixLengthField()).toBeTrue();
    expect(
      fixture.componentInstance.form.controls.delegatedPrefixLength.hasError('required'),
    ).toBeTrue();
  });

  it('resets Kind back to AddressAssignment, and clears the delegated prefix length, once protocol switches away from IPv6', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.patchValue({
      protocolVersion: InternetProtocolVersionEnum.Ipv6,
      kind: DhcpScopeKindEnum.PrefixDelegation,
      delegatedPrefixLength: 64,
    });
    expect(fixture.componentInstance.form.controls.delegatedPrefixLength.value).toBe(64);

    fixture.componentInstance.form.controls.protocolVersion.setValue(
      InternetProtocolVersionEnum.Ipv4,
    );

    expect(fixture.componentInstance.form.controls.kind.value).toBe('AddressAssignment');
    expect(fixture.componentInstance.form.controls.delegatedPrefixLength.value).toBeNull();
    expect(fixture.componentInstance.showKindField()).toBeFalse();
  });

  it('includes kind/delegatedPrefixLength in the write request for a Prefix Delegation scope', async () => {
    const fixture = await createComponent(null);
    scopesApiSpy.apiDhcpScopesPost.and.returnValue(of({ id: 'scope-1' }));

    fixture.componentInstance.form.patchValue({
      name: 'prefixes',
      protocolVersion: InternetProtocolVersionEnum.Ipv6,
      kind: DhcpScopeKindEnum.PrefixDelegation,
      delegatedPrefixLength: 60,
      rangeStartAddress: '2001:db8::',
      rangeEndAddress: '2001:db8:ffff::',
    });

    await fixture.componentInstance.save();

    expect(scopesApiSpy.apiDhcpScopesPost).toHaveBeenCalledWith(
      jasmine.objectContaining({
        dhcpScopeWriteRequest: jasmine.objectContaining({
          kind: 'PrefixDelegation',
          delegatedPrefixLength: 60,
        }),
      }),
    );
  });

  it('loads an existing Prefix Delegation scope with its own kind/delegatedPrefixLength intact', async () => {
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', ['apiDhcpScopesIdGet']);
    scopesApiSpy.apiDhcpScopesIdGet.and.returnValue(
      of({
        id: 'scope-1',
        name: 'prefixes',
        protocolVersion: 'IPv6',
        rangeStartAddress: '2001:db8::',
        rangeEndAddress: '2001:db8:ffff::',
        subnetPrefixLength: 48,
        defaultLeaseDuration: '1.00:00:00',
        maxLeaseDuration: '1.00:00:00',
        isDisabled: false,
        gatewayAddresses: [],
        dnsServerAddresses: [],
        comments: null,
        kind: 'PrefixDelegation',
        delegatedPrefixLength: 60,
      }),
    );
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    zonesApiSpy = jasmine.createSpyObj('ZonesService', ['apiZonesGet']);
    zonesApiSpy.apiZonesGet.and.returnValue(of({ items: [], skip: 0, take: 500, totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [DhcpScopeFormComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: ZonesService, useValue: zonesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ scopeId: 'scope-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpScopeFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.showKindField()).toBeTrue();
    expect(fixture.componentInstance.showDelegatedPrefixLengthField()).toBeTrue();
    expect(fixture.componentInstance.form.controls.kind.value).toBe('PrefixDelegation');
    expect(fixture.componentInstance.form.controls.delegatedPrefixLength.value).toBe(60);
  });

  describe('custom options table', () => {
    let optionCatalogApiSpy: { apiDhcpOptionCatalogGet: jasmine.Spy };
    let customOptionsApiSpy: {
      apiDhcpScopesScopeIdCustomOptionsGet: jasmine.Spy;
      apiDhcpScopesScopeIdCustomOptionsPost: jasmine.Spy;
      apiDhcpScopesScopeIdCustomOptionsIdPut: jasmine.Spy;
      apiDhcpScopesScopeIdCustomOptionsIdDelete: jasmine.Spy;
    };

    const dnsServerCatalogEntry = {
      code: 6,
      name: 'Domain Name Server',
      description: 'DNS servers available to the client.',
      valueType: DhcpOptionValueTypeEnum.Ipv4AddressList,
      isServerAddressEligible: true,
    };

    async function createComponentWithOptionsApis(scopeId: string | null) {
      scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', [
        'apiDhcpScopesGet',
        'apiDhcpScopesIdGet',
        'apiDhcpScopesPost',
        'apiDhcpScopesIdPut',
      ]);
      notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
      zonesApiSpy = jasmine.createSpyObj('ZonesService', ['apiZonesGet']);
      zonesApiSpy.apiZonesGet.and.returnValue(of({ items: [], skip: 0, take: 500, totalCount: 0 }));

      optionCatalogApiSpy = jasmine.createSpyObj('DhcpOptionCatalogService', [
        'apiDhcpOptionCatalogGet',
      ]);
      optionCatalogApiSpy.apiDhcpOptionCatalogGet.and.returnValue(of([dnsServerCatalogEntry]));

      customOptionsApiSpy = jasmine.createSpyObj('DhcpScopeCustomOptionsService', [
        'apiDhcpScopesScopeIdCustomOptionsGet',
        'apiDhcpScopesScopeIdCustomOptionsPost',
        'apiDhcpScopesScopeIdCustomOptionsIdPut',
        'apiDhcpScopesScopeIdCustomOptionsIdDelete',
      ]);
      customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsGet.and.returnValue(
        of({ items: [], skip: 0, take: 500, totalCount: 0 }),
      );
      customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsPost.and.returnValue(
        of({ id: 'option-1' }),
      );
      customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsIdPut.and.returnValue(of(undefined));
      customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsIdDelete.and.returnValue(of(undefined));

      await TestBed.configureTestingModule({
        imports: [DhcpScopeFormComponent],
        providers: [
          provideRouter([]),
          { provide: DhcpScopesService, useValue: scopesApiSpy },
          { provide: ZonesService, useValue: zonesApiSpy },
          { provide: DhcpOptionCatalogService, useValue: optionCatalogApiSpy },
          { provide: DhcpScopeCustomOptionsService, useValue: customOptionsApiSpy },
          { provide: NotificationService, useValue: notificationSpy },
          {
            provide: ActivatedRoute,
            useValue: {
              snapshot: { paramMap: convertToParamMap(scopeId ? { scopeId } : {}) },
            },
          },
        ],
      }).compileComponents();

      const fixture = TestBed.createComponent(DhcpScopeFormComponent);
      fixture.detectChanges();
      await flushMicrotasks();
      fixture.detectChanges();
      return fixture;
    }

    it('starts with an empty options table', async () => {
      const fixture = await createComponentWithOptionsApis(null);
      expect(fixture.componentInstance.options.length).toBe(0);
    });

    it('adds a row defaulting to the custom/raw tag, and removes it again', async () => {
      const fixture = await createComponentWithOptionsApis(null);

      fixture.componentInstance.addOptionRow();
      expect(fixture.componentInstance.options.length).toBe(1);
      expect(fixture.componentInstance.options.at(0).controls.catalogSelection.value).toBe(
        'custom',
      );

      fixture.componentInstance.removeOptionRow(0);
      expect(fixture.componentInstance.options.length).toBe(0);
    });

    it('picking a catalog entry syncs optionTag and resolves the value type/server-address eligibility', async () => {
      const fixture = await createComponentWithOptionsApis(null);

      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.catalogSelection.setValue('6');
      fixture.componentInstance.onOptionCatalogSelectionChange(0);

      expect(fixture.componentInstance.options.at(0).controls.optionTag.value).toBe(6);
      expect(fixture.componentInstance.optionRowViewState()[0]).toEqual({
        valueType: DhcpOptionValueTypeEnum.Ipv4AddressList,
        isServerAddressEligible: true,
      });
    });

    it('encodes a typed value and posts it as a new custom option on save', async () => {
      const fixture = await createComponentWithOptionsApis(null);
      scopesApiSpy.apiDhcpScopesPost.and.returnValue(of({ id: 'scope-1' }));

      fixture.componentInstance.form.patchValue({
        name: 'lan',
        rangeStartAddress: '192.168.1.100',
        rangeEndAddress: '192.168.1.200',
      });
      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.catalogSelection.setValue('6');
      fixture.componentInstance.onOptionCatalogSelectionChange(0);
      fixture.componentInstance.options.at(0).controls.value.setValue('192.168.1.1');

      await fixture.componentInstance.save();

      expect(customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsPost).toHaveBeenCalledWith(
        jasmine.objectContaining({
          scopeId: 'scope-1',
          dhcpScopeCustomOptionWriteRequest: jasmine.objectContaining({
            optionTag: 6,
            valueHex: 'C0A80101',
            valueSource: 'Custom',
          }),
        }),
      );
    });

    it('posts ServerAddress as the value source with a blank value, ignoring whatever is typed', async () => {
      const fixture = await createComponentWithOptionsApis(null);
      scopesApiSpy.apiDhcpScopesPost.and.returnValue(of({ id: 'scope-1' }));

      fixture.componentInstance.form.patchValue({
        name: 'lan',
        rangeStartAddress: '192.168.1.100',
        rangeEndAddress: '192.168.1.200',
      });
      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.catalogSelection.setValue('6');
      fixture.componentInstance.onOptionCatalogSelectionChange(0);
      fixture.componentInstance.options.at(0).controls.valueSource.setValue('ServerAddress');

      await fixture.componentInstance.save();

      expect(customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsPost).toHaveBeenCalledWith(
        jasmine.objectContaining({
          dhcpScopeCustomOptionWriteRequest: jasmine.objectContaining({
            optionTag: 6,
            valueHex: '',
            valueSource: 'ServerAddress',
          }),
        }),
      );
    });

    it('blocks save and surfaces a row-identifying message when a value fails to encode', async () => {
      const fixture = await createComponentWithOptionsApis(null);

      fixture.componentInstance.form.patchValue({
        name: 'lan',
        rangeStartAddress: '192.168.1.100',
        rangeEndAddress: '192.168.1.200',
      });
      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.catalogSelection.setValue('6');
      fixture.componentInstance.onOptionCatalogSelectionChange(0);
      fixture.componentInstance.options.at(0).controls.value.setValue('not-an-address');

      await fixture.componentInstance.save();

      expect(scopesApiSpy.apiDhcpScopesPost).not.toHaveBeenCalled();
      expect(fixture.componentInstance.errorMessage()).toContain('Option row 1');
    });

    it('reconciles an edited existing option as a PUT and a removed one as a DELETE', async () => {
      customOptionsApiSpy = jasmine.createSpyObj('DhcpScopeCustomOptionsService', [
        'apiDhcpScopesScopeIdCustomOptionsGet',
        'apiDhcpScopesScopeIdCustomOptionsPost',
        'apiDhcpScopesScopeIdCustomOptionsIdPut',
        'apiDhcpScopesScopeIdCustomOptionsIdDelete',
      ]);
      customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsGet.and.returnValue(
        of({
          items: [
            {
              id: 'kept',
              scopeId: 'scope-1',
              optionTag: 66,
              valueHex: '414243',
              isDisabled: false,
            },
            {
              id: 'removed',
              scopeId: 'scope-1',
              optionTag: 67,
              valueHex: '5A5A',
              isDisabled: false,
            },
          ],
          skip: 0,
          take: 500,
          totalCount: 2,
        }),
      );
      customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsIdPut.and.returnValue(of(undefined));
      customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsIdDelete.and.returnValue(of(undefined));
      customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsPost.and.returnValue(
        of({ id: 'unused' }),
      );

      scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', [
        'apiDhcpScopesIdGet',
        'apiDhcpScopesIdPut',
      ]);
      scopesApiSpy.apiDhcpScopesIdGet.and.returnValue(
        of({
          id: 'scope-1',
          name: 'lan',
          protocolVersion: 'IPv4',
          rangeStartAddress: '192.168.1.100',
          rangeEndAddress: '192.168.1.200',
          subnetPrefixLength: 24,
          defaultLeaseDuration: '1.00:00:00',
          maxLeaseDuration: '1.00:00:00',
          isDisabled: false,
          gatewayAddresses: [],
          dnsServerAddresses: [],
          comments: null,
        }),
      );
      scopesApiSpy.apiDhcpScopesIdPut.and.returnValue(of(undefined));
      notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
      zonesApiSpy = jasmine.createSpyObj('ZonesService', ['apiZonesGet']);
      zonesApiSpy.apiZonesGet.and.returnValue(of({ items: [], skip: 0, take: 500, totalCount: 0 }));
      optionCatalogApiSpy = jasmine.createSpyObj('DhcpOptionCatalogService', [
        'apiDhcpOptionCatalogGet',
      ]);
      optionCatalogApiSpy.apiDhcpOptionCatalogGet.and.returnValue(of([]));

      await TestBed.configureTestingModule({
        imports: [DhcpScopeFormComponent],
        providers: [
          provideRouter([]),
          { provide: DhcpScopesService, useValue: scopesApiSpy },
          { provide: ZonesService, useValue: zonesApiSpy },
          { provide: DhcpOptionCatalogService, useValue: optionCatalogApiSpy },
          { provide: DhcpScopeCustomOptionsService, useValue: customOptionsApiSpy },
          { provide: NotificationService, useValue: notificationSpy },
          {
            provide: ActivatedRoute,
            useValue: { snapshot: { paramMap: convertToParamMap({ scopeId: 'scope-1' }) } },
          },
        ],
      }).compileComponents();

      const fixture = TestBed.createComponent(DhcpScopeFormComponent);
      fixture.detectChanges();
      await flushMicrotasks();
      fixture.detectChanges();

      expect(fixture.componentInstance.options.length).toBe(2);

      // Remove the second loaded row ("removed"); its id must become a DELETE.
      fixture.componentInstance.removeOptionRow(1);

      await fixture.componentInstance.save();

      expect(customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsIdPut).toHaveBeenCalledWith(
        jasmine.objectContaining({ scopeId: 'scope-1', id: 'kept' }),
      );
      expect(customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsIdDelete).toHaveBeenCalledWith(
        jasmine.objectContaining({ scopeId: 'scope-1', id: 'removed' }),
      );
      expect(customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsPost).not.toHaveBeenCalled();
    });
  });
});

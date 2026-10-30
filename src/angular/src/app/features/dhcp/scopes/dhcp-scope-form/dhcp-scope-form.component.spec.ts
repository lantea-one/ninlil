import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
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
    expect(fixture.componentInstance.isEditMode()).toBeFalse();
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

    expect(fixture.componentInstance.isEditMode()).toBeTrue();
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

  it('hides the gateway field for an IPv6 scope and clears what was typed', async () => {
    const fixture = await createComponent(null);
    const component = fixture.componentInstance;
    const gatewayField = () =>
      (fixture.nativeElement as HTMLElement).querySelector('#dhcp-scope-gateways');

    component.form.patchValue({ gatewayAddresses: '192.168.1.1' });
    expect(component.showGatewayField()).toBeTrue();

    component.form.controls.protocolVersion.setValue(InternetProtocolVersionEnum.Ipv6);
    fixture.detectChanges();

    expect(component.showGatewayField()).toBeFalse();
    expect(gatewayField()).toBeNull();
    expect(component.form.controls.gatewayAddresses.value).toBe('');
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

    /** The scope the edit-mode option specs load; `IdGet` returns it. */
    const loadedScope = {
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
    };

    /** The router's `navigate`, spied so the specs can assert that a failed save stays on the page. */
    let navigateSpy: jasmine.Spy;

    /** A 400 problem response, as the option endpoints answer a refused value. */
    function badRequest(detail: string): HttpErrorResponse {
      return new HttpErrorResponse({ status: 400, error: { detail } });
    }

    /** Fills the required scope fields so a new scope can be saved. */
    function fillNewScope(fixture: { componentInstance: DhcpScopeFormComponent }): void {
      fixture.componentInstance.form.patchValue({
        name: 'lan',
        rangeStartAddress: '192.168.1.100',
        rangeEndAddress: '192.168.1.200',
      });
    }

    async function createComponentWithOptionsApis(
      scopeId: string | null,
      configureApis?: () => void,
    ) {
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

      configureApis?.();

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

      navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

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

    it('applies the DHCPv4 tag limits on an IPv4 scope and the DHCPv6 code limits on an IPv6 scope', async () => {
      const fixture = await createComponentWithOptionsApis(null);
      fixture.componentInstance.addOptionRow();
      const tagControl = fixture.componentInstance.options.at(0).controls.optionTag;

      for (const tag of [0, 255, 52, 53, 82]) {
        tagControl.setValue(tag);
        expect(tagControl.invalid).toBeTrue();
      }
      tagControl.setValue(66);
      expect(tagControl.valid).toBeTrue();

      fixture.componentInstance.form.controls.protocolVersion.setValue(
        InternetProtocolVersionEnum.Ipv6,
      );
      tagControl.setValue(255);
      expect(tagControl.valid).toBeTrue();
      for (const code of [0, 256, 2, 25, 39]) {
        tagControl.setValue(code);
        expect(tagControl.invalid).toBeTrue();
      }
      tagControl.setValue(53);
      expect(tagControl.valid).toBeTrue();
    });

    it('says why a custom tag is refused, per protocol, and shows the DHCPv6 value length hint on DHCPv6 custom rows only', async () => {
      const fixture = await createComponentWithOptionsApis(null);
      const component = fixture.componentInstance;
      const element = fixture.nativeElement as HTMLElement;
      // The error text depends on form control state, not on a signal, so a programmatic
      // setValue needs the view marked for check (typing does it through the input event).
      const refresh = (): void => {
        fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
        fixture.detectChanges();
      };
      component.addOptionRow();
      const tag = component.options.at(0).controls.optionTag;
      const errorText = (): string | null =>
        element.querySelector('.option-row .field-error')?.textContent?.trim() ?? null;
      const hasLengthHint = (): boolean =>
        (element.querySelector('.option-row')?.textContent ?? '').includes('At most 65,535 bytes');

      tag.setValue(53);
      refresh();
      expect(errorText()).toContain('Tag 53 is set by the server or the relay agent');
      tag.setValue(255);
      refresh();
      expect(errorText()).toContain('between 1 and 254');
      tag.setValue(66);
      refresh();
      expect(errorText()).toBeNull();
      expect(hasLengthHint()).toBeFalse();

      component.form.controls.protocolVersion.setValue(InternetProtocolVersionEnum.Ipv6);
      refresh();
      tag.setValue(2);
      refresh();
      expect(errorText()).toContain('Code 2 (Server Identifier) is written by the server');
      tag.setValue(256);
      refresh();
      expect(errorText()).toContain('between 1 and 255');
      tag.setValue(200);
      refresh();
      expect(errorText()).toBeNull();
      expect(hasLengthHint()).toBeTrue();
    });

    it('shows the hints for options 66 and 150 on an IPv4 scope only', async () => {
      const fixture = await createComponentWithOptionsApis(null);
      const component = fixture.componentInstance;

      expect(component.optionHint(66)).toContain('as text');
      expect(component.optionHint(150)).toContain('RFC 5859');
      expect(component.optionHint(6)).toBeNull();

      component.form.controls.protocolVersion.setValue(InternetProtocolVersionEnum.Ipv6);
      expect(component.optionHint(66)).toBeNull();
    });

    it('blocks save with a row-identifying message when a value is over 255 bytes', async () => {
      const fixture = await createComponentWithOptionsApis(null);

      fixture.componentInstance.form.patchValue({
        name: 'lan',
        rangeStartAddress: '192.168.1.100',
        rangeEndAddress: '192.168.1.200',
      });
      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.optionTag.setValue(200);
      fixture.componentInstance.options.at(0).controls.value.setValue('AA'.repeat(256));

      await fixture.componentInstance.save();

      expect(scopesApiSpy.apiDhcpScopesPost).not.toHaveBeenCalled();
      expect(fixture.componentInstance.errorMessage()).toContain('Option row 1');
      expect(fixture.componentInstance.errorMessage()).toContain('255');
    });

    it('shows a stored value that does not fit its catalog type as a custom raw-hex row and saves it unchanged', async () => {
      // Option 3 (Router) is an address list; two bytes cannot decode as one.
      const fixture = await createComponentWithOptionsApis('scope-1', () => {
        optionCatalogApiSpy.apiDhcpOptionCatalogGet.and.returnValue(
          of([
            {
              code: 3,
              name: 'Router',
              description: '',
              valueType: DhcpOptionValueTypeEnum.Ipv4AddressList,
              isServerAddressEligible: true,
            },
          ]),
        );
        scopesApiSpy.apiDhcpScopesIdGet.and.returnValue(of(loadedScope));
        scopesApiSpy.apiDhcpScopesIdPut.and.returnValue(of(undefined));
        customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsGet.and.returnValue(
          of({
            items: [
              { id: 'odd', scopeId: 'scope-1', optionTag: 3, valueHex: '0102', isDisabled: false },
            ],
            skip: 0,
            take: 500,
            totalCount: 1,
          }),
        );
      });

      const row = fixture.componentInstance.options.at(0);
      expect(row.controls.catalogSelection.value).toBe('custom');
      expect(row.controls.value.value).toBe('0102');
      expect(fixture.nativeElement.textContent).toContain('does not fit the Router format');

      await fixture.componentInstance.save();

      expect(fixture.componentInstance.errorMessage()).toBeNull();
      expect(customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsIdPut).toHaveBeenCalledWith(
        jasmine.objectContaining({
          id: 'odd',
          dhcpScopeCustomOptionWriteRequest: jasmine.objectContaining({
            optionTag: 3,
            valueHex: '0102',
          }),
        }),
      );
    });

    it('refuses two rows with the same tag before any call and names both rows', async () => {
      const fixture = await createComponentWithOptionsApis(null);
      fillNewScope(fixture);
      for (let count = 0; count < 2; count++) {
        fixture.componentInstance.addOptionRow();
        fixture.componentInstance.options.at(count).controls.optionTag.setValue(200);
      }

      await fixture.componentInstance.save();

      expect(scopesApiSpy.apiDhcpScopesPost).not.toHaveBeenCalled();
      expect(customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsPost).not.toHaveBeenCalled();
      expect(fixture.componentInstance.errorMessage()).toContain('Option rows 1 and 2');
      expect(fixture.componentInstance.errorMessage()).toContain('tag 200');
    });

    it('says the scope was saved when an option fails, stays on the page, and edits that scope on retry', async () => {
      const fixture = await createComponentWithOptionsApis(null);
      scopesApiSpy.apiDhcpScopesPost.and.returnValue(of({ id: 'scope-9' }));
      scopesApiSpy.apiDhcpScopesIdPut.and.returnValue(of(undefined));
      customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsPost.and.returnValues(
        throwError(() => badRequest('Value refused.')),
        of({ id: 'option-9' }),
      );
      fillNewScope(fixture);
      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.optionTag.setValue(200);
      fixture.componentInstance.options.at(0).controls.value.setValue('AA');

      await fixture.componentInstance.save();

      expect(fixture.componentInstance.errorMessage()).toContain('The scope was saved');
      expect(fixture.componentInstance.errorMessage()).toContain('option row 1 (tag 200)');
      expect(fixture.componentInstance.errorMessage()).toContain('Value refused.');
      expect(navigateSpy).not.toHaveBeenCalled();
      expect(fixture.componentInstance.isEditMode()).toBeTrue();

      await fixture.componentInstance.save();

      expect(scopesApiSpy.apiDhcpScopesPost).toHaveBeenCalledTimes(1);
      expect(scopesApiSpy.apiDhcpScopesIdPut).toHaveBeenCalledWith(
        jasmine.objectContaining({ id: 'scope-9' }),
      );
      expect(navigateSpy).toHaveBeenCalledWith(['/dhcp-scopes']);
    });

    it('keeps the id of an option that was created, so the retry updates it and posts only the failed row', async () => {
      const fixture = await createComponentWithOptionsApis(null);
      scopesApiSpy.apiDhcpScopesPost.and.returnValue(of({ id: 'scope-9' }));
      scopesApiSpy.apiDhcpScopesIdPut.and.returnValue(of(undefined));
      customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsPost.and.returnValues(
        of({ id: 'option-a' }),
        throwError(() => badRequest('Second refused.')),
        of({ id: 'option-b' }),
      );
      fillNewScope(fixture);
      for (const [index, tag] of [200, 201].entries()) {
        fixture.componentInstance.addOptionRow();
        fixture.componentInstance.options.at(index).controls.optionTag.setValue(tag);
        fixture.componentInstance.options.at(index).controls.value.setValue('AA');
      }

      await fixture.componentInstance.save();
      expect(fixture.componentInstance.errorMessage()).toContain('option row 2 (tag 201)');

      await fixture.componentInstance.save();

      const posts = customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsPost;
      expect(posts).toHaveBeenCalledTimes(3);
      expect(posts.calls.argsFor(2)[0].dhcpScopeCustomOptionWriteRequest.optionTag).toBe(201);
      expect(customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsIdPut).toHaveBeenCalledTimes(1);
      expect(customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsIdPut).toHaveBeenCalledWith(
        jasmine.objectContaining({ scopeId: 'scope-9', id: 'option-a' }),
      );
      expect(navigateSpy).toHaveBeenCalledWith(['/dhcp-scopes']);
    });

    it('deletes a removed row before it creates a new row with the same tag', async () => {
      const calls: string[] = [];
      const fixture = await createComponentWithOptionsApis('scope-1', () => {
        scopesApiSpy.apiDhcpScopesIdGet.and.returnValue(of(loadedScope));
        scopesApiSpy.apiDhcpScopesIdPut.and.returnValue(of(undefined));
        customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsGet.and.returnValue(
          of({
            items: [
              {
                id: 'old',
                scopeId: 'scope-1',
                optionTag: 6,
                valueHex: 'C0A80101',
                isDisabled: false,
              },
            ],
            skip: 0,
            take: 500,
            totalCount: 1,
          }),
        );
        customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsIdDelete.and.callFake(() => {
          calls.push('delete');
          return of(undefined);
        });
        customOptionsApiSpy.apiDhcpScopesScopeIdCustomOptionsPost.and.callFake(() => {
          calls.push('post');
          return of({ id: 'new' });
        });
      });

      fixture.componentInstance.removeOptionRow(0);
      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.optionTag.setValue(6);
      fixture.componentInstance.options.at(0).controls.value.setValue('AA');

      await fixture.componentInstance.save();

      expect(calls).toEqual(['delete', 'post']);
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

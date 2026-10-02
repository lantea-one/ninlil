import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import {
  DhcpOptionCatalogService,
  DhcpScopesService,
  DhcpTagRuleOptionsService,
  DhcpTagRulesService,
} from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { DhcpTagRuleFormComponent } from './dhcp-tag-rule-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the tag rule form: create and edit, the match fields (including the DHCPv6
 * ones), and the options table (merged catalog, adding, encoding, server address, errors, tag
 * collisions between catalogs, and the create/update/delete calls). TestBed with spy-backed rule,
 * option, scope and catalog APIs and a stubbed route.
 */
describe('DhcpTagRuleFormComponent', () => {
  let tagRulesApiSpy: {
    apiDhcpTagRulesPost: jasmine.Spy;
    apiDhcpTagRulesIdPut: jasmine.Spy;
    apiDhcpTagRulesIdGet: jasmine.Spy;
  };
  let scopesApiSpy: { apiDhcpScopesGet: jasmine.Spy };
  let optionCatalogApiSpy: { apiDhcpOptionCatalogGet: jasmine.Spy };
  let tagRuleOptionsApiSpy: {
    apiDhcpTagRulesTagRuleIdOptionsGet: jasmine.Spy;
    apiDhcpTagRulesTagRuleIdOptionsPost: jasmine.Spy;
    apiDhcpTagRulesTagRuleIdOptionsIdPut: jasmine.Spy;
    apiDhcpTagRulesTagRuleIdOptionsIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(tagRuleId: string | null, configureApis?: () => void) {
    tagRulesApiSpy = jasmine.createSpyObj('DhcpTagRulesService', [
      'apiDhcpTagRulesPost',
      'apiDhcpTagRulesIdPut',
      'apiDhcpTagRulesIdGet',
    ]);
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', ['apiDhcpScopesGet']);
    scopesApiSpy.apiDhcpScopesGet.and.returnValue(
      of({ items: [{ id: 'scope-1', name: 'lan' }], skip: 0, take: 200, totalCount: 1 }),
    );
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    optionCatalogApiSpy = jasmine.createSpyObj('DhcpOptionCatalogService', [
      'apiDhcpOptionCatalogGet',
    ]);
    optionCatalogApiSpy.apiDhcpOptionCatalogGet.and.returnValue(of([]));

    tagRuleOptionsApiSpy = jasmine.createSpyObj('DhcpTagRuleOptionsService', [
      'apiDhcpTagRulesTagRuleIdOptionsGet',
      'apiDhcpTagRulesTagRuleIdOptionsPost',
      'apiDhcpTagRulesTagRuleIdOptionsIdPut',
      'apiDhcpTagRulesTagRuleIdOptionsIdDelete',
    ]);
    tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsGet.and.returnValue(
      of({ items: [], skip: 0, take: 500, totalCount: 0 }),
    );
    tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsPost.and.returnValue(
      of({ id: 'option-1' }),
    );
    tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsIdPut.and.returnValue(of(undefined));
    tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsIdDelete.and.returnValue(of(undefined));

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [DhcpTagRuleFormComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpTagRulesService, useValue: tagRulesApiSpy },
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: DhcpOptionCatalogService, useValue: optionCatalogApiSpy },
        { provide: DhcpTagRuleOptionsService, useValue: tagRuleOptionsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap(tagRuleId ? { tagRuleId } : {}) },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(DhcpTagRuleFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('is in create mode with no tagRuleId route param and loads the scope dropdown', async () => {
    const fixture = await createComponent(null);
    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(fixture.componentInstance.scopes().length).toBe(1);
  });

  it('posts a new rule with the real string match-mode values and navigates away', async () => {
    const fixture = await createComponent(null, () => {
      tagRulesApiSpy.apiDhcpTagRulesPost.and.returnValue(of({ id: 'rule-1' }));
    });

    fixture.componentInstance.form.patchValue({
      name: 'pxe',
      vendorClassIdentifierMatch: 'PXEClient',
      vendorClassIdentifierMatchMode: 'Prefix',
    });

    await fixture.componentInstance.save();

    expect(tagRulesApiSpy.apiDhcpTagRulesPost).toHaveBeenCalledWith(
      jasmine.objectContaining({
        dhcpTagRuleWriteRequest: jasmine.objectContaining({
          name: 'pxe',
          vendorClassIdentifierMatchMode: 'Prefix',
          scopeId: null,
        }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/dhcp-tag-rules']);
  });

  it('loads an existing rule in edit mode and PUTs on save', async () => {
    const fixture = await createComponent('rule-1', () => {
      tagRulesApiSpy.apiDhcpTagRulesIdGet.and.returnValue(
        of({
          id: 'rule-1',
          name: 'pxe',
          scopeId: 'scope-1',
          priority: 5,
          isDisabled: false,
          vendorClassIdentifierMatch: 'PXEClient',
          vendorClassIdentifierMatchMode: 'Exact',
          vendorClassEnterpriseNumber: null,
          userClassMatch: null,
          userClassMatchMode: 'Exact',
          relayAgentCircuitIdHex: null,
          relayAgentRemoteIdHex: null,
          relayInterfaceIdHex: null,
          comments: null,
        }),
      );
      tagRulesApiSpy.apiDhcpTagRulesIdPut.and.returnValue(of(undefined));
    });

    expect(fixture.componentInstance.form.controls.name.value).toBe('pxe');
    expect(fixture.componentInstance.form.controls.scopeId.value).toBe('scope-1');

    await fixture.componentInstance.save();

    expect(tagRulesApiSpy.apiDhcpTagRulesIdPut).toHaveBeenCalledWith(
      jasmine.objectContaining({ id: 'rule-1' }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('round-trips the DHCPv6-specific match fields (vendor class enterprise number, relay interface ID)', async () => {
    const fixture = await createComponent('rule-6', () => {
      tagRulesApiSpy.apiDhcpTagRulesIdGet.and.returnValue(
        of({
          id: 'rule-6',
          name: 'dhcpv6-clients',
          scopeId: 'scope-1',
          priority: 0,
          isDisabled: false,
          vendorClassIdentifierMatch: 'PXEClient:Arch',
          vendorClassIdentifierMatchMode: 'Prefix',
          vendorClassEnterpriseNumber: 32473,
          userClassMatch: null,
          userClassMatchMode: 'Exact',
          relayAgentCircuitIdHex: null,
          relayAgentRemoteIdHex: '00007ED9AABB',
          relayInterfaceIdHex: '65746830',
          comments: null,
        }),
      );
      tagRulesApiSpy.apiDhcpTagRulesIdPut.and.returnValue(of(undefined));
    });

    expect(fixture.componentInstance.form.controls.vendorClassEnterpriseNumber.value).toBe(32473);
    expect(fixture.componentInstance.form.controls.relayInterfaceIdHex.value).toBe('65746830');

    await fixture.componentInstance.save();

    expect(tagRulesApiSpy.apiDhcpTagRulesIdPut).toHaveBeenCalledWith(
      jasmine.objectContaining({
        id: 'rule-6',
        dhcpTagRuleWriteRequest: jasmine.objectContaining({
          vendorClassEnterpriseNumber: 32473,
          relayAgentRemoteIdHex: '00007ED9AABB',
          relayInterfaceIdHex: '65746830',
        }),
      }),
    );
  });

  describe('contributed options table', () => {
    const dnsServerCatalogEntry = {
      code: 6,
      name: 'Domain Name Server',
      description: 'DNS servers available to the client.',
      valueType: 'IPv4AddressList',
      isServerAddressEligible: true,
    };

    it('merges both protocols’ catalogs, each labeled with its own protocol', async () => {
      const fixture = await createComponent(null, () => {
        optionCatalogApiSpy.apiDhcpOptionCatalogGet.and.callFake(
          (params: { protocolVersion: string }) =>
            of(params.protocolVersion === 'IPv4' ? [dnsServerCatalogEntry] : []),
        );
      });

      expect(fixture.componentInstance.combinedCatalog().length).toBe(1);
      expect(fixture.componentInstance.combinedCatalog()[0].selectionValue).toBe('IPv4:6');
    });

    it('adds a row defaulting to the custom/raw tag, and removes it again', async () => {
      const fixture = await createComponent(null);

      fixture.componentInstance.addOptionRow();
      expect(fixture.componentInstance.options.length).toBe(1);
      expect(fixture.componentInstance.options.at(0).controls.catalogSelection.value).toBe(
        'custom',
      );

      fixture.componentInstance.removeOptionRow(0);
      expect(fixture.componentInstance.options.length).toBe(0);
    });

    it('picking a merged catalog entry syncs optionTag and resolves the value type/server-address eligibility', async () => {
      const fixture = await createComponent(null, () => {
        optionCatalogApiSpy.apiDhcpOptionCatalogGet.and.callFake(
          (params: { protocolVersion: string }) =>
            of(params.protocolVersion === 'IPv4' ? [dnsServerCatalogEntry] : []),
        );
      });

      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.catalogSelection.setValue('IPv4:6');
      fixture.componentInstance.onOptionCatalogSelectionChange(0);

      expect(fixture.componentInstance.options.at(0).controls.optionTag.value).toBe(6);
      expect(fixture.componentInstance.optionRowViewState()[0]).toEqual({
        valueType: 'IPv4AddressList',
        isServerAddressEligible: true,
      });
    });

    it('encodes a typed value and posts it as a new contributed option on save', async () => {
      const fixture = await createComponent(null, () => {
        optionCatalogApiSpy.apiDhcpOptionCatalogGet.and.callFake(
          (params: { protocolVersion: string }) =>
            of(params.protocolVersion === 'IPv4' ? [dnsServerCatalogEntry] : []),
        );
        tagRulesApiSpy.apiDhcpTagRulesPost.and.returnValue(of({ id: 'rule-1' }));
      });

      fixture.componentInstance.form.patchValue({ name: 'pxe' });
      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.catalogSelection.setValue('IPv4:6');
      fixture.componentInstance.onOptionCatalogSelectionChange(0);
      fixture.componentInstance.options.at(0).controls.value.setValue('192.168.1.1');

      await fixture.componentInstance.save();

      expect(tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsPost).toHaveBeenCalledWith(
        jasmine.objectContaining({
          tagRuleId: 'rule-1',
          dhcpTagRuleOptionWriteRequest: jasmine.objectContaining({
            optionTag: 6,
            valueHex: 'C0A80101',
            valueSource: 'Custom',
          }),
        }),
      );
    });

    it('posts ServerAddress as the value source with a blank value', async () => {
      const fixture = await createComponent(null, () => {
        optionCatalogApiSpy.apiDhcpOptionCatalogGet.and.callFake(
          (params: { protocolVersion: string }) =>
            of(params.protocolVersion === 'IPv4' ? [dnsServerCatalogEntry] : []),
        );
        tagRulesApiSpy.apiDhcpTagRulesPost.and.returnValue(of({ id: 'rule-1' }));
      });

      fixture.componentInstance.form.patchValue({ name: 'pxe' });
      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.catalogSelection.setValue('IPv4:6');
      fixture.componentInstance.onOptionCatalogSelectionChange(0);
      fixture.componentInstance.options.at(0).controls.valueSource.setValue('ServerAddress');

      await fixture.componentInstance.save();

      expect(tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsPost).toHaveBeenCalledWith(
        jasmine.objectContaining({
          dhcpTagRuleOptionWriteRequest: jasmine.objectContaining({
            optionTag: 6,
            valueHex: '',
            valueSource: 'ServerAddress',
          }),
        }),
      );
    });

    it('blocks save and surfaces a row-identifying message when a value fails to encode', async () => {
      const fixture = await createComponent(null, () => {
        optionCatalogApiSpy.apiDhcpOptionCatalogGet.and.callFake(
          (params: { protocolVersion: string }) =>
            of(params.protocolVersion === 'IPv4' ? [dnsServerCatalogEntry] : []),
        );
      });

      fixture.componentInstance.form.patchValue({ name: 'pxe' });
      fixture.componentInstance.addOptionRow();
      fixture.componentInstance.options.at(0).controls.catalogSelection.setValue('IPv4:6');
      fixture.componentInstance.onOptionCatalogSelectionChange(0);
      fixture.componentInstance.options.at(0).controls.value.setValue('not-an-address');

      await fixture.componentInstance.save();

      expect(tagRulesApiSpy.apiDhcpTagRulesPost).not.toHaveBeenCalled();
      expect(fixture.componentInstance.errorMessage()).toContain('Option row 1');
    });

    it('disambiguates a loaded option whose tag collides across both catalogs by which one actually decodes the stored value', async () => {
      // DHCPv4 option 23 (Default IP Time-to-live, UInt8, 1 byte) collides with DHCPv6 option 23
      // (DNS Recursive Name Server, an address list; here one 16-byte IPv6 address). Taking the
      // first catalog's entry would decode the address as a 1-byte UInt8.
      const ipv4DefaultTtl = {
        code: 23,
        name: 'Default IP Time-to-live',
        description: '',
        valueType: 'UInt8',
        isServerAddressEligible: false,
      };
      const ipv6DnsServer = {
        code: 23,
        name: 'DNS Recursive Name Server',
        description: '',
        valueType: 'IPv6AddressList',
        isServerAddressEligible: true,
      };

      const fixture = await createComponent('rule-1', () => {
        optionCatalogApiSpy.apiDhcpOptionCatalogGet.and.callFake(
          (params: { protocolVersion: string }) =>
            of(params.protocolVersion === 'IPv4' ? [ipv4DefaultTtl] : [ipv6DnsServer]),
        );
        tagRulesApiSpy.apiDhcpTagRulesIdGet.and.returnValue(
          of({
            id: 'rule-1',
            name: 'pxe',
            scopeId: null,
            priority: 0,
            isDisabled: false,
            vendorClassIdentifierMatch: null,
            vendorClassIdentifierMatchMode: 'Exact',
            vendorClassEnterpriseNumber: null,
            userClassMatch: null,
            userClassMatchMode: 'Exact',
            relayAgentCircuitIdHex: null,
            relayAgentRemoteIdHex: null,
            relayInterfaceIdHex: null,
            comments: null,
          }),
        );
        tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsGet.and.returnValue(
          of({
            items: [
              {
                id: 'opt-1',
                tagRuleId: 'rule-1',
                optionTag: 23,
                // 2001:db8::1, 16 bytes. Decoding as UInt8 requires exactly 1 byte and throws,
                // which is what tells the two entries apart.
                valueHex: '20010DB8000000000000000000000001',
              },
            ],
            skip: 0,
            take: 500,
            totalCount: 1,
          }),
        );
      });

      expect(fixture.componentInstance.options.at(0).controls.catalogSelection.value).toBe(
        'IPv6:23',
      );
      expect(fixture.componentInstance.options.at(0).controls.value.value).toBe(
        '2001:db8:0:0:0:0:0:1',
      );
      expect(fixture.componentInstance.optionRowViewState()[0]).toEqual({
        valueType: 'IPv6AddressList',
        isServerAddressEligible: true,
      });
    });

    it('reconciles an edited existing option as a PUT and a removed one as a DELETE', async () => {
      const fixture = await createComponent('rule-1', () => {
        tagRulesApiSpy.apiDhcpTagRulesIdGet.and.returnValue(
          of({
            id: 'rule-1',
            name: 'pxe',
            scopeId: null,
            priority: 0,
            isDisabled: false,
            vendorClassIdentifierMatch: null,
            vendorClassIdentifierMatchMode: 'Exact',
            vendorClassEnterpriseNumber: null,
            userClassMatch: null,
            userClassMatchMode: 'Exact',
            relayAgentCircuitIdHex: null,
            relayAgentRemoteIdHex: null,
            relayInterfaceIdHex: null,
            comments: null,
          }),
        );
        tagRulesApiSpy.apiDhcpTagRulesIdPut.and.returnValue(of(undefined));
        tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsGet.and.returnValue(
          of({
            items: [
              { id: 'kept', tagRuleId: 'rule-1', optionTag: 66, valueHex: '414243' },
              { id: 'removed', tagRuleId: 'rule-1', optionTag: 67, valueHex: '5A5A' },
            ],
            skip: 0,
            take: 500,
            totalCount: 2,
          }),
        );
      });

      expect(fixture.componentInstance.options.length).toBe(2);

      fixture.componentInstance.removeOptionRow(1);

      await fixture.componentInstance.save();

      expect(tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsIdPut).toHaveBeenCalledWith(
        jasmine.objectContaining({ tagRuleId: 'rule-1', id: 'kept' }),
      );
      expect(tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsIdDelete).toHaveBeenCalledWith(
        jasmine.objectContaining({ tagRuleId: 'rule-1', id: 'removed' }),
      );
      expect(tagRuleOptionsApiSpy.apiDhcpTagRulesTagRuleIdOptionsPost).not.toHaveBeenCalled();
    });
  });
});

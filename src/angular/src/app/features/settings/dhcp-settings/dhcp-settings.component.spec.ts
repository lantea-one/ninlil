import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { DhcpSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { DhcpSettingsComponent } from './dhcp-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('DhcpSettingsComponent', () => {
  let dhcpSettingsApiSpy: {
    apiDhcpSettingsCoreGet: jasmine.Spy;
    apiDhcpSettingsCorePatch: jasmine.Spy;
    apiDhcpSettingsProtocolGet: jasmine.Spy;
    apiDhcpSettingsProtocolPatch: jasmine.Spy;
    apiDhcpSettingsDhcpv6ProtocolGet: jasmine.Spy;
    apiDhcpSettingsDhcpv6ProtocolPatch: jasmine.Spy;
    apiDhcpSettingsRelayGet: jasmine.Spy;
    apiDhcpSettingsRelayPatch: jasmine.Spy;
    apiDhcpSettingsDhcpv6RelayGet: jasmine.Spy;
    apiDhcpSettingsDhcpv6RelayPatch: jasmine.Spy;
    apiDhcpSettingsTftpGet: jasmine.Spy;
    apiDhcpSettingsTftpPatch: jasmine.Spy;
    apiDhcpSettingsRaProtocolGet: jasmine.Spy;
    apiDhcpSettingsRaProtocolPatch: jasmine.Spy;
    apiDhcpSettingsRaGet: jasmine.Spy;
    apiDhcpSettingsRaPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(raGetResponse?: object) {
    dhcpSettingsApiSpy = jasmine.createSpyObj('DhcpSettingsService', [
      'apiDhcpSettingsCoreGet',
      'apiDhcpSettingsCorePatch',
      'apiDhcpSettingsProtocolGet',
      'apiDhcpSettingsProtocolPatch',
      'apiDhcpSettingsDhcpv6ProtocolGet',
      'apiDhcpSettingsDhcpv6ProtocolPatch',
      'apiDhcpSettingsRelayGet',
      'apiDhcpSettingsRelayPatch',
      'apiDhcpSettingsDhcpv6RelayGet',
      'apiDhcpSettingsDhcpv6RelayPatch',
      'apiDhcpSettingsTftpGet',
      'apiDhcpSettingsTftpPatch',
      'apiDhcpSettingsRaProtocolGet',
      'apiDhcpSettingsRaProtocolPatch',
      'apiDhcpSettingsRaGet',
      'apiDhcpSettingsRaPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    dhcpSettingsApiSpy.apiDhcpSettingsCoreGet.and.returnValue(
      of({
        serverIdentifierAddress: null,
        offerHoldDuration: '00:02:00',
        declinedAddressQuarantineDuration: '00:15:00',
      }),
    );
    dhcpSettingsApiSpy.apiDhcpSettingsProtocolGet.and.returnValue(
      of({ enable: true, serverPort: 67, clientPort: 68 }),
    );
    dhcpSettingsApiSpy.apiDhcpSettingsDhcpv6ProtocolGet.and.returnValue(
      of({ enable: false, serverPort: 547, clientPort: 546 }),
    );
    dhcpSettingsApiSpy.apiDhcpSettingsRelayGet.and.returnValue(
      of({
        enable: false,
        upstreamServerAddresses: [],
        upstreamServerPort: 67,
        relayAgentAddress: null,
        relayAgentCircuitId: null,
        relayAgentRemoteId: null,
      }),
    );
    dhcpSettingsApiSpy.apiDhcpSettingsDhcpv6RelayGet.and.returnValue(
      of({
        enable: false,
        upstreamServerAddresses: [],
        upstreamServerPort: 547,
        relayLinkAddress: null,
        interfaceId: null,
      }),
    );
    dhcpSettingsApiSpy.apiDhcpSettingsTftpGet.and.returnValue(
      of({
        enable: false,
        port: 69,
        rootDirectory: null,
        retransmitTimeoutSeconds: 2,
        maxRetransmissions: 5,
      }),
    );
    dhcpSettingsApiSpy.apiDhcpSettingsRaProtocolGet.and.returnValue(
      of({
        enable: false,
        minRouterAdvertisementInterval: '00:03:18',
        maxRouterAdvertisementInterval: '00:10:00',
      }),
    );
    dhcpSettingsApiSpy.apiDhcpSettingsRaGet.and.returnValue(
      of(
        raGetResponse ?? {
          managedAddressConfiguration: false,
          otherConfiguration: false,
          routerLifetime: '00:00:00',
          currentHopLimit: 64,
          reachableTime: '00:00:00',
          retransTimer: '00:00:00',
          linkMtu: null,
          recursiveDnsServers: [],
          recursiveDnsServerLifetime: '00:30:00',
          dnsSearchListDomains: [],
          dnsSearchListLifetime: '00:30:00',
          routes: [],
        },
      ),
    );

    await TestBed.configureTestingModule({
      imports: [DhcpSettingsComponent],
      providers: [
        { provide: DhcpSettingsService, useValue: dhcpSettingsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads every section on init', async () => {
    const fixture = await createComponent();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsCoreGet).toHaveBeenCalled();
    expect(dhcpSettingsApiSpy.apiDhcpSettingsProtocolGet).toHaveBeenCalled();
    expect(dhcpSettingsApiSpy.apiDhcpSettingsDhcpv6ProtocolGet).toHaveBeenCalled();
    expect(dhcpSettingsApiSpy.apiDhcpSettingsRelayGet).toHaveBeenCalled();
    expect(dhcpSettingsApiSpy.apiDhcpSettingsDhcpv6RelayGet).toHaveBeenCalled();
    expect(dhcpSettingsApiSpy.apiDhcpSettingsTftpGet).toHaveBeenCalled();
    expect(dhcpSettingsApiSpy.apiDhcpSettingsRaProtocolGet).toHaveBeenCalled();
    expect(dhcpSettingsApiSpy.apiDhcpSettingsRaGet).toHaveBeenCalled();
    expect(fixture.componentInstance.protocolForm.controls.serverPort.value).toBe(67);
    expect(fixture.componentInstance.dhcpv6ProtocolForm.controls.serverPort.value).toBe(547);
    expect(fixture.componentInstance.tftpForm.controls.port.value).toBe(69);
    expect(
      fixture.componentInstance.raProtocolForm.controls.minRouterAdvertisementInterval.value,
    ).toBe('00:03:18');
    expect(fixture.componentInstance.raForm.controls.currentHopLimit.value).toBe(64);
  });

  it('saveCore() PATCHes core settings and surfaces the restart-required note', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsCorePatch.and.returnValue(
      of({ note: 'Saved. A restart is required for this change to take effect.' }),
    );

    await fixture.componentInstance.saveCore();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsCorePatch).toHaveBeenCalled();
    expect(notificationSpy.success).toHaveBeenCalledWith(
      'Saved. A restart is required for this change to take effect.',
    );
  });

  it('saveProtocol() PATCHes protocol settings', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsProtocolPatch.and.returnValue(of({ note: 'Saved.' }));

    await fixture.componentInstance.saveProtocol();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsProtocolPatch).toHaveBeenCalledWith({
      body: { enable: true, serverPort: 67, clientPort: 68 },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('saveDhcpv6Protocol() PATCHes DHCPv6 protocol settings', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsDhcpv6ProtocolPatch.and.returnValue(of({ note: 'Saved.' }));
    fixture.componentInstance.dhcpv6ProtocolForm.patchValue({ enable: true });

    await fixture.componentInstance.saveDhcpv6Protocol();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsDhcpv6ProtocolPatch).toHaveBeenCalledWith({
      body: { enable: true, serverPort: 547, clientPort: 546 },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('saveRelay() splits the upstream addresses textarea into an array', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsRelayPatch.and.returnValue(of({ note: 'Saved.' }));
    fixture.componentInstance.relayForm.patchValue({
      upstreamServerAddresses: '192.168.1.1\n192.168.1.2',
    });

    await fixture.componentInstance.saveRelay();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsRelayPatch).toHaveBeenCalledWith(
      jasmine.objectContaining({
        body: jasmine.objectContaining({
          upstreamServerAddresses: ['192.168.1.1', '192.168.1.2'],
        }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('saveDhcpv6Relay() splits the upstream addresses textarea into an array', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsDhcpv6RelayPatch.and.returnValue(of({ note: 'Saved.' }));
    fixture.componentInstance.dhcpv6RelayForm.patchValue({
      upstreamServerAddresses: '2001:db8::1\n2001:db8::2',
    });

    await fixture.componentInstance.saveDhcpv6Relay();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsDhcpv6RelayPatch).toHaveBeenCalledWith(
      jasmine.objectContaining({
        body: jasmine.objectContaining({
          upstreamServerAddresses: ['2001:db8::1', '2001:db8::2'],
        }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('saveTftp() PATCHes TFTP settings and surfaces the restart-required note', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsTftpPatch.and.returnValue(
      of({ note: 'Saved. A restart is required for this change to take effect.' }),
    );
    fixture.componentInstance.tftpForm.patchValue({
      enable: true,
      rootDirectory: '/var/lib/ninlil/tftp',
    });

    await fixture.componentInstance.saveTftp();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsTftpPatch).toHaveBeenCalledWith(
      jasmine.objectContaining({
        body: jasmine.objectContaining({
          enable: true,
          rootDirectory: '/var/lib/ninlil/tftp',
        }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalledWith(
      'Saved. A restart is required for this change to take effect.',
    );
  });

  it('saveRaProtocol() PATCHes Router Advertisement protocol settings', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsRaProtocolPatch.and.returnValue(
      of({ note: 'Saved. A restart is required for this change to take effect.' }),
    );
    fixture.componentInstance.raProtocolForm.patchValue({ enable: true });

    await fixture.componentInstance.saveRaProtocol();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsRaProtocolPatch).toHaveBeenCalledWith(
      jasmine.objectContaining({
        body: jasmine.objectContaining({ enable: true }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalledWith(
      'Saved. A restart is required for this change to take effect.',
    );
  });

  it('saveRa() splits the Recursive DNS Server and DNS Search List textareas into arrays', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsRaPatch.and.returnValue(of({ note: 'Saved.' }));
    fixture.componentInstance.raForm.patchValue({
      recursiveDnsServers: '2001:db8::1\n2001:db8::2',
      dnsSearchListDomains: 'homelab.internal\nexample.com',
    });

    await fixture.componentInstance.saveRa();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsRaPatch).toHaveBeenCalledWith(
      jasmine.objectContaining({
        body: jasmine.objectContaining({
          recursiveDnsServers: ['2001:db8::1', '2001:db8::2'],
          dnsSearchListDomains: ['homelab.internal', 'example.com'],
        }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('loading a route from GetRa populates the routes FormArray', async () => {
    const fixture = await createComponent({
      managedAddressConfiguration: false,
      otherConfiguration: false,
      routerLifetime: '00:00:00',
      currentHopLimit: 64,
      reachableTime: '00:00:00',
      retransTimer: '00:00:00',
      linkMtu: null,
      recursiveDnsServers: [],
      recursiveDnsServerLifetime: '00:30:00',
      dnsSearchListDomains: [],
      dnsSearchListLifetime: '00:30:00',
      routes: [
        { prefix: '2001:db8:5::', prefixLength: 64, preference: 'High', routeLifetime: '00:30:00' },
      ],
    });

    expect(fixture.componentInstance.routes.length).toBe(1);
    expect(fixture.componentInstance.routes.at(0).value).toEqual({
      prefix: '2001:db8:5::',
      prefixLength: 64,
      preference: 'High',
      routeLifetime: '00:30:00',
    });
  });

  it('addRoute() appends an empty row with Medium/30-minute defaults, removeRoute() removes it', async () => {
    const fixture = await createComponent();
    expect(fixture.componentInstance.routes.length).toBe(0);

    fixture.componentInstance.addRoute();

    expect(fixture.componentInstance.routes.length).toBe(1);
    expect(fixture.componentInstance.routes.at(0).value).toEqual({
      prefix: '',
      prefixLength: 64,
      preference: 'Medium',
      routeLifetime: '00:30:00',
    });

    fixture.componentInstance.removeRoute(0);

    expect(fixture.componentInstance.routes.length).toBe(0);
  });

  it('saveRa() sends every route row as a genuine whole-array replace', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsRaPatch.and.returnValue(of({ note: 'Saved.' }));
    fixture.componentInstance.addRoute();
    fixture.componentInstance.routes.at(0).patchValue({
      prefix: '2001:db8:6::',
      prefixLength: 48,
      preference: 'Low',
      routeLifetime: '01:00:00',
    });

    await fixture.componentInstance.saveRa();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsRaPatch).toHaveBeenCalledWith(
      jasmine.objectContaining({
        body: jasmine.objectContaining({
          routes: [
            {
              prefix: '2001:db8:6::',
              prefixLength: 48,
              preference: 'Low',
              routeLifetime: '01:00:00',
            },
          ],
        }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('loading an interface override from GetRa populates the interfaceOverrides FormArray', async () => {
    const fixture = await createComponent({
      managedAddressConfiguration: false,
      otherConfiguration: false,
      routerLifetime: '00:00:00',
      currentHopLimit: 64,
      reachableTime: '00:00:00',
      retransTimer: '00:00:00',
      linkMtu: null,
      recursiveDnsServers: [],
      recursiveDnsServerLifetime: '00:30:00',
      dnsSearchListDomains: [],
      dnsSearchListLifetime: '00:30:00',
      routes: [],
      interfaceOverrides: [
        {
          interfaceName: 'eth0',
          exclude: false,
          managedAddressConfiguration: true,
          otherConfiguration: null,
          routerLifetime: '00:05:00',
          currentHopLimit: 32,
          reachableTime: null,
          retransTimer: null,
          linkMtu: null,
          recursiveDnsServers: ['2001:db8::1'],
          recursiveDnsServerLifetime: null,
          dnsSearchListDomains: null,
          dnsSearchListLifetime: null,
        },
      ],
    });

    expect(fixture.componentInstance.interfaceOverrides.length).toBe(1);
    const row = fixture.componentInstance.interfaceOverrides.at(0).value;
    expect(row.interfaceName).toBe('eth0');
    expect(row.exclude).toBe(false);
    // A configured field round-trips through the tri-state select as its own string value...
    expect(row.managedAddressConfiguration).toBe('true');
    // ...while an unset (null) field round-trips as the "inherit" empty string.
    expect(row.otherConfiguration).toBe('');
    expect(row.routerLifetime).toBe('00:05:00');
    expect(row.currentHopLimit).toBe('32');
    expect(row.reachableTime).toBe('');
    expect(row.recursiveDnsServers).toBe('2001:db8::1');
    expect(row.dnsSearchListDomains).toBe('');
  });

  it('addInterfaceOverride() appends an empty ("inherit everything") row, removeInterfaceOverride() removes it', async () => {
    const fixture = await createComponent();
    expect(fixture.componentInstance.interfaceOverrides.length).toBe(0);

    fixture.componentInstance.addInterfaceOverride();

    expect(fixture.componentInstance.interfaceOverrides.length).toBe(1);
    expect(fixture.componentInstance.interfaceOverrides.at(0).value).toEqual({
      interfaceName: '',
      exclude: false,
      managedAddressConfiguration: '',
      otherConfiguration: '',
      routerLifetime: '',
      currentHopLimit: '',
      reachableTime: '',
      retransTimer: '',
      linkMtu: '',
      recursiveDnsServers: '',
      recursiveDnsServerLifetime: '',
      dnsSearchListDomains: '',
      dnsSearchListLifetime: '',
    });

    fixture.componentInstance.removeInterfaceOverride(0);

    expect(fixture.componentInstance.interfaceOverrides.length).toBe(0);
  });

  it('saveRa() sends every interface override row as a genuine whole-array replace, parsing blank fields to null', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsRaPatch.and.returnValue(of({ note: 'Saved.' }));
    fixture.componentInstance.addInterfaceOverride();
    fixture.componentInstance.interfaceOverrides.at(0).patchValue({
      interfaceName: 'eth1',
      exclude: true,
    });

    await fixture.componentInstance.saveRa();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsRaPatch).toHaveBeenCalledWith(
      jasmine.objectContaining({
        body: jasmine.objectContaining({
          interfaceOverrides: [
            {
              interfaceName: 'eth1',
              exclude: true,
              managedAddressConfiguration: null,
              otherConfiguration: null,
              routerLifetime: null,
              currentHopLimit: null,
              reachableTime: null,
              retransTimer: null,
              linkMtu: null,
              recursiveDnsServers: null,
              recursiveDnsServerLifetime: null,
              dnsSearchListDomains: null,
              dnsSearchListLifetime: null,
            },
          ],
        }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('saveRa() parses a configured interface override row into its own non-null fields', async () => {
    const fixture = await createComponent();
    dhcpSettingsApiSpy.apiDhcpSettingsRaPatch.and.returnValue(of({ note: 'Saved.' }));
    fixture.componentInstance.addInterfaceOverride();
    fixture.componentInstance.interfaceOverrides.at(0).patchValue({
      interfaceName: 'eth0',
      exclude: false,
      managedAddressConfiguration: 'true',
      otherConfiguration: 'false',
      routerLifetime: '00:05:00',
      currentHopLimit: '32',
      linkMtu: '1500',
      recursiveDnsServers: '2001:db8::1\n2001:db8::2',
      dnsSearchListDomains: 'eth0.internal',
    });

    await fixture.componentInstance.saveRa();

    expect(dhcpSettingsApiSpy.apiDhcpSettingsRaPatch).toHaveBeenCalledWith(
      jasmine.objectContaining({
        body: jasmine.objectContaining({
          interfaceOverrides: [
            jasmine.objectContaining({
              interfaceName: 'eth0',
              exclude: false,
              managedAddressConfiguration: true,
              otherConfiguration: false,
              routerLifetime: '00:05:00',
              currentHopLimit: 32,
              linkMtu: 1500,
              recursiveDnsServers: ['2001:db8::1', '2001:db8::2'],
              dnsSearchListDomains: ['eth0.internal'],
            }),
          ],
        }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });
});

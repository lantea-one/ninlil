import { Component, OnInit, inject, signal } from '@angular/core';
import {
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  DhcpRaInterfaceOverrideDetail,
  DhcpRaRouteDetail,
  DhcpSettingsService,
  RaRoutePreferenceEnum,
} from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  joinLines,
  parseOptionalLines,
  parseOptionalNumber,
  parseOptionalText,
  parseOptionalTriStateBoolean,
  splitLines,
  triStateBooleanToSelectValue,
} from './dhcp-settings.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This type names one advertised-route row's shape, so `getRawValue()` allows dot access under
 * `noPropertyAccessFromIndexSignature`.
 */
type RaRouteFormGroup = FormGroup<{
  prefix: FormControl<string>;
  prefixLength: FormControl<number>;
  preference: FormControl<RaRoutePreferenceEnum>;
  routeLifetime: FormControl<string>;
}>;

/**
 * This type names one Router Advertisement interface override row's shape. Every field except
 * `interfaceName` and `exclude` is text, where blank means "inherit the global value", parsed with
 * the `parseOptional*` helpers in `dhcp-settings.model.ts`; typed number and boolean controls have
 * no blank state. `managedAddressConfiguration` and `otherConfiguration` are three-way selects
 * (`''`, `'true'`, `'false'`) for the same reason.
 */
type RaInterfaceOverrideFormGroup = FormGroup<{
  interfaceName: FormControl<string>;
  exclude: FormControl<boolean>;
  managedAddressConfiguration: FormControl<string>;
  otherConfiguration: FormControl<string>;
  routerLifetime: FormControl<string>;
  currentHopLimit: FormControl<string>;
  reachableTime: FormControl<string>;
  retransTimer: FormControl<string>;
  linkMtu: FormControl<string>;
  recursiveDnsServers: FormControl<string>;
  recursiveDnsServerLifetime: FormControl<string>;
  dnsSearchListDomains: FormControl<string>;
  dnsSearchListLifetime: FormControl<string>;
}>;

/**
 * This component is the "DHCP" settings page (`/settings/dhcp`). It shows one card per
 * `DhcpSettingsController` sub-resource under `/api/dhcp-settings` (`core`, `protocol`,
 * `dhcpv6-protocol`, `relay`, `dhcpv6-relay`, `tftp`, `ra-protocol`, `ra`), each with its own
 * `PATCH`, all needing `dhcpsettings:read` to view and `dhcpsettings:write` to save. The DHCPv6
 * cards match their DHCPv4 counterparts, except that the DHCPv6 relay has one Interface-ID field
 * (RFC 8415) where the DHCPv4 relay has Agent Circuit ID and Remote ID.
 *
 * <p><b>Every DHCP setting needs a restart.</b> `DhcpSettingsController` always returns the
 * restart note (for example, the relay `Enable` flags choose in `Program.cs`, at startup, whether
 * the local engine or the relay agent answers), and each card shows that note after a save.
 *
 * <p>The two Router Advertisement cards split transport and timing (`raProtocolForm`) from content
 * (`raForm`). `raForm` has no control for `RaOptions.Prefixes`, which is set only in the
 * configuration file (see the controller). `raForm.routes` is a `FormArray` with one row per RFC
 * 4191 route, sent as a whole-list replace like `recursiveDnsServers` and `dnsSearchListDomains`.
 * `raForm.interfaceOverrides` gives one interface its own advertisement content, or excludes it;
 * blank fields on a row inherit the global values (see {@link RaInterfaceOverrideFormGroup}).
 * Per-interface prefix and route lists are also configuration-file-only.
 */
@Component({
  selector: 'app-dhcp-settings',
  imports: [
    IfPermissionDirective,
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    FormTextDirective,
    IconDirective,
    SpinnerComponent,
  ],
  templateUrl: './dhcp-settings.component.html',
  styleUrl: './dhcp-settings.component.scss',
})
export class DhcpSettingsComponent implements OnInit {
  private readonly dhcpSettingsApi = inject(DhcpSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly loadErrorMessage = signal<string | null>(null);

  readonly coreSubmitting = signal(false);
  readonly coreErrorMessage = signal<string | null>(null);

  readonly protocolSubmitting = signal(false);
  readonly protocolErrorMessage = signal<string | null>(null);

  readonly dhcpv6ProtocolSubmitting = signal(false);
  readonly dhcpv6ProtocolErrorMessage = signal<string | null>(null);

  readonly relaySubmitting = signal(false);
  readonly relayErrorMessage = signal<string | null>(null);

  readonly dhcpv6RelaySubmitting = signal(false);
  readonly dhcpv6RelayErrorMessage = signal<string | null>(null);

  readonly tftpSubmitting = signal(false);
  readonly tftpErrorMessage = signal<string | null>(null);

  readonly raProtocolSubmitting = signal(false);
  readonly raProtocolErrorMessage = signal<string | null>(null);

  readonly raSubmitting = signal(false);
  readonly raErrorMessage = signal<string | null>(null);

  readonly coreForm = this.formBuilder.group({
    serverIdentifierAddress: [''],
    offerHoldDuration: ['00:02:00', [Validators.required]],
    declinedAddressQuarantineDuration: ['00:15:00', [Validators.required]],
  });

  readonly protocolForm = this.formBuilder.group({
    enable: [true],
    serverPort: [67, [Validators.required, Validators.min(1), Validators.max(65535)]],
    clientPort: [68, [Validators.required, Validators.min(1), Validators.max(65535)]],
  });

  readonly dhcpv6ProtocolForm = this.formBuilder.group({
    enable: [false],
    serverPort: [547, [Validators.required, Validators.min(1), Validators.max(65535)]],
    clientPort: [546, [Validators.required, Validators.min(1), Validators.max(65535)]],
  });

  readonly relayForm = this.formBuilder.group({
    enable: [false],
    upstreamServerAddresses: [''],
    upstreamServerPort: [67, [Validators.required, Validators.min(1), Validators.max(65535)]],
    relayAgentAddress: [''],
    relayAgentCircuitId: [''],
    relayAgentRemoteId: [''],
  });

  readonly dhcpv6RelayForm = this.formBuilder.group({
    enable: [false],
    upstreamServerAddresses: [''],
    upstreamServerPort: [547, [Validators.required, Validators.min(1), Validators.max(65535)]],
    relayLinkAddress: [''],
    interfaceId: [''],
  });

  readonly tftpForm = this.formBuilder.group({
    enable: [false],
    port: [69, [Validators.required, Validators.min(1), Validators.max(65535)]],
    rootDirectory: [''],
    retransmitTimeoutSeconds: [2, [Validators.required, Validators.min(0.1)]],
    maxRetransmissions: [5, [Validators.required, Validators.min(1)]],
  });

  readonly raProtocolForm = this.formBuilder.group({
    enable: [false],
    minRouterAdvertisementInterval: ['00:03:18', [Validators.required]],
    maxRouterAdvertisementInterval: ['00:10:00', [Validators.required]],
  });

  readonly raForm = this.formBuilder.group({
    managedAddressConfiguration: [false],
    otherConfiguration: [false],
    routerLifetime: ['00:00:00', [Validators.required]],
    currentHopLimit: [64, [Validators.required, Validators.min(0), Validators.max(255)]],
    reachableTime: ['00:00:00', [Validators.required]],
    retransTimer: ['00:00:00', [Validators.required]],
    linkMtu: this.formBuilder.control<number | null>(null, [Validators.min(1280)]),
    recursiveDnsServers: [''],
    recursiveDnsServerLifetime: ['00:30:00', [Validators.required]],
    dnsSearchListDomains: [''],
    dnsSearchListLifetime: ['00:30:00', [Validators.required]],
    routes: this.formBuilder.array<RaRouteFormGroup>([]),
    interfaceOverrides: this.formBuilder.array<RaInterfaceOverrideFormGroup>([]),
  });

  /** The advertised route (RFC 4191) rows. */
  get routes() {
    return this.raForm.controls.routes;
  }

  /** The per-interface override rows. */
  get interfaceOverrides() {
    return this.raForm.controls.interfaceOverrides;
  }

  ngOnInit(): void {
    void this.loadAll();
  }

  /** This method appends an empty advertised-route row. */
  addRoute(): void {
    this.routes.push(this.buildRouteGroup(null));
  }

  /** This method removes the advertised-route row at the given index. */
  removeRoute(index: number): void {
    this.routes.removeAt(index);
  }

  /**
   * This method builds one route row from a loaded `DhcpRaRouteDetail`, or with the defaults of
   * `RaAdvertisedRouteOptions` (Medium preference, 30-minute lifetime) for a new row.
   */
  private buildRouteGroup(route: DhcpRaRouteDetail | null): RaRouteFormGroup {
    return this.formBuilder.group({
      prefix: [route?.prefix ?? ''],
      prefixLength: [
        route?.prefixLength ?? 64,
        [Validators.required, Validators.min(0), Validators.max(128)],
      ],
      preference: [route?.preference ?? RaRoutePreferenceEnum.Medium],
      routeLifetime: [route?.routeLifetime ?? '00:30:00', [Validators.required]],
    });
  }

  /** This method appends an empty interface override row. */
  addInterfaceOverride(): void {
    this.interfaceOverrides.push(this.buildInterfaceOverrideGroup(null));
  }

  /** This method removes the interface override row at the given index. */
  removeInterfaceOverride(index: number): void {
    this.interfaceOverrides.removeAt(index);
  }

  /**
   * This method builds one interface override row from a loaded `DhcpRaInterfaceOverrideDetail`, or
   * blank for a new row, so every field inherits the global value until set.
   */
  private buildInterfaceOverrideGroup(
    override: DhcpRaInterfaceOverrideDetail | null,
  ): RaInterfaceOverrideFormGroup {
    return this.formBuilder.group({
      interfaceName: [override?.interfaceName ?? '', [Validators.required]],
      exclude: [override?.exclude ?? false],
      managedAddressConfiguration: [
        triStateBooleanToSelectValue(override?.managedAddressConfiguration),
      ],
      otherConfiguration: [triStateBooleanToSelectValue(override?.otherConfiguration)],
      routerLifetime: [override?.routerLifetime ?? ''],
      currentHopLimit: [override?.currentHopLimit != null ? String(override.currentHopLimit) : ''],
      reachableTime: [override?.reachableTime ?? ''],
      retransTimer: [override?.retransTimer ?? ''],
      linkMtu: [override?.linkMtu != null ? String(override.linkMtu) : '', [Validators.min(1280)]],
      recursiveDnsServers: [joinLines(override?.recursiveDnsServers)],
      recursiveDnsServerLifetime: [override?.recursiveDnsServerLifetime ?? ''],
      dnsSearchListDomains: [joinLines(override?.dnsSearchListDomains)],
      dnsSearchListLifetime: [override?.dnsSearchListLifetime ?? ''],
    });
  }

  /** This method PATCHes the settings shared by every local DHCP protocol engine. */
  async saveCore(): Promise<void> {
    if (this.coreForm.invalid || this.coreSubmitting()) return;

    this.coreSubmitting.set(true);
    this.coreErrorMessage.set(null);

    const value = this.coreForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.dhcpSettingsApi.apiDhcpSettingsCorePatch({
          body: {
            serverIdentifierAddress: value.serverIdentifierAddress || null,
            offerHoldDuration: value.offerHoldDuration,
            declinedAddressQuarantineDuration: value.declinedAddressQuarantineDuration,
          },
        }),
      );
      this.notification.success(result.note ?? 'Core DHCP settings saved.');
    } catch (error) {
      this.coreErrorMessage.set(extractErrorMessage(error, 'Could not save core DHCP settings.'));
    } finally {
      this.coreSubmitting.set(false);
    }
  }

  /** This method PATCHes the DHCPv4 protocol settings (enable and ports). */
  async saveProtocol(): Promise<void> {
    if (this.protocolForm.invalid || this.protocolSubmitting()) return;

    this.protocolSubmitting.set(true);
    this.protocolErrorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.dhcpSettingsApi.apiDhcpSettingsProtocolPatch({
          body: this.protocolForm.getRawValue(),
        }),
      );
      this.notification.success(result.note ?? 'DHCPv4 protocol settings saved.');
    } catch (error) {
      this.protocolErrorMessage.set(
        extractErrorMessage(error, 'Could not save DHCPv4 protocol settings.'),
      );
    } finally {
      this.protocolSubmitting.set(false);
    }
  }

  /** This method PATCHes the DHCPv6 protocol settings (enable and ports). */
  async saveDhcpv6Protocol(): Promise<void> {
    if (this.dhcpv6ProtocolForm.invalid || this.dhcpv6ProtocolSubmitting()) return;

    this.dhcpv6ProtocolSubmitting.set(true);
    this.dhcpv6ProtocolErrorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.dhcpSettingsApi.apiDhcpSettingsDhcpv6ProtocolPatch({
          body: this.dhcpv6ProtocolForm.getRawValue(),
        }),
      );
      this.notification.success(result.note ?? 'DHCPv6 protocol settings saved.');
    } catch (error) {
      this.dhcpv6ProtocolErrorMessage.set(
        extractErrorMessage(error, 'Could not save DHCPv6 protocol settings.'),
      );
    } finally {
      this.dhcpv6ProtocolSubmitting.set(false);
    }
  }

  /** This method PATCHes the DHCPv4 relay agent settings. */
  async saveRelay(): Promise<void> {
    if (this.relayForm.invalid || this.relaySubmitting()) return;

    this.relaySubmitting.set(true);
    this.relayErrorMessage.set(null);

    const value = this.relayForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.dhcpSettingsApi.apiDhcpSettingsRelayPatch({
          body: {
            enable: value.enable,
            upstreamServerAddresses: splitLines(value.upstreamServerAddresses),
            upstreamServerPort: value.upstreamServerPort,
            relayAgentAddress: value.relayAgentAddress || null,
            relayAgentCircuitId: value.relayAgentCircuitId || null,
            relayAgentRemoteId: value.relayAgentRemoteId || null,
          },
        }),
      );
      this.notification.success(result.note ?? 'DHCPv4 relay settings saved.');
    } catch (error) {
      this.relayErrorMessage.set(
        extractErrorMessage(error, 'Could not save DHCPv4 relay settings.'),
      );
    } finally {
      this.relaySubmitting.set(false);
    }
  }

  /** This method PATCHes the RFC 8415 DHCPv6 relay agent settings. */
  async saveDhcpv6Relay(): Promise<void> {
    if (this.dhcpv6RelayForm.invalid || this.dhcpv6RelaySubmitting()) return;

    this.dhcpv6RelaySubmitting.set(true);
    this.dhcpv6RelayErrorMessage.set(null);

    const value = this.dhcpv6RelayForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.dhcpSettingsApi.apiDhcpSettingsDhcpv6RelayPatch({
          body: {
            enable: value.enable,
            upstreamServerAddresses: splitLines(value.upstreamServerAddresses),
            upstreamServerPort: value.upstreamServerPort,
            relayLinkAddress: value.relayLinkAddress || null,
            interfaceId: value.interfaceId || null,
          },
        }),
      );
      this.notification.success(result.note ?? 'DHCPv6 relay settings saved.');
    } catch (error) {
      this.dhcpv6RelayErrorMessage.set(
        extractErrorMessage(error, 'Could not save DHCPv6 relay settings.'),
      );
    } finally {
      this.dhcpv6RelaySubmitting.set(false);
    }
  }

  /** This method PATCHes the bundled read-only TFTP server settings. */
  async saveTftp(): Promise<void> {
    if (this.tftpForm.invalid || this.tftpSubmitting()) return;

    this.tftpSubmitting.set(true);
    this.tftpErrorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.dhcpSettingsApi.apiDhcpSettingsTftpPatch({
          body: this.tftpForm.getRawValue(),
        }),
      );
      this.notification.success(result.note ?? 'TFTP server settings saved.');
    } catch (error) {
      this.tftpErrorMessage.set(extractErrorMessage(error, 'Could not save TFTP server settings.'));
    } finally {
      this.tftpSubmitting.set(false);
    }
  }

  /** This method PATCHes the Router Advertisement enable flag and timing. */
  async saveRaProtocol(): Promise<void> {
    if (this.raProtocolForm.invalid || this.raProtocolSubmitting()) return;

    this.raProtocolSubmitting.set(true);
    this.raProtocolErrorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.dhcpSettingsApi.apiDhcpSettingsRaProtocolPatch({
          body: this.raProtocolForm.getRawValue(),
        }),
      );
      this.notification.success(result.note ?? 'Router Advertisement protocol settings saved.');
    } catch (error) {
      this.raProtocolErrorMessage.set(
        extractErrorMessage(error, 'Could not save Router Advertisement protocol settings.'),
      );
    } finally {
      this.raProtocolSubmitting.set(false);
    }
  }

  /**
   * This method PATCHes the Router Advertisement content, everything except `RaOptions.Prefixes`.
   * `routes` and `interfaceOverrides` replace the stored lists as a whole.
   */
  async saveRa(): Promise<void> {
    if (this.raForm.invalid || this.raSubmitting()) return;

    this.raSubmitting.set(true);
    this.raErrorMessage.set(null);

    const value = this.raForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.dhcpSettingsApi.apiDhcpSettingsRaPatch({
          body: {
            managedAddressConfiguration: value.managedAddressConfiguration,
            otherConfiguration: value.otherConfiguration,
            routerLifetime: value.routerLifetime,
            currentHopLimit: value.currentHopLimit,
            reachableTime: value.reachableTime,
            retransTimer: value.retransTimer,
            linkMtu: value.linkMtu,
            recursiveDnsServers: splitLines(value.recursiveDnsServers),
            recursiveDnsServerLifetime: value.recursiveDnsServerLifetime,
            dnsSearchListDomains: splitLines(value.dnsSearchListDomains),
            dnsSearchListLifetime: value.dnsSearchListLifetime,
            routes: value.routes.map((route) => ({
              prefix: route.prefix || null,
              prefixLength: route.prefixLength,
              preference: route.preference,
              routeLifetime: route.routeLifetime,
            })),
            interfaceOverrides: value.interfaceOverrides.map((override) => ({
              interfaceName: override.interfaceName,
              exclude: override.exclude,
              managedAddressConfiguration: parseOptionalTriStateBoolean(
                override.managedAddressConfiguration,
              ),
              otherConfiguration: parseOptionalTriStateBoolean(override.otherConfiguration),
              routerLifetime: parseOptionalText(override.routerLifetime),
              currentHopLimit: parseOptionalNumber(override.currentHopLimit),
              reachableTime: parseOptionalText(override.reachableTime),
              retransTimer: parseOptionalText(override.retransTimer),
              linkMtu: parseOptionalNumber(override.linkMtu),
              recursiveDnsServers: parseOptionalLines(override.recursiveDnsServers),
              recursiveDnsServerLifetime: parseOptionalText(override.recursiveDnsServerLifetime),
              dnsSearchListDomains: parseOptionalLines(override.dnsSearchListDomains),
              dnsSearchListLifetime: parseOptionalText(override.dnsSearchListLifetime),
            })),
          },
        }),
      );
      this.notification.success(result.note ?? 'Router Advertisement content settings saved.');
    } catch (error) {
      this.raErrorMessage.set(
        extractErrorMessage(error, 'Could not save Router Advertisement content settings.'),
      );
    } finally {
      this.raSubmitting.set(false);
    }
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    this.loadErrorMessage.set(null);

    try {
      const [core, protocol, dhcpv6Protocol, relay, dhcpv6Relay, tftp, raProtocol, ra] =
        await Promise.all([
          firstValueFrom(this.dhcpSettingsApi.apiDhcpSettingsCoreGet()),
          firstValueFrom(this.dhcpSettingsApi.apiDhcpSettingsProtocolGet()),
          firstValueFrom(this.dhcpSettingsApi.apiDhcpSettingsDhcpv6ProtocolGet()),
          firstValueFrom(this.dhcpSettingsApi.apiDhcpSettingsRelayGet()),
          firstValueFrom(this.dhcpSettingsApi.apiDhcpSettingsDhcpv6RelayGet()),
          firstValueFrom(this.dhcpSettingsApi.apiDhcpSettingsTftpGet()),
          firstValueFrom(this.dhcpSettingsApi.apiDhcpSettingsRaProtocolGet()),
          firstValueFrom(this.dhcpSettingsApi.apiDhcpSettingsRaGet()),
        ]);

      this.coreForm.patchValue({
        serverIdentifierAddress: core.serverIdentifierAddress ?? '',
        offerHoldDuration: core.offerHoldDuration ?? '00:02:00',
        declinedAddressQuarantineDuration: core.declinedAddressQuarantineDuration ?? '00:15:00',
      });

      this.protocolForm.patchValue({
        enable: protocol.enable ?? true,
        serverPort: protocol.serverPort ?? 67,
        clientPort: protocol.clientPort ?? 68,
      });

      this.dhcpv6ProtocolForm.patchValue({
        enable: dhcpv6Protocol.enable ?? false,
        serverPort: dhcpv6Protocol.serverPort ?? 547,
        clientPort: dhcpv6Protocol.clientPort ?? 546,
      });

      this.relayForm.patchValue({
        enable: relay.enable ?? false,
        upstreamServerAddresses: joinLines(relay.upstreamServerAddresses),
        upstreamServerPort: relay.upstreamServerPort ?? 67,
        relayAgentAddress: relay.relayAgentAddress ?? '',
        relayAgentCircuitId: relay.relayAgentCircuitId ?? '',
        relayAgentRemoteId: relay.relayAgentRemoteId ?? '',
      });

      this.dhcpv6RelayForm.patchValue({
        enable: dhcpv6Relay.enable ?? false,
        upstreamServerAddresses: joinLines(dhcpv6Relay.upstreamServerAddresses),
        upstreamServerPort: dhcpv6Relay.upstreamServerPort ?? 547,
        relayLinkAddress: dhcpv6Relay.relayLinkAddress ?? '',
        interfaceId: dhcpv6Relay.interfaceId ?? '',
      });

      this.tftpForm.patchValue({
        enable: tftp.enable ?? false,
        port: tftp.port ?? 69,
        rootDirectory: tftp.rootDirectory ?? '',
        retransmitTimeoutSeconds: tftp.retransmitTimeoutSeconds ?? 2,
        maxRetransmissions: tftp.maxRetransmissions ?? 5,
      });

      this.raProtocolForm.patchValue({
        enable: raProtocol.enable ?? false,
        minRouterAdvertisementInterval: raProtocol.minRouterAdvertisementInterval ?? '00:03:18',
        maxRouterAdvertisementInterval: raProtocol.maxRouterAdvertisementInterval ?? '00:10:00',
      });

      this.raForm.patchValue({
        managedAddressConfiguration: ra.managedAddressConfiguration ?? false,
        otherConfiguration: ra.otherConfiguration ?? false,
        routerLifetime: ra.routerLifetime ?? '00:00:00',
        currentHopLimit: ra.currentHopLimit ?? 64,
        reachableTime: ra.reachableTime ?? '00:00:00',
        retransTimer: ra.retransTimer ?? '00:00:00',
        linkMtu: ra.linkMtu ?? null,
        recursiveDnsServers: joinLines(ra.recursiveDnsServers),
        recursiveDnsServerLifetime: ra.recursiveDnsServerLifetime ?? '00:30:00',
        dnsSearchListDomains: joinLines(ra.dnsSearchListDomains),
        dnsSearchListLifetime: ra.dnsSearchListLifetime ?? '00:30:00',
      });

      this.routes.clear();
      for (const route of ra.routes ?? []) this.routes.push(this.buildRouteGroup(route));

      this.interfaceOverrides.clear();
      for (const override of ra.interfaceOverrides ?? [])
        this.interfaceOverrides.push(this.buildInterfaceOverrideGroup(override));
    } catch (error) {
      this.loadErrorMessage.set(extractErrorMessage(error, 'Could not load DHCP settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

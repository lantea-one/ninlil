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

/**
 * This type alias names one advertised-route row's own typed shape explicitly, the same
 * `noPropertyAccessFromIndexSignature`-driven pattern `AplRecordFormComponent`'s own
 * `AplItemGroup` already establishes for a `FormArray` row.
 */
type RaRouteFormGroup = FormGroup<{
  prefix: FormControl<string>;
  prefixLength: FormControl<number>;
  preference: FormControl<string>;
  routeLifetime: FormControl<string>;
}>;

/**
 * This type alias names one Router Advertisement interface override row's own typed shape
 * explicitly, the same pattern {@link RaRouteFormGroup} already establishes. Every field below
 * except `interfaceName`/`exclude` works in plain text -- a blank value means "inherit the
 * global default," parsed via `dhcp-settings.model.ts`'s own `parseOptional*` helpers rather
 * than Angular's own typed numeric/boolean controls, since a genuinely nullable field has no
 * "blank" representation in those. `managedAddressConfiguration`/`otherConfiguration` are a
 * three-option `<select>` (`''`/`'true'`/`'false'`) for the same reason.
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
 * This component reads and writes every DHCP settings section under one thematic page -- the
 * six `DhcpSettingsController` sub-resources (`core`/`protocol`/`dhcpv6-protocol`/`relay`/
 * `dhcpv6-relay`/`tftp`), one card per section, each with its own independent load/save cycle --
 * the same "one page, several independent cards" shape `TransportSettingsComponent` already
 * establishes for the five DNS transports. The two DHCPv6 cards mirror their DHCPv4 counterparts'
 * exact shape (`dhcpv6ProtocolForm`/`dhcpv6RelayForm`), since `Dhcpv6ProtocolSettingsDetail`/
 * `Dhcpv6RelaySettingsDetail` carry the same fields as `DhcpProtocolSettingsDetail`/
 * `DhcpRelaySettingsDetail`, minus the DHCPv4 relay's own Agent Circuit/Remote ID fields (RFC 8415
 * Section 21.18's own Interface-ID option replaces both with a single free-form field).
 *
 * <p><b>Every field here is genuinely restart-required, with no live toggle at all</b> -- unlike
 * `TransportSettingsComponent`'s own DNS transports, this backend never hot-reloads a DHCP setting:
 * `DhcpSettingsController.ApplyAsync` always returns `RuntimeSettingsWriteResult.RestartRequiredNote`
 * (see that controller's own doc comment for why -- most importantly, `Dhcpv4RelayOptions.Enable`
 * decides which of two mutually exclusive engines even answers a DHCPv4 request at all, decided
 * once in `Program.cs` before dependency injection is built). This component surfaces that note
 * directly in the success notification after every save, the exact mechanism
 * `TransportSettingsComponent` already establishes -- never a bespoke "restart required" banner of
 * its own.
 *
 * <p>The two Router Advertisement cards (`raProtocolForm`/`raForm`) mirror
 * `DhcpSettingsController.GetRaProtocol`/`GetRa`'s own transport-vs-content split. `raForm`
 * deliberately has no control for `RaOptions.Prefixes` -- see that controller's own doc comment
 * for why a list of several independently-flagged/lifetimed prefix objects has no REST/UI surface
 * yet; advertised prefixes remain config-file-only. `raForm.routes` (a `FormArray`, one repeatable
 * row per RFC 4191 route -- add/remove rows the same way `AplRecordFormComponent` already
 * establishes for a repeatable record-data list) does get real coverage: it PATCHes as a genuine
 * whole-array replace, exactly like `recursiveDnsServers`/`dnsSearchListDomains` above it.
 *
 * <p>`raForm.interfaceOverrides` (a `FormArray`, the same repeatable-row shape `routes` already
 * establishes) lets an operator give one network interface its own distinct Router Advertisement
 * content -- or exclude it from Router Advertisement entirely -- rather than every interface
 * receiving the one shared advertisement this page used to be the only way to configure. Every
 * field on a row except `interfaceName`/`exclude` is a plain text control that means "inherit the
 * global default above" when left blank, parsed via `dhcp-settings.model.ts`'s own
 * `parseOptional*` helpers (`RaInterfaceOverrideFormGroup`'s own doc comment explains why a typed
 * numeric/boolean control does not fit a genuinely nullable field). This form deliberately has no
 * control for an override's own would-be nested `prefixes`/`routes` lists -- see
 * `DhcpRaInterfaceOverrideDetail`'s own doc comment for why, mirroring this same page's own
 * `Prefixes` scope boundary one level deeper; both remain config-file-only within an override.
 *
 * <p><b>A generated-client quirk, not a bug in this form</b> (the same one
 * `DhcpTagRuleFormComponent`'s own doc comment already documents for
 * `Dhcpv4TagMatchModeEnum`): each route row's own `preference` control works in the real wire
 * string values (`'High'`/`'Medium'`/`'Low'`) rather than the generated `RaRoutePreferenceEnum`
 * type, since Swashbuckle's OpenAPI schema generation does not reflect
 * `RaRoutePreferenceEnum`'s own server-side `JsonStringEnumConverter`, so the generated TypeScript
 * type is inaccurately numeric. The cast to `RaRoutePreferenceEnum` happens only at the one point
 * a `DhcpRaSettingsPatchRequest` is actually built.
 */
@Component({
  selector: 'app-dhcp-settings',
  imports: [
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
    linkMtu: this.formBuilder.control<number | null>(null),
    recursiveDnsServers: [''],
    recursiveDnsServerLifetime: ['00:30:00', [Validators.required]],
    dnsSearchListDomains: [''],
    dnsSearchListLifetime: ['00:30:00', [Validators.required]],
    routes: this.formBuilder.array<RaRouteFormGroup>([]),
    interfaceOverrides: this.formBuilder.array<RaInterfaceOverrideFormGroup>([]),
  });

  /** Every advertised route (RFC 4191) row, as its own repeatable `FormArray`. */
  get routes() {
    return this.raForm.controls.routes;
  }

  /** Every network interface's own content override row, as its own repeatable `FormArray`. */
  get interfaceOverrides() {
    return this.raForm.controls.interfaceOverrides;
  }

  ngOnInit(): void {
    void this.loadAll();
  }

  /** This method appends a new, empty advertised-route row for the operator to fill in. */
  addRoute(): void {
    this.routes.push(this.buildRouteGroup(null));
  }

  /** This method removes the advertised-route row at the given index. */
  removeRoute(index: number): void {
    this.routes.removeAt(index);
  }

  /**
   * This method builds one route row's own `FormGroup`, seeded from an existing
   * `DhcpRaRouteDetail` when editing, or empty defaults (`'Medium'` preference, a 30-minute
   * lifetime -- the same defaults `RaAdvertisedRouteOptions` itself uses) for a freshly added row.
   */
  private buildRouteGroup(route: DhcpRaRouteDetail | null): RaRouteFormGroup {
    return this.formBuilder.group({
      prefix: [route?.prefix ?? ''],
      prefixLength: [
        route?.prefixLength ?? 64,
        [Validators.required, Validators.min(0), Validators.max(128)],
      ],
      // See this class's own doc comment for why this control works in the real wire string
      // value rather than the generated (inaccurately numeric) RaRoutePreferenceEnum type.
      preference: [(route?.preference as unknown as string) ?? 'Medium'],
      routeLifetime: [route?.routeLifetime ?? '00:30:00', [Validators.required]],
    });
  }

  /** This method appends a new, empty interface override row for the operator to fill in. */
  addInterfaceOverride(): void {
    this.interfaceOverrides.push(this.buildInterfaceOverrideGroup(null));
  }

  /** This method removes the interface override row at the given index. */
  removeInterfaceOverride(index: number): void {
    this.interfaceOverrides.removeAt(index);
  }

  /**
   * This method builds one interface override row's own `FormGroup`, seeded from an existing
   * `DhcpRaInterfaceOverrideDetail` when editing, or empty ("inherit everything") defaults for a
   * freshly added row -- every field but `interfaceName` starts blank, meaning every one of them
   * inherits the global default above until the operator actually overrides one.
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
      linkMtu: [override?.linkMtu != null ? String(override.linkMtu) : ''],
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

  /** This method PATCHes the DHCPv4 UDP transport's own bind/port settings. */
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

  /** This method PATCHes the DHCPv6 UDP transport's own bind/port settings. */
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

  /** This method PATCHes this host's own DHCPv4 relay agent settings. */
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

  /** This method PATCHes this host's own RFC 8415 DHCPv6 relay agent settings. */
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

  /** This method PATCHes this host's own bundled read-only TFTP server settings. */
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

  /** This method PATCHes this host's own Router Advertisement transport/timing settings. */
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
   * This method PATCHes this host's own Router Advertisement content settings -- everything
   * except `RaOptions.Prefixes`, which this page has no control for (see this component's own
   * doc comment). `routes` is sent as a genuine whole-array replace: every route this server
   * should advertise after this save, not merely the ones added since the last one.
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
              preference: route.preference as unknown as RaRoutePreferenceEnum,
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

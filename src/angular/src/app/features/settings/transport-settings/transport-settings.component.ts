import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { TransportSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  buildPfxPasswordPatchField,
  joinLines,
  splitLines,
  splitListenAddresses,
} from './transport-settings.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component reads and writes every DNS transport's own configuration
 * (`TransportSettingsController`'s five sub-resources: Plain, DNS-over-TLS, DNS-over-HTTPS,
 * DNS-over-QUIC, DNSCrypt) -- one card per transport, each with its own independent load/save
 * cycle, since each is its own separate `GET`/`PATCH` REST resource rather than one combined form.
 *
 * <p>Every transport's own `enable` field (Plain has none -- it starts unconditionally, this
 * project's own baseline "this is a DNS server at all" behavior, not a capability-expanding
 * toggle) is genuinely live, with no restart: the backend starts or stops the real,
 * already-running listener(s) to match, through the same Service Lifecycle Management registry
 * `/api/services` itself exposes. Every other field (port, certificate, protocol-specific tuning)
 * is restart-required -- rebinding a socket to a new port fundamentally cannot happen without at
 * least that one transport restarting. Either way, the backend's own `PATCH` response note
 * (surfaced directly in the success notification, never suppressed) says which applies to the
 * exact write just made.
 *
 * <p>A TLS-based transport's own certificate is configured as a path/reference (an operator-
 * provided PFX file, or a self-managed self-signed certificate), never raw key material uploaded
 * through this form -- matching `TlsCertificateOptions` itself. Its own `pfxPassword` follows this
 * project's established reveal-once secret pattern (see `ReverseProxySettingsComponent`'s own
 * `sharedSecret` field): never pre-filled, and left blank on save means "leave unchanged."
 */
@Component({
  selector: 'app-transport-settings',
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
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './transport-settings.component.html',
  styleUrl: './transport-settings.component.scss',
})
export class TransportSettingsComponent implements OnInit {
  private readonly transportApi = inject(TransportSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly loadErrorMessage = signal<string | null>(null);

  readonly plainSubmitting = signal(false);
  readonly plainErrorMessage = signal<string | null>(null);

  readonly dnsOverTlsSubmitting = signal(false);
  readonly dnsOverTlsErrorMessage = signal<string | null>(null);
  readonly dnsOverTlsPfxPasswordConfigured = signal(false);

  readonly dnsOverHttpsSubmitting = signal(false);
  readonly dnsOverHttpsErrorMessage = signal<string | null>(null);

  readonly dnsOverQuicSubmitting = signal(false);
  readonly dnsOverQuicErrorMessage = signal<string | null>(null);
  readonly dnsOverQuicPfxPasswordConfigured = signal(false);

  readonly dnsCryptSubmitting = signal(false);
  readonly dnsCryptErrorMessage = signal<string | null>(null);

  readonly plainForm = this.formBuilder.group({
    port: [53, [Validators.required, Validators.min(1), Validators.max(65535)]],
    listenAddresses: [''],
    tcpIdleTimeout: ['00:00:30', [Validators.required]],
    maxConcurrentTcpConnections: [512, [Validators.required, Validators.min(1)]],
  });

  readonly dnsOverTlsForm = this.formBuilder.group({
    enable: [false],
    port: [853, [Validators.required, Validators.min(1), Validators.max(65535)]],
    listenAddresses: [''],
    idleTimeout: ['00:00:30', [Validators.required]],
    maxConcurrentConnections: [512, [Validators.required, Validators.min(1)]],
    pfxPath: [''],
    pfxPassword: [''],
    selfSignedSubjectAlternativeNames: ['localhost'],
    certificateReloadPollInterval: ['00:05:00', [Validators.required]],
    allowedProtocolTls12: [false],
    allowedProtocolTls13: [false],
  });

  readonly dnsOverHttpsForm = this.formBuilder.group({
    enable: [false],
    path: ['/dns-query', [Validators.required]],
    maxMessageSizeBytes: [65535, [Validators.required, Validators.min(1)]],
  });

  readonly dnsOverQuicForm = this.formBuilder.group({
    enable: [false],
    port: [853, [Validators.required, Validators.min(1), Validators.max(65535)]],
    listenAddresses: [''],
    idleTimeout: ['00:00:30', [Validators.required]],
    maxConcurrentBidirectionalStreams: [100, [Validators.required, Validators.min(1)]],
    pfxPath: [''],
    pfxPassword: [''],
    selfSignedSubjectAlternativeNames: ['localhost'],
    certificateReloadPollInterval: ['00:05:00', [Validators.required]],
    allowedProtocolTls12: [false],
    allowedProtocolTls13: [false],
  });

  readonly dnsCryptForm = this.formBuilder.group({
    enable: [false],
    port: [5353, [Validators.required, Validators.min(1), Validators.max(65535)]],
    listenAddresses: [''],
    providerName: [''],
    tcpIdleTimeout: ['00:00:30', [Validators.required]],
    maxConcurrentTcpConnections: [512, [Validators.required, Validators.min(1)]],
    esVersionXChaCha20Poly1305: [true],
    esVersionXSalsa20Poly1305: [true],
    certificateRotationInterval: ['1.00:00:00', [Validators.required]],
    certificateValidityDuration: ['2.00:00:00', [Validators.required]],
    certificateClockSkewTolerance: ['00:05:00', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadAll();
  }

  /** This method PATCHes Plain DNS's own restart-required fields. */
  async savePlain(): Promise<void> {
    if (this.plainForm.invalid || this.plainSubmitting()) return;

    this.plainSubmitting.set(true);
    this.plainErrorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.transportApi.apiTransportSettingsPlainPatch({
          body: {
            ...this.plainForm.getRawValue(),
            listenAddresses: splitListenAddresses(this.plainForm.getRawValue().listenAddresses),
          },
        }),
      );
      this.notification.success(result.note ?? 'Plain DNS settings saved.');
    } catch (error) {
      this.plainErrorMessage.set(extractErrorMessage(error, 'Could not save Plain DNS settings.'));
    } finally {
      this.plainSubmitting.set(false);
    }
  }

  /** This method PATCHes DNS-over-TLS's own settings, including its own certificate sub-object. */
  async saveDnsOverTls(): Promise<void> {
    if (this.dnsOverTlsForm.invalid || this.dnsOverTlsSubmitting()) return;

    this.dnsOverTlsSubmitting.set(true);
    this.dnsOverTlsErrorMessage.set(null);

    const value = this.dnsOverTlsForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.transportApi.apiTransportSettingsDnsOverTlsPatch({
          body: {
            enable: value.enable,
            port: value.port,
            listenAddresses: splitListenAddresses(value.listenAddresses),
            idleTimeout: value.idleTimeout,
            maxConcurrentConnections: value.maxConcurrentConnections,
            certificate: {
              pfxPath: value.pfxPath,
              ...buildPfxPasswordPatchField(value.pfxPassword),
              selfSignedSubjectAlternativeNames: splitLines(
                value.selfSignedSubjectAlternativeNames,
              ),
              certificateReloadPollInterval: value.certificateReloadPollInterval,
              allowedProtocols: [
                ...(value.allowedProtocolTls12 ? ['Tls12'] : []),
                ...(value.allowedProtocolTls13 ? ['Tls13'] : []),
              ],
            },
          },
        }),
      );
      this.dnsOverTlsForm.patchValue({ pfxPassword: '' });
      this.notification.success(result.note ?? 'DNS-over-TLS settings saved.');
    } catch (error) {
      this.dnsOverTlsErrorMessage.set(
        extractErrorMessage(error, 'Could not save DNS-over-TLS settings.'),
      );
    } finally {
      this.dnsOverTlsSubmitting.set(false);
    }
  }

  /** This method PATCHes DNS-over-HTTPS's own settings. */
  async saveDnsOverHttps(): Promise<void> {
    if (this.dnsOverHttpsForm.invalid || this.dnsOverHttpsSubmitting()) return;

    this.dnsOverHttpsSubmitting.set(true);
    this.dnsOverHttpsErrorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.transportApi.apiTransportSettingsDnsOverHttpsPatch({
          body: this.dnsOverHttpsForm.getRawValue(),
        }),
      );
      this.notification.success(result.note ?? 'DNS-over-HTTPS settings saved.');
    } catch (error) {
      this.dnsOverHttpsErrorMessage.set(
        extractErrorMessage(error, 'Could not save DNS-over-HTTPS settings.'),
      );
    } finally {
      this.dnsOverHttpsSubmitting.set(false);
    }
  }

  /** This method PATCHes DNS-over-QUIC's own settings, including its own certificate sub-object. */
  async saveDnsOverQuic(): Promise<void> {
    if (this.dnsOverQuicForm.invalid || this.dnsOverQuicSubmitting()) return;

    this.dnsOverQuicSubmitting.set(true);
    this.dnsOverQuicErrorMessage.set(null);

    const value = this.dnsOverQuicForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.transportApi.apiTransportSettingsDnsOverQuicPatch({
          body: {
            enable: value.enable,
            port: value.port,
            listenAddresses: splitListenAddresses(value.listenAddresses),
            idleTimeout: value.idleTimeout,
            maxConcurrentBidirectionalStreams: value.maxConcurrentBidirectionalStreams,
            certificate: {
              pfxPath: value.pfxPath,
              ...buildPfxPasswordPatchField(value.pfxPassword),
              selfSignedSubjectAlternativeNames: splitLines(
                value.selfSignedSubjectAlternativeNames,
              ),
              certificateReloadPollInterval: value.certificateReloadPollInterval,
              allowedProtocols: [
                ...(value.allowedProtocolTls12 ? ['Tls12'] : []),
                ...(value.allowedProtocolTls13 ? ['Tls13'] : []),
              ],
            },
          },
        }),
      );
      this.dnsOverQuicForm.patchValue({ pfxPassword: '' });
      this.notification.success(result.note ?? 'DNS-over-QUIC settings saved.');
    } catch (error) {
      this.dnsOverQuicErrorMessage.set(
        extractErrorMessage(error, 'Could not save DNS-over-QUIC settings.'),
      );
    } finally {
      this.dnsOverQuicSubmitting.set(false);
    }
  }

  /** This method PATCHes DNSCrypt's own settings. */
  async saveDnsCrypt(): Promise<void> {
    if (this.dnsCryptForm.invalid || this.dnsCryptSubmitting()) return;

    this.dnsCryptSubmitting.set(true);
    this.dnsCryptErrorMessage.set(null);

    const value = this.dnsCryptForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.transportApi.apiTransportSettingsDnscryptPatch({
          body: {
            enable: value.enable,
            port: value.port,
            listenAddresses: splitListenAddresses(value.listenAddresses),
            providerName: value.providerName,
            tcpIdleTimeout: value.tcpIdleTimeout,
            maxConcurrentTcpConnections: value.maxConcurrentTcpConnections,
            enabledEsVersions: [
              ...(value.esVersionXChaCha20Poly1305 ? ['XChaCha20Poly1305'] : []),
              ...(value.esVersionXSalsa20Poly1305 ? ['XSalsa20Poly1305'] : []),
            ],
            certificateRotationInterval: value.certificateRotationInterval,
            certificateValidityDuration: value.certificateValidityDuration,
            certificateClockSkewTolerance: value.certificateClockSkewTolerance,
          },
        }),
      );
      this.notification.success(result.note ?? 'DNSCrypt settings saved.');
    } catch (error) {
      this.dnsCryptErrorMessage.set(
        extractErrorMessage(error, 'Could not save DNSCrypt settings.'),
      );
    } finally {
      this.dnsCryptSubmitting.set(false);
    }
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    this.loadErrorMessage.set(null);

    try {
      const [plain, dnsOverTls, dnsOverHttps, dnsOverQuic, dnsCrypt] = await Promise.all([
        firstValueFrom(this.transportApi.apiTransportSettingsPlainGet()),
        firstValueFrom(this.transportApi.apiTransportSettingsDnsOverTlsGet()),
        firstValueFrom(this.transportApi.apiTransportSettingsDnsOverHttpsGet()),
        firstValueFrom(this.transportApi.apiTransportSettingsDnsOverQuicGet()),
        firstValueFrom(this.transportApi.apiTransportSettingsDnscryptGet()),
      ]);

      this.plainForm.patchValue({
        port: plain.port ?? 53,
        listenAddresses: joinLines(plain.listenAddresses),
        tcpIdleTimeout: plain.tcpIdleTimeout ?? '00:00:30',
        maxConcurrentTcpConnections: plain.maxConcurrentTcpConnections ?? 512,
      });

      this.dnsOverTlsPfxPasswordConfigured.set(
        dnsOverTls.certificate?.pfxPasswordConfigured ?? false,
      );
      this.dnsOverTlsForm.patchValue({
        enable: dnsOverTls.enable ?? false,
        port: dnsOverTls.port ?? 853,
        listenAddresses: joinLines(dnsOverTls.listenAddresses),
        idleTimeout: dnsOverTls.idleTimeout ?? '00:00:30',
        maxConcurrentConnections: dnsOverTls.maxConcurrentConnections ?? 512,
        pfxPath: dnsOverTls.certificate?.pfxPath ?? '',
        selfSignedSubjectAlternativeNames: joinLines(
          dnsOverTls.certificate?.selfSignedSubjectAlternativeNames,
        ),
        certificateReloadPollInterval:
          dnsOverTls.certificate?.certificateReloadPollInterval ?? '00:05:00',
        allowedProtocolTls12: (dnsOverTls.certificate?.allowedProtocols ?? []).includes('Tls12'),
        allowedProtocolTls13: (dnsOverTls.certificate?.allowedProtocols ?? []).includes('Tls13'),
      });

      this.dnsOverHttpsForm.patchValue({
        enable: dnsOverHttps.enable ?? false,
        path: dnsOverHttps.path ?? '/dns-query',
        maxMessageSizeBytes: dnsOverHttps.maxMessageSizeBytes ?? 65535,
      });

      this.dnsOverQuicPfxPasswordConfigured.set(
        dnsOverQuic.certificate?.pfxPasswordConfigured ?? false,
      );
      this.dnsOverQuicForm.patchValue({
        enable: dnsOverQuic.enable ?? false,
        port: dnsOverQuic.port ?? 853,
        listenAddresses: joinLines(dnsOverQuic.listenAddresses),
        idleTimeout: dnsOverQuic.idleTimeout ?? '00:00:30',
        maxConcurrentBidirectionalStreams: dnsOverQuic.maxConcurrentBidirectionalStreams ?? 100,
        pfxPath: dnsOverQuic.certificate?.pfxPath ?? '',
        selfSignedSubjectAlternativeNames: joinLines(
          dnsOverQuic.certificate?.selfSignedSubjectAlternativeNames,
        ),
        certificateReloadPollInterval:
          dnsOverQuic.certificate?.certificateReloadPollInterval ?? '00:05:00',
        allowedProtocolTls12: (dnsOverQuic.certificate?.allowedProtocols ?? []).includes('Tls12'),
        allowedProtocolTls13: (dnsOverQuic.certificate?.allowedProtocols ?? []).includes('Tls13'),
      });

      this.dnsCryptForm.patchValue({
        enable: dnsCrypt.enable ?? false,
        port: dnsCrypt.port ?? 5353,
        listenAddresses: joinLines(dnsCrypt.listenAddresses),
        providerName: dnsCrypt.providerName ?? '',
        tcpIdleTimeout: dnsCrypt.tcpIdleTimeout ?? '00:00:30',
        maxConcurrentTcpConnections: dnsCrypt.maxConcurrentTcpConnections ?? 512,
        esVersionXChaCha20Poly1305: (dnsCrypt.enabledEsVersions ?? []).includes(
          'XChaCha20Poly1305',
        ),
        esVersionXSalsa20Poly1305: (dnsCrypt.enabledEsVersions ?? []).includes('XSalsa20Poly1305'),
        certificateRotationInterval: dnsCrypt.certificateRotationInterval ?? '1.00:00:00',
        certificateValidityDuration: dnsCrypt.certificateValidityDuration ?? '2.00:00:00',
        certificateClockSkewTolerance: dnsCrypt.certificateClockSkewTolerance ?? '00:05:00',
      });
    } catch (error) {
      this.loadErrorMessage.set(extractErrorMessage(error, 'Could not load transport settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

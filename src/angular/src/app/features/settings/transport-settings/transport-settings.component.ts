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
 * This component is the "Transports" settings page (`/settings/transports`). It shows one card per
 * `TransportSettingsController` sub-resource under `/api/transport-settings` (plain DNS,
 * DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC, DNSCrypt), each loaded and saved on its own, with
 * `transportsettings:read` to view and `transportsettings:write` to save.
 *
 * <p>Each transport's `enable` switch applies at once: the backend starts or stops the listener
 * through the service registry behind `/api/services`. Plain DNS has no switch; it always runs.
 * Other fields (port, certificate, protocol tuning) apply when that transport's service restarts
 * (from the Services page) or the server restarts. The backend's note in the success notification
 * says which applies.
 *
 * <p>A TLS transport's certificate is a PFX file path or a self-signed certificate the server
 * manages, as in `TlsCertificateOptions`; no key material is uploaded here. The PFX password is
 * never returned, so a blank field means "keep the stored one" (see `buildPfxPasswordPatchField`).
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

  /** This method PATCHes the plain DNS settings, which all need a restart. */
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

  /** This method PATCHes the DNS-over-TLS settings, including the certificate. */
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

  /** This method PATCHes the DNS-over-HTTPS settings. */
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

  /** This method PATCHes the DNS-over-QUIC settings, including the certificate. */
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

  /** This method PATCHes the DNSCrypt settings. */
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

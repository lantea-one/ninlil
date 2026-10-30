import { Component, OnInit, Signal, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
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
import { Observable, firstValueFrom, map } from 'rxjs';
import { TransportSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  changedFields,
  hasChangedFields,
} from '../../../shared/settings-patch/changed-fields.util';
import {
  buildPfxPasswordPatchField,
  joinLines,
  splitLines,
  splitListenAddresses,
} from './transport-settings.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This interface is the changed-only state of one transport card: the fields edited since the
 * card was last loaded or saved, and a way to move the baseline.
 */
interface ChangeTracker<TValue extends object, TBody extends object> {
  /** The fields that differ from the baseline, the body of the card's next `PATCH`. */
  readonly changes: Signal<Partial<TBody>>;
  /** Whether any field differs from the baseline. */
  readonly hasChanges: Signal<boolean>;
  /** Makes the given form value the baseline, after a load or a successful save. */
  markClean(value: TValue): void;
}

/**
 * This function tracks one card's form against the `PATCH` body it last loaded or saved. The
 * transport controllers pick the note by which fields a `PATCH` holds (the `enable` switch applies
 * at once, the rest needs a listener or process restart), so a card sends only the fields the user
 * changed. It must run in an injection context (a field initializer), because `toSignal` needs one.
 *
 * @param form The card's form.
 * @param toBody Turns a form value into the request-shaped body, so lists are arrays on both sides
 *   of the comparison and a secret is present only while it is typed.
 */
function trackChanges<TValue extends object, TBody extends object>(
  form: { valueChanges: Observable<unknown>; getRawValue(): TValue },
  toBody: (value: TValue) => TBody,
): ChangeTracker<TValue, TBody> {
  const formValue = toSignal(form.valueChanges.pipe(map(() => form.getRawValue())), {
    initialValue: form.getRawValue(),
  });
  const baseline = signal<TBody | null>(null);
  const changes = computed(() => {
    const loaded = baseline();
    return loaded === null ? {} : changedFields(loaded, toBody(formValue()));
  });

  return {
    changes,
    hasChanges: computed(() => hasChangedFields(changes())),
    markClean: (value) => baseline.set(toBody(value)),
  };
}

/** This type is the form value both TLS-based transports (DNS-over-TLS and DNS-over-QUIC) share. */
interface CertificateFormValue {
  pfxPath: string;
  pfxPassword: string;
  selfSignedSubjectAlternativeNames: string;
  certificateReloadPollInterval: string;
  allowedProtocolTls12: boolean;
  allowedProtocolTls13: boolean;
}

/**
 * This function builds the `certificate` part of a DNS-over-TLS or DNS-over-QUIC body. The object
 * is compared and sent whole when any of its fields changed: every certificate field needs a
 * restart, so the note is the same as for the changed fields alone.
 */
function certificateBody(value: CertificateFormValue) {
  return {
    pfxPath: value.pfxPath,
    ...buildPfxPasswordPatchField(value.pfxPassword),
    selfSignedSubjectAlternativeNames: splitLines(value.selfSignedSubjectAlternativeNames),
    certificateReloadPollInterval: value.certificateReloadPollInterval,
    allowedProtocols: [
      ...(value.allowedProtocolTls12 ? ['Tls12'] : []),
      ...(value.allowedProtocolTls13 ? ['Tls13'] : []),
    ],
  };
}

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
 *
 * <p>The DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC and DNSCrypt cards send only the fields that
 * changed, because their controller actions pick the note by which fields a `PATCH` holds (the
 * `enable` switch applies at once, the rest needs a restart). Save stays disabled while nothing
 * changed. The Plain DNS card sends its whole form: every one of its fields needs a restart, so its
 * note never depends on the fields present.
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
    maxConcurrentConnections: [512, [Validators.required, Validators.min(1)]],
    pfxPath: [''],
    pfxPassword: [''],
    selfSignedSubjectAlternativeNames: ['localhost'],
    certificateReloadPollInterval: ['00:05:00', [Validators.required]],
    allowedProtocolTls12: [false],
    allowedProtocolTls13: [false],
  });

  readonly dnsCryptForm = this.formBuilder.group({
    enable: [false],
    port: [5443, [Validators.required, Validators.min(1), Validators.max(65535)]],
    listenAddresses: [''],
    providerName: [''],
    tcpIdleTimeout: ['00:00:30', [Validators.required]],
    maxConcurrentTcpConnections: [512, [Validators.required, Validators.min(1)]],
    esVersionXChaCha20Poly1305: [true],
    esVersionXSalsa20Poly1305: [true],
    certificateRotationInterval: ['1.00:00:00', [Validators.required]],
    certificateValidityDuration: ['1.04:00:00', [Validators.required]],
    certificateClockSkewTolerance: ['00:05:00', [Validators.required]],
  });

  private readonly dnsOverTlsTracker = trackChanges(this.dnsOverTlsForm, (value) =>
    this.dnsOverTlsBody(value),
  );
  private readonly dnsOverHttpsTracker = trackChanges(this.dnsOverHttpsForm, (value) => value);
  private readonly dnsOverQuicTracker = trackChanges(this.dnsOverQuicForm, (value) =>
    this.dnsOverQuicBody(value),
  );
  private readonly dnsCryptTracker = trackChanges(this.dnsCryptForm, (value) =>
    this.dnsCryptBody(value),
  );

  /** Whether a DNS-over-TLS field was edited since the card was loaded or saved. */
  readonly dnsOverTlsHasChanges = this.dnsOverTlsTracker.hasChanges;
  /** Whether a DNS-over-HTTPS field was edited since the card was loaded or saved. */
  readonly dnsOverHttpsHasChanges = this.dnsOverHttpsTracker.hasChanges;
  /** Whether a DNS-over-QUIC field was edited since the card was loaded or saved. */
  readonly dnsOverQuicHasChanges = this.dnsOverQuicTracker.hasChanges;
  /** Whether a DNSCrypt field was edited since the card was loaded or saved. */
  readonly dnsCryptHasChanges = this.dnsCryptTracker.hasChanges;

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

  /** This method PATCHes the changed DNS-over-TLS fields only. */
  async saveDnsOverTls(): Promise<void> {
    if (this.dnsOverTlsForm.invalid || this.dnsOverTlsSubmitting() || !this.dnsOverTlsHasChanges())
      return;

    this.dnsOverTlsSubmitting.set(true);
    this.dnsOverTlsErrorMessage.set(null);

    const saved = this.dnsOverTlsForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.transportApi.apiTransportSettingsDnsOverTlsPatch({
          body: this.dnsOverTlsTracker.changes(),
        }),
      );
      this.dnsOverTlsForm.patchValue({ pfxPassword: '' });
      // The password is write-only and cleared above, so the baseline never holds it.
      this.dnsOverTlsTracker.markClean({ ...saved, pfxPassword: '' });
      this.notification.success(result.note ?? 'DNS-over-TLS settings saved.');
    } catch (error) {
      this.dnsOverTlsErrorMessage.set(
        extractErrorMessage(error, 'Could not save DNS-over-TLS settings.'),
      );
    } finally {
      this.dnsOverTlsSubmitting.set(false);
    }
  }

  /** This method PATCHes the changed DNS-over-HTTPS fields only. */
  async saveDnsOverHttps(): Promise<void> {
    if (
      this.dnsOverHttpsForm.invalid ||
      this.dnsOverHttpsSubmitting() ||
      !this.dnsOverHttpsHasChanges()
    )
      return;

    this.dnsOverHttpsSubmitting.set(true);
    this.dnsOverHttpsErrorMessage.set(null);

    const saved = this.dnsOverHttpsForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.transportApi.apiTransportSettingsDnsOverHttpsPatch({
          body: this.dnsOverHttpsTracker.changes(),
        }),
      );
      this.dnsOverHttpsTracker.markClean(saved);
      this.notification.success(result.note ?? 'DNS-over-HTTPS settings saved.');
    } catch (error) {
      this.dnsOverHttpsErrorMessage.set(
        extractErrorMessage(error, 'Could not save DNS-over-HTTPS settings.'),
      );
    } finally {
      this.dnsOverHttpsSubmitting.set(false);
    }
  }

  /** This method PATCHes the changed DNS-over-QUIC fields only. */
  async saveDnsOverQuic(): Promise<void> {
    if (
      this.dnsOverQuicForm.invalid ||
      this.dnsOverQuicSubmitting() ||
      !this.dnsOverQuicHasChanges()
    )
      return;

    this.dnsOverQuicSubmitting.set(true);
    this.dnsOverQuicErrorMessage.set(null);

    const saved = this.dnsOverQuicForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.transportApi.apiTransportSettingsDnsOverQuicPatch({
          body: this.dnsOverQuicTracker.changes(),
        }),
      );
      this.dnsOverQuicForm.patchValue({ pfxPassword: '' });
      this.dnsOverQuicTracker.markClean({ ...saved, pfxPassword: '' });
      this.notification.success(result.note ?? 'DNS-over-QUIC settings saved.');
    } catch (error) {
      this.dnsOverQuicErrorMessage.set(
        extractErrorMessage(error, 'Could not save DNS-over-QUIC settings.'),
      );
    } finally {
      this.dnsOverQuicSubmitting.set(false);
    }
  }

  /** This method PATCHes the changed DNSCrypt fields only. */
  async saveDnsCrypt(): Promise<void> {
    if (this.dnsCryptForm.invalid || this.dnsCryptSubmitting() || !this.dnsCryptHasChanges())
      return;

    this.dnsCryptSubmitting.set(true);
    this.dnsCryptErrorMessage.set(null);

    const saved = this.dnsCryptForm.getRawValue();

    try {
      const result = await firstValueFrom(
        this.transportApi.apiTransportSettingsDnscryptPatch({
          body: this.dnsCryptTracker.changes(),
        }),
      );
      this.dnsCryptTracker.markClean(saved);
      this.notification.success(result.note ?? 'DNSCrypt settings saved.');
    } catch (error) {
      this.dnsCryptErrorMessage.set(
        extractErrorMessage(error, 'Could not save DNSCrypt settings.'),
      );
    } finally {
      this.dnsCryptSubmitting.set(false);
    }
  }

  /** This method turns the DNS-over-TLS form value into the request-shaped body. */
  private dnsOverTlsBody(value: ReturnType<typeof this.dnsOverTlsForm.getRawValue>) {
    return {
      enable: value.enable,
      port: value.port,
      listenAddresses: splitListenAddresses(value.listenAddresses),
      idleTimeout: value.idleTimeout,
      maxConcurrentConnections: value.maxConcurrentConnections,
      certificate: certificateBody(value),
    };
  }

  /** This method turns the DNS-over-QUIC form value into the request-shaped body. */
  private dnsOverQuicBody(value: ReturnType<typeof this.dnsOverQuicForm.getRawValue>) {
    return {
      enable: value.enable,
      port: value.port,
      listenAddresses: splitListenAddresses(value.listenAddresses),
      idleTimeout: value.idleTimeout,
      maxConcurrentBidirectionalStreams: value.maxConcurrentBidirectionalStreams,
      maxConcurrentConnections: value.maxConcurrentConnections,
      certificate: certificateBody(value),
    };
  }

  /** This method turns the DNSCrypt form value into the request-shaped body. */
  private dnsCryptBody(value: ReturnType<typeof this.dnsCryptForm.getRawValue>) {
    return {
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
    };
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
      this.dnsOverTlsTracker.markClean(this.dnsOverTlsForm.getRawValue());

      this.dnsOverHttpsForm.patchValue({
        enable: dnsOverHttps.enable ?? false,
        path: dnsOverHttps.path ?? '/dns-query',
        maxMessageSizeBytes: dnsOverHttps.maxMessageSizeBytes ?? 65535,
      });
      this.dnsOverHttpsTracker.markClean(this.dnsOverHttpsForm.getRawValue());

      this.dnsOverQuicPfxPasswordConfigured.set(
        dnsOverQuic.certificate?.pfxPasswordConfigured ?? false,
      );
      this.dnsOverQuicForm.patchValue({
        enable: dnsOverQuic.enable ?? false,
        port: dnsOverQuic.port ?? 853,
        listenAddresses: joinLines(dnsOverQuic.listenAddresses),
        idleTimeout: dnsOverQuic.idleTimeout ?? '00:00:30',
        maxConcurrentBidirectionalStreams: dnsOverQuic.maxConcurrentBidirectionalStreams ?? 100,
        maxConcurrentConnections: dnsOverQuic.maxConcurrentConnections ?? 512,
        pfxPath: dnsOverQuic.certificate?.pfxPath ?? '',
        selfSignedSubjectAlternativeNames: joinLines(
          dnsOverQuic.certificate?.selfSignedSubjectAlternativeNames,
        ),
        certificateReloadPollInterval:
          dnsOverQuic.certificate?.certificateReloadPollInterval ?? '00:05:00',
        allowedProtocolTls12: (dnsOverQuic.certificate?.allowedProtocols ?? []).includes('Tls12'),
        allowedProtocolTls13: (dnsOverQuic.certificate?.allowedProtocols ?? []).includes('Tls13'),
      });
      this.dnsOverQuicTracker.markClean(this.dnsOverQuicForm.getRawValue());

      this.dnsCryptForm.patchValue({
        enable: dnsCrypt.enable ?? false,
        port: dnsCrypt.port ?? 5443,
        listenAddresses: joinLines(dnsCrypt.listenAddresses),
        providerName: dnsCrypt.providerName ?? '',
        tcpIdleTimeout: dnsCrypt.tcpIdleTimeout ?? '00:00:30',
        maxConcurrentTcpConnections: dnsCrypt.maxConcurrentTcpConnections ?? 512,
        esVersionXChaCha20Poly1305: (dnsCrypt.enabledEsVersions ?? []).includes(
          'XChaCha20Poly1305',
        ),
        esVersionXSalsa20Poly1305: (dnsCrypt.enabledEsVersions ?? []).includes('XSalsa20Poly1305'),
        certificateRotationInterval: dnsCrypt.certificateRotationInterval ?? '1.00:00:00',
        certificateValidityDuration: dnsCrypt.certificateValidityDuration ?? '1.04:00:00',
        certificateClockSkewTolerance: dnsCrypt.certificateClockSkewTolerance ?? '00:05:00',
      });
      this.dnsCryptTracker.markClean(this.dnsCryptForm.getRawValue());
    } catch (error) {
      this.loadErrorMessage.set(extractErrorMessage(error, 'Could not load transport settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

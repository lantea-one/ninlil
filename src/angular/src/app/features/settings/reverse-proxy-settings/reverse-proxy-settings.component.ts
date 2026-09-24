import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
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
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import { ReverseProxySettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClusterSyncInfoComponent } from '../../../shared/cluster-sync-info/cluster-sync-info.component';
import { buildSharedSecretPatchField, joinLines, splitLines } from './reverse-proxy-settings.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component reads and writes this host's own `ReverseProxyOptions` -- whether the shared
 * Kestrel HTTP/HTTPS listener trusts a reverse proxy's own `X-Forwarded-For`/`X-Forwarded-Proto`
 * headers, via `GET`/`PATCH /api/reverse-proxy-settings`. `knownProxies`/`knownNetworks` are each
 * edited as a one-entry-per-line textarea (see `splitLines`/`joinLines`), the same convention
 * `ZoneFormComponent`'s own `primaryServerAddresses`/`forwarderAddresses` fields already
 * establish, rather than a raw JSON array a caller would otherwise have to hand-author.
 * `sharedSecret` follows `EventBrokerSettingsComponent`'s own broker `password` field exactly:
 * `ReverseProxySettingsController.Get` never returns it at all, only whether one is currently
 * configured (`sharedSecretConfigured`), so this form can never pre-fill it, and leaving it blank
 * on submit must mean "leave unchanged," never "clear it" -- see
 * `buildSharedSecretPatchField`'s own doc comment for the full reasoning. Every successful
 * `PATCH`'s own "restart required" note is surfaced directly in the success notification, never
 * suppressed.
 *
 * A second, separate section of this same form edits DNS-over-TLS's own PROXY protocol trust
 * configuration (`dnsOverTlsEnable`/`dnsOverTlsKnownProxiesText`/`dnsOverTlsKnownNetworksText`,
 * always sent together as one `dnsOverTls` sub-object) -- an entirely different allowlist from
 * the HTTP one above, since a raw TCP-level proxy in front of DNS-over-TLS is not guaranteed to
 * be the same upstream as an HTTP-mode reverse proxy, even though it very often is. It has no
 * shared-secret field of its own at all: the PROXY protocol's own fixed v1/v2 header framing has
 * no natural place to carry one, so the IP/CIDR allowlist alone is that section's entire trust
 * boundary.
 */
@Component({
  selector: 'app-reverse-proxy-settings',
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
    IconDirective,
    SpinnerComponent,
    ClusterSyncInfoComponent,
  ],
  templateUrl: './reverse-proxy-settings.component.html',
  styleUrl: './reverse-proxy-settings.component.scss',
})
export class ReverseProxySettingsComponent implements OnInit {
  private readonly reverseProxyApi = inject(ReverseProxySettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether a shared secret is currently configured -- read directly from the backend, never guessed from this form's own blank secret input. */
  readonly sharedSecretConfigured = signal(false);

  /** When this settings section was last written and by which cluster node, per the cluster's own config-sync mechanism. */
  readonly lastModifiedUtc = signal<string | null | undefined>(null);
  readonly originatingNodeId = signal<string | null | undefined>(null);

  readonly form = this.formBuilder.group({
    enable: [false],
    knownProxiesText: [''],
    knownNetworksText: [''],
    sharedSecretHeaderName: [''],
    sharedSecret: [''],
    dnsOverTlsEnable: [false],
    dnsOverTlsKnownProxiesText: [''],
    dnsOverTlsKnownNetworksText: [''],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /**
   * This method PATCHes every field this form holds, omitting `sharedSecret` entirely when its
   * own input was left blank so the stored value is left untouched rather than cleared.
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const body = {
      enable: value.enable,
      knownProxies: splitLines(value.knownProxiesText),
      knownNetworks: splitLines(value.knownNetworksText),
      sharedSecretHeaderName: value.sharedSecretHeaderName,
      ...buildSharedSecretPatchField(value.sharedSecret),
      dnsOverTls: {
        enable: value.dnsOverTlsEnable,
        knownProxies: splitLines(value.dnsOverTlsKnownProxiesText),
        knownNetworks: splitLines(value.dnsOverTlsKnownNetworksText),
      },
    };

    try {
      const result = await firstValueFrom(
        this.reverseProxyApi.apiReverseProxySettingsPatch({ body }),
      );
      this.notification.success(result.note ?? 'Reverse-proxy settings saved.');
      // Never re-populate the secret field from the response -- it is never returned, so
      // clearing it back to blank keeps this form's own "blank means unchanged" contract honest.
      this.form.patchValue({ sharedSecret: '' });
      await this.loadSettings();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save reverse-proxy settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.reverseProxyApi.apiReverseProxySettingsGet());
      this.sharedSecretConfigured.set(settings.sharedSecretConfigured ?? false);
      this.form.patchValue({
        enable: settings.enable ?? false,
        knownProxiesText: joinLines(settings.knownProxies),
        knownNetworksText: joinLines(settings.knownNetworks),
        sharedSecretHeaderName: settings.sharedSecretHeaderName ?? '',
        dnsOverTlsEnable: settings.dnsOverTls?.enable ?? false,
        dnsOverTlsKnownProxiesText: joinLines(settings.dnsOverTls?.knownProxies),
        dnsOverTlsKnownNetworksText: joinLines(settings.dnsOverTls?.knownNetworks),
      });
      this.lastModifiedUtc.set(settings.lastModifiedUtc);
      this.originatingNodeId.set(settings.originatingNodeId);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load reverse-proxy settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

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
 * This component is the "Reverse proxy" settings page (`/settings/reverse-proxy`). It edits
 * `ReverseProxyOptions` through `GET` and `PATCH /api/reverse-proxy-settings` (`reverseproxy:read`,
 * `reverseproxy:write`): whether the web listener trusts a reverse proxy's `X-Forwarded-For` and
 * `X-Forwarded-Proto` headers, and from which proxies and networks (one per line, see
 * `splitLines`). The shared secret is never returned, only whether one is set
 * (`sharedSecretConfigured`), so a blank secret field means "keep the stored one" (see
 * `buildSharedSecretPatchField`).
 *
 * <p>A second section edits the PROXY protocol allowlist for DNS-over-TLS, sent as one `dnsOverTls`
 * object. It is separate because a TCP proxy in front of DNS-over-TLS need not be the HTTP proxy.
 * It has no shared secret, since the PROXY protocol header has no standard field for one; the
 * address allowlist is the whole trust boundary.
 *
 * <p>Changes need a restart; the backend's note in the success notification says so.
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

  /** Whether a shared secret is configured, as the backend reports (the secret field is always blank). */
  readonly sharedSecretConfigured = signal(false);

  /** When this section was last written, from the cluster config sync; {@link originatingNodeId} names the node. */
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
   * This method PATCHes every field, leaving out a blank `sharedSecret` so the stored one is kept,
   * then reloads.
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
      // The secret is never returned; clearing the field keeps "blank means unchanged" true.
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

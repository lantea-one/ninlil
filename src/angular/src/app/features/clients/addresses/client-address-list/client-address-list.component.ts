import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  BadgeComponent,
  ButtonDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  FormTextDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  ClientAddressDetail,
  ClientAddressesService,
  ClientsService,
  InternetProtocolVersionEnum,
} from '../../../../api-client';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../../shared/confirm-dialog/confirm-dialog.component';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { formatRelativeTime } from '../../shared/relative-time.util';

/**
 * The most identifiers one device realistically answers to; the whole set is fetched as one page,
 * since a device's addresses are a handful, not a paged collection.
 */
const MAX_ADDRESSES = 100;

/**
 * This component manages one known device's own identifiers (`/api/clients/{clientId}/addresses`):
 * every MAC address or DUID the device answers to, each with the IP address it was last seen at,
 * the MAC it embeds and that MAC's hardware vendor, and when it was first and last seen.
 *
 * <p>An identifier is immutable once added -- it is the key every identity lookup and DHCP
 * auto-population decision matches on -- so this page offers only "add" and "remove", never edit,
 * mirroring `ClientAddressesController` exactly. Adding one links a further identity to this same
 * device (for example the DHCPv6 DUID of a dual-stack host whose DHCPv4 MAC is already here), so
 * both resolve to one device for filtering, schedules, and activity. The server refuses to remove
 * a device's last identifier (a device with none could never be resolved), and refuses an
 * identifier some device already answers to; either refusal is shown as-is.
 *
 * <p>The add form and every remove action appear only for a session holding `clients:write`.
 */
@Component({
  selector: 'app-client-address-list',
  imports: [
    RouterLink,
    DatePipe,
    ReactiveFormsModule,
    CdkTableModule,
    BadgeComponent,
    ButtonDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    FormTextDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    ConfirmDialogComponent,
  ],
  templateUrl: './client-address-list.component.html',
  styleUrl: './client-address-list.component.scss',
})
export class ClientAddressListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly addressesApi = inject(ClientAddressesService);
  private readonly clientsApi = inject(ClientsService);
  private readonly authService = inject(AuthService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** Exposes the enum to this component's own template, for the protocol select and badges. */
  readonly InternetProtocolVersionEnum = InternetProtocolVersionEnum;

  /** Whether this session may add or remove identifiers. */
  readonly canWrite = this.authService.hasPermission('clients:write');

  readonly clientId = this.route.snapshot.paramMap.get('clientId') ?? '';

  readonly displayedColumns = ['protocol', 'identifier', 'address', 'vendor', 'seen', 'actions'];

  readonly clientLabel = signal<string | null>(null);
  readonly addresses = signal<ClientAddressDetail[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether {@link ConfirmDialogComponent} is currently shown for a pending removal. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /** The add-identifier form: the identifier itself and which protocol's identifier space it belongs to. */
  readonly form = this.formBuilder.group({
    clientIdentifier: ['', [Validators.required, Validators.maxLength(256)]],
    protocolVersion: [InternetProtocolVersionEnum.Ipv4 as InternetProtocolVersionEnum],
  });

  constructor() {
    void this.loadClientLabel();
    void this.loadAddresses();
  }

  /** This method renders a sighting as "5 min ago", or `null` for one never recorded. */
  relative(instant: string | null | undefined): string | null {
    return formatRelativeTime(instant);
  }

  /** This method adds the form's identifier to this device, then clears the form. */
  async addAddress(): Promise<void> {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    try {
      const { clientIdentifier, protocolVersion } = this.form.getRawValue();
      await firstValueFrom(
        this.addressesApi.apiClientsClientIdAddressesPost({
          clientId: this.clientId,
          clientAddressWriteRequest: {
            clientIdentifier: clientIdentifier.trim(),
            protocolVersion,
          },
        }),
      );
      this.notification.success('Identifier added.');
      this.form.reset();
      await this.loadAddresses();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not add this identifier.'));
    } finally {
      this.saving.set(false);
    }
  }

  /** This method confirms, then removes, one identifier from this device. */
  async removeAddress(address: ClientAddressDetail): Promise<void> {
    if (!address.id) return;

    const confirmed = await this.confirm({
      title: `Remove ${address.clientIdentifier}?`,
      message:
        'This device will no longer be recognized by this identifier. A later DHCP lease for it would register it as a separate new device.',
    });
    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.addressesApi.apiClientsClientIdAddressesIdDelete({
          clientId: this.clientId,
          id: address.id,
        }),
      );
      this.notification.success('Identifier removed.');
      await this.loadAddresses();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not remove this identifier.'));
    }
  }

  /** This method shows {@link ConfirmDialogComponent} and resolves once the operator confirms or dismisses it. */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private async loadClientLabel(): Promise<void> {
    try {
      const client = await firstValueFrom(this.clientsApi.apiClientsIdGet({ id: this.clientId }));
      this.clientLabel.set(client.displayName || client.addresses?.[0]?.clientIdentifier || null);
    } catch {
      // The heading simply falls back to a generic title; the address list reports its own errors.
      this.clientLabel.set(null);
    }
  }

  private async loadAddresses(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.addressesApi.apiClientsClientIdAddressesGet({
          clientId: this.clientId,
          skip: 0,
          take: MAX_ADDRESSES,
        }),
      );
      this.addresses.set(page.items ?? []);
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load this device’s identifiers.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}

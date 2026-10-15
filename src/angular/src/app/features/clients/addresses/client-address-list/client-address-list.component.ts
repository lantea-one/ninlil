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
import {
  CLIENT_IDENTIFIER_ERROR,
  CLIENT_IDENTIFIER_HINT,
  clientIdentifierValidator,
} from '../../../../shared/validators/client-identifier.validator';
import { formatRelativeTime } from '../../shared/relative-time.util';

/**
 * The most identifiers fetched for one device. A device has a handful, so the page loads them all
 * in one request instead of paging.
 */
const MAX_ADDRESSES = 100;

/**
 * This component is a client's identifiers page (`/clients/:clientId/addresses`,
 * `clients:read`). It lists every MAC address or DUID the device answers to, with the IP address
 * it was last seen at, the MAC it embeds and that MAC's vendor, and when it was first and last
 * seen (`GET /api/clients/{clientId}/addresses`).
 *
 * <p>Identity lookups and DHCP auto-registration match on the identifier, so it cannot be edited;
 * the page only adds (`POST`) and removes (`DELETE .../addresses/{id}`). Adding one links another
 * identity to the same device, such as the DHCPv6 DUID of a dual-stack host whose DHCPv4 MAC is
 * already here, so both count as one device for filtering, schedules and activity. The backend
 * answers 409 for an identifier another device already has, shown as a toast. Removing the last
 * identifier is refused too, so its remove button is disabled.
 *
 * <p>The add form and remove buttons appear only with `clients:write`.
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

  /** The enum, exposed to the template for the protocol select and badges. */
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

  /** Whether the remove confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /** The hint under the identifier field: the accepted forms and the stored form. */
  readonly clientIdentifierHint = CLIENT_IDENTIFIER_HINT;

  /** The message shown when the identifier is not in an accepted form. */
  readonly clientIdentifierError = CLIENT_IDENTIFIER_ERROR;

  /** The add form: the identifier and the protocol (IPv4 MAC or IPv6 DUID) it belongs to. */
  readonly form = this.formBuilder.group({
    clientIdentifier: ['', [Validators.required, clientIdentifierValidator()]],
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

  /** This method asks for confirmation, then removes an identifier from the device. */
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

  /** This method hides the confirmation dialog and settles the pending {@link confirm} promise. */
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
      // The heading falls back to a generic title; loadAddresses() reports errors.
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

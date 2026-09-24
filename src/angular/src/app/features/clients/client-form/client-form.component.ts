import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
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
import {
  ClientGroupDetail,
  ClientGroupsService,
  ClientsService,
  DnsClientDeviceIconEnum,
  InternetProtocolVersionEnum,
} from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { InstantPauseComponent } from '../../../shared/instant-pause/instant-pause.component';
import { NotificationService } from '../../../shared/notification/notification.service';
import { BlockedServicesComponent } from '../../settings/blocked-services/blocked-services.component';
import { SafeSearchComponent } from '../../settings/safe-search/safe-search.component';
import { DeviceIconPickerComponent } from '../device-icon-picker/device-icon-picker.component';

/**
 * This component is both the "register a client" and "edit a client" page -- one component for
 * both, distinguished by whether the route supplies a `clientId` (see `app.routes.ts`).
 *
 * <p><b>`clientIdentifier`/`protocolVersion` are locked once a client is registered.</b> Together
 * they are the device's own first address -- the pair `ClientsController` looks a device up by,
 * and the pair the DHCP-driven auto-population feature uses to decide whether it has seen a
 * device before. Both fields are shown, but disabled, in edit mode (reflecting the device's own
 * first-registered address, `addresses[0]`): showing them (rather than hiding them entirely) keeps
 * the identity visible for reference while making unmistakably clear neither can be changed here
 * -- see `ClientsController`'s own doc comment for the full reasoning. A device with more than one
 * address still shows only its first one here; the form links to `ClientAddressListComponent`
 * (`/clients/{id}/addresses`), where the device's full identifier set is added to and removed from.
 *
 * <p>`clientGroupId` is an optional dropdown, populated from a one-time client-groups fetch, the
 * same "optional dropdown resolved from a one-time fetch" pattern `DhcpScopeFormComponent`'s own
 * `dnsZoneId` dropdown already establishes.
 *
 * <p>`deviceIconKey`/`isFavorite` are the device's own presentation metadata: an explicit icon
 * choice (via `DeviceIconPickerComponent`, `null` meaning "Automatic") and a favorite star. In edit
 * mode this form also shows the device's read-time MAC vendor (or that its MAC is randomized) and
 * passes the backend's vendor-based icon suggestion to the picker, so leaving the icon on
 * "Automatic" visibly means that suggestion. Neither the vendor nor the suggestion is ever sent
 * back -- both are computed by the backend on every read.
 */
@Component({
  selector: 'app-client-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
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
    IconDirective,
    BlockedServicesComponent,
    SafeSearchComponent,
    InstantPauseComponent,
    DeviceIconPickerComponent,
  ],
  templateUrl: './client-form.component.html',
  styleUrl: './client-form.component.scss',
})
export class ClientFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly clientsApi = inject(ClientsService);
  private readonly clientGroupsApi = inject(ClientGroupsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** The route's own `clientId`, or `null` when registering a new client -- also the id `BlockedServicesComponent`/`SafeSearchComponent` scope their own panel to below. */
  readonly clientId = this.route.snapshot.paramMap.get('clientId');

  /** Whether this instance is editing an existing client rather than registering a new one. */
  readonly isEditMode = this.clientId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** The backend's vendor-based icon suggestion for this device, shown on the picker's "Automatic" tile. */
  readonly suggestedDeviceIconKey = signal<DnsClientDeviceIconEnum | null>(null);

  /** This device's hardware vendor, resolved by the backend at read time, or `null` when unknown. */
  readonly vendor = signal<string | null>(null);

  /** Whether every MAC address this device presents is randomized, so no vendor can be known. */
  readonly isRandomizedMacAddress = signal(false);

  /** How many identifiers this device answers to, for the edit form's link to its addresses page. */
  readonly addressCount = signal(0);

  /** Every client group this client's own optional `clientGroupId` can designate. */
  readonly clientGroups = signal<ClientGroupDetail[]>([]);

  readonly form = this.formBuilder.group({
    clientIdentifier: ['', [Validators.required]],
    protocolVersion: this.formBuilder.control<InternetProtocolVersionEnum>(
      InternetProtocolVersionEnum.Ipv4,
      [Validators.required],
    ),
    displayName: [''],
    clientGroupId: [''],
    isDisabled: [false],
    comments: [''],
    deviceIconKey: this.formBuilder.control<DnsClientDeviceIconEnum | null>(null),
    isFavorite: [false],
  });

  ngOnInit(): void {
    void this.loadClientGroups();

    if (this.isEditMode) {
      // clientIdentifier/protocolVersion are this row's own entire identity and can never change
      // once it exists -- see this class's own doc comment for why.
      this.form.controls.clientIdentifier.disable();
      this.form.controls.protocolVersion.disable();
      void this.loadExistingClient();
    }
  }

  /** This method registers (`POST`) or fully replaces the editable fields of (`PUT`) the client, per {@link isEditMode}. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      if (this.isEditMode && this.clientId) {
        // ClientWriteRequest deliberately carries no clientIdentifier/protocolVersion at all --
        // see ClientsController's own doc comment.
        await firstValueFrom(
          this.clientsApi.apiClientsIdPut({
            id: this.clientId,
            clientWriteRequest: {
              displayName: value.displayName || null,
              clientGroupId: value.clientGroupId || null,
              isDisabled: value.isDisabled,
              comments: value.comments || null,
              deviceIconKey: value.deviceIconKey,
              isFavorite: value.isFavorite,
            },
          }),
        );
        this.notification.success('Client saved.');
      } else {
        await firstValueFrom(
          this.clientsApi.apiClientsPost({
            clientCreateRequest: {
              clientIdentifier: value.clientIdentifier,
              protocolVersion: value.protocolVersion,
              displayName: value.displayName || null,
              clientGroupId: value.clientGroupId || null,
              isDisabled: value.isDisabled,
              comments: value.comments || null,
              deviceIconKey: value.deviceIconKey,
              isFavorite: value.isFavorite,
            },
          }),
        );
        this.notification.success('Client registered.');
      }

      await this.router.navigate(['/clients']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this client.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadClientGroups(): Promise<void> {
    try {
      const page = await firstValueFrom(
        this.clientGroupsApi.apiClientGroupsGet({
          skip: 0,
          take: 500,
        }),
      );
      this.clientGroups.set(page.items ?? []);
    } catch {
      // A failed group lookup only narrows this form's own optional group dropdown -- the rest
      // of the form remains usable, and a genuinely invalid clientGroupId is still rejected
      // server-side.
    }
  }

  private async loadExistingClient(): Promise<void> {
    if (!this.clientId) return;

    try {
      const client = await firstValueFrom(this.clientsApi.apiClientsIdGet({ id: this.clientId }));

      this.form.patchValue({
        clientIdentifier: client.addresses?.[0]?.clientIdentifier ?? '',
        protocolVersion: client.addresses?.[0]?.protocolVersion ?? InternetProtocolVersionEnum.Ipv4,
        displayName: client.displayName ?? '',
        clientGroupId: client.clientGroupId ?? '',
        isDisabled: client.isDisabled ?? false,
        comments: client.comments ?? '',
        deviceIconKey: client.deviceIconKey ?? null,
        isFavorite: client.isFavorite ?? false,
      });
      this.suggestedDeviceIconKey.set(client.suggestedDeviceIconKey ?? null);
      this.vendor.set(client.vendor ?? null);
      this.isRandomizedMacAddress.set(client.isRandomizedMacAddress ?? false);
      this.addressCount.set(client.addresses?.length ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this client.'));
    } finally {
      this.loading.set(false);
    }
  }
}

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
import {
  CLIENT_IDENTIFIER_ERROR,
  CLIENT_IDENTIFIER_HINT,
  clientIdentifierValidator,
} from '../../../shared/validators/client-identifier.validator';
import { BlockedServicesComponent } from '../../settings/blocked-services/blocked-services.component';
import { SafeSearchComponent } from '../../settings/safe-search/safe-search.component';
import { DeviceIconPickerComponent } from '../device-icon-picker/device-icon-picker.component';

/**
 * This component is the register and edit page for a client (`/clients/new` and
 * `/clients/:clientId/edit`, `clients:write`); edit mode is chosen by the `clientId` route
 * parameter. It saves with `POST /api/clients` or `PUT /api/clients/{id}`. In edit mode it also
 * shows the instant pause, Blocked Services and Safe Search panels for the client.
 *
 * <p><b>The identifier and protocol cannot change after registration.</b> They are the device's
 * first address, which device lookups and DHCP auto-registration match on, so the `PUT` request
 * has no field for them. In edit mode both are shown disabled, from `addresses[0]`. A device with
 * several identifiers shows only the first here; the form links to the identifiers page
 * (`ClientAddressListComponent`) for the full set.
 *
 * <p>The client group is an optional select filled from `GET /api/client-groups`. The icon
 * (`DeviceIconPickerComponent`, `null` for "Automatic") and the favorite star are display
 * settings. In edit mode the form shows the device's MAC vendor (or that its MAC is randomized)
 * and passes the backend's vendor-based icon suggestion to the picker. The backend computes
 * vendor and suggestion on every read, so neither is sent back.
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

  /** The `clientId` route parameter, or `null` when registering; the edit-mode panels use it too. */
  readonly clientId = this.route.snapshot.paramMap.get('clientId');

  /** Whether this instance is editing an existing client rather than registering a new one. */
  readonly isEditMode = this.clientId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** The backend's vendor-based icon suggestion for this device, shown on the picker's "Automatic" tile. */
  readonly suggestedDeviceIconKey = signal<DnsClientDeviceIconEnum | null>(null);

  /** The device's hardware vendor, looked up by the backend on read, or `null` when unknown. */
  readonly vendor = signal<string | null>(null);

  /** Whether every MAC address this device presents is randomized, so no vendor can be known. */
  readonly isRandomizedMacAddress = signal(false);

  /** How many identifiers the device has, for the link to its identifiers page. */
  readonly addressCount = signal(0);

  /** The client groups offered in the group select (up to 500). */
  readonly clientGroups = signal<ClientGroupDetail[]>([]);

  /** The hint under the identifier field: the accepted forms and the stored form. */
  readonly clientIdentifierHint = CLIENT_IDENTIFIER_HINT;

  /** The message shown when the identifier is not in an accepted form. */
  readonly clientIdentifierError = CLIENT_IDENTIFIER_ERROR;

  readonly form = this.formBuilder.group({
    clientIdentifier: ['', [Validators.required, clientIdentifierValidator()]],
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
      // The identifier and protocol cannot change after registration; see the class comment.
      this.form.controls.clientIdentifier.disable();
      this.form.controls.protocolVersion.disable();
      void this.loadExistingClient();
    }
  }

  /** This method registers the client (`POST`) or replaces its editable fields (`PUT`), then returns to the list. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      if (this.isEditMode && this.clientId) {
        // ClientWriteRequest has no identifier or protocol; they are fixed at registration.
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
      // Without the groups (e.g. no clientgroups:read) the select is empty but the rest of the
      // form works, and the loaded group id is kept; the backend rejects an unknown id.
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

import { Component, OnInit, computed, inject, signal } from '@angular/core';
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
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { ClientGroupNetworksService } from '../../../../api-client';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';

/** The two kinds of match a group network row can be; a row is one or the other. */
type NetworkMatchKind = 'cidr' | 'clientId';

/**
 * This component is the add and edit page for a client group's network
 * (`/client-groups/:groupId/networks/new` and `.../networks/:networkId/edit`,
 * `clientgroups:write`); edit mode is chosen by the `networkId` route parameter. It saves with
 * `POST` or `PUT /api/client-groups/{groupId}/networks/{id}`.
 *
 * <p>A row matches clients either by source address (a CIDR) or by the ClientID a DNS-over-HTTPS
 * client sends in the `/dns-query/{clientid}` path, never both. The form has a kind select and
 * shows only the field that kind needs. Fields are only checked as required here; the backend
 * parses the CIDR (`IPNetwork.TryParse`) and a ClientID is free text.
 */
@Component({
  selector: 'app-client-group-network-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    SpinnerComponent,
  ],
  templateUrl: './client-group-network-form.component.html',
  styleUrl: './client-group-network-form.component.scss',
})
export class ClientGroupNetworkFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly networksApi = inject(ClientGroupNetworksService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly groupId = this.route.snapshot.paramMap.get('groupId') ?? '';
  private readonly networkId = this.route.snapshot.paramMap.get('networkId');

  /** Whether this instance is editing an existing network rather than adding a new one. */
  readonly isEditMode = this.networkId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    matchKind: ['cidr' as NetworkMatchKind, [Validators.required]],
    cidr: ['', [Validators.required]],
    clientId: [''],
  });

  /** The selected match kind as a signal (the form's `valueChanges` is not one), which decides the field shown. */
  readonly matchKind = signal<NetworkMatchKind>('cidr');
  readonly showCidrField = computed(() => this.matchKind() === 'cidr');
  readonly showClientIdField = computed(() => this.matchKind() === 'clientId');

  constructor() {
    // Switching kind clears the unused field and makes the other one required, so save() sends
    // exactly one and no stale text is left behind.
    this.form.controls.matchKind.valueChanges.subscribe((kind) => {
      this.matchKind.set(kind);

      if (kind === 'cidr') {
        this.form.controls.cidr.setValidators([Validators.required]);
        this.form.controls.clientId.clearValidators();
        this.form.controls.clientId.setValue('');
      } else {
        this.form.controls.clientId.setValidators([Validators.required]);
        this.form.controls.cidr.clearValidators();
        this.form.controls.cidr.setValue('');
      }

      this.form.controls.cidr.updateValueAndValidity();
      this.form.controls.clientId.updateValueAndValidity();
    });
  }

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingNetwork();
  }

  /** This method adds the network (`POST`) or replaces it (`PUT`), then returns to the network list. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const request =
      value.matchKind === 'cidr' ? { cidr: value.cidr } : { clientId: value.clientId };

    try {
      if (this.isEditMode && this.networkId) {
        await firstValueFrom(
          this.networksApi.apiClientGroupsGroupIdNetworksIdPut({
            groupId: this.groupId,
            id: this.networkId,
            clientGroupNetworkWriteRequest: request,
          }),
        );
        this.notification.success('Network saved.');
      } else {
        await firstValueFrom(
          this.networksApi.apiClientGroupsGroupIdNetworksPost({
            groupId: this.groupId,
            clientGroupNetworkWriteRequest: request,
          }),
        );
        this.notification.success('Network added.');
      }

      await this.router.navigate(['/client-groups', this.groupId, 'networks']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this network.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadExistingNetwork(): Promise<void> {
    if (!this.networkId) return;

    try {
      const network = await firstValueFrom(
        this.networksApi.apiClientGroupsGroupIdNetworksIdGet({
          groupId: this.groupId,
          id: this.networkId,
        }),
      );
      const kind: NetworkMatchKind = network.clientId ? 'clientId' : 'cidr';
      this.form.patchValue({
        matchKind: kind,
        cidr: network.cidr ?? '',
        clientId: network.clientId ?? '',
      });
      this.matchKind.set(kind);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this network.'));
    } finally {
      this.loading.set(false);
    }
  }
}

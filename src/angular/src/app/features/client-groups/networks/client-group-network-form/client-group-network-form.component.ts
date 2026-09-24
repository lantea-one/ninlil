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

/** The two mutually-exclusive shapes a `DnsClientGroupNetworkEntity` row can take. */
type NetworkMatchKind = 'cidr' | 'clientId';

/**
 * This component is both the "add a network" and "edit a network" page for a client group -- one
 * component for both, distinguished by whether the route supplies a `networkId` (see
 * `app.routes.ts`). A row matches a client either by CIDR (its own source address) or by a
 * DNS-over-HTTPS ClientID label -- exactly one of the two, per
 * `DnsClientGroupNetworkEntity`'s own doc comment -- so this form is a mode select plus whichever
 * single field that mode needs, rather than two always-visible fields an operator could
 * (invalidly) fill in both at once. Neither field's own format is validated client-side beyond
 * "required": the CIDR is only validated by the real backend parser
 * (`System.Net.IPNetwork.TryParse`), and a ClientID is free text with no format of its own, so a
 * caller always sees the exact same acceptance rule `DnsFilterEngine` itself matches against at
 * query time.
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

  /** Which of {@link cidr}/`clientId`'s own fields is currently the active one, tracked as a plain signal since `valueChanges` isn't one. */
  readonly matchKind = signal<NetworkMatchKind>('cidr');
  readonly showCidrField = computed(() => this.matchKind() === 'cidr');
  readonly showClientIdField = computed(() => this.matchKind() === 'clientId');

  constructor() {
    // Switching mode clears the field that mode no longer uses, and re-requires the one it does
    // -- so save() never accidentally submits both, or neither, and the disabled field never
    // shows stale leftover text from before the switch.
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

  /** This method adds (`POST`) or fully replaces (`PUT`) the network, per {@link isEditMode}. */
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

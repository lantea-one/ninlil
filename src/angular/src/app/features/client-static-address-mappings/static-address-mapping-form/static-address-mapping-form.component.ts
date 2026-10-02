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
  SpinnerComponent,
} from '@coreui/angular';
import {
  DnsClientStaticAddressMappingsService,
  InternetProtocolVersionEnum,
} from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component is the create and edit page for a static address mapping
 * (`/client-static-address-mappings/new` and `.../:id/edit`, `clients:write`); edit mode is chosen
 * by the `id` route parameter. A mapping tells the server that queries from an address or network
 * come from the client with a given identifier (MAC or DUID), for devices that never take a DHCP
 * lease from this server. It saves with `POST` or
 * `PUT /api/client-static-address-mappings/{id}`.
 */
@Component({
  selector: 'app-static-address-mapping-form',
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
    SpinnerComponent,
  ],
  templateUrl: './static-address-mapping-form.component.html',
  styleUrl: './static-address-mapping-form.component.scss',
})
export class StaticAddressMappingFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly mappingsApi = inject(DnsClientStaticAddressMappingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly mappingId = this.route.snapshot.paramMap.get('id');

  /** Whether this instance is editing an existing mapping rather than creating a new one. */
  readonly isEditMode = this.mappingId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    cidr: ['', [Validators.required]],
    clientIdentifier: ['', [Validators.required]],
    protocolVersion: [InternetProtocolVersionEnum.Ipv4 as InternetProtocolVersionEnum],
    isDisabled: [false],
    comments: [''],
  });

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingMapping();
  }

  /** This method creates the mapping (`POST`) or replaces it (`PUT`), then returns to the list. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const request = {
      cidr: value.cidr,
      clientIdentifier: value.clientIdentifier,
      protocolVersion: value.protocolVersion,
      isDisabled: value.isDisabled,
      comments: value.comments || null,
    };

    try {
      if (this.isEditMode && this.mappingId) {
        await firstValueFrom(
          this.mappingsApi.apiClientStaticAddressMappingsIdPut({
            id: this.mappingId,
            dnsClientStaticAddressMappingWriteRequest: request,
          }),
        );
        this.notification.success('Mapping saved.');
      } else {
        await firstValueFrom(
          this.mappingsApi.apiClientStaticAddressMappingsPost({
            dnsClientStaticAddressMappingWriteRequest: request,
          }),
        );
        this.notification.success('Mapping created.');
      }

      await this.router.navigate(['/client-static-address-mappings']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this mapping.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadExistingMapping(): Promise<void> {
    if (!this.mappingId) return;

    try {
      const mapping = await firstValueFrom(
        this.mappingsApi.apiClientStaticAddressMappingsIdGet({ id: this.mappingId }),
      );

      this.form.patchValue({
        cidr: mapping.cidr ?? '',
        clientIdentifier: mapping.clientIdentifier ?? '',
        protocolVersion: mapping.protocolVersion ?? InternetProtocolVersionEnum.Ipv4,
        isDisabled: mapping.isDisabled ?? false,
        comments: mapping.comments ?? '',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this mapping.'));
    } finally {
      this.loading.set(false);
    }
  }
}

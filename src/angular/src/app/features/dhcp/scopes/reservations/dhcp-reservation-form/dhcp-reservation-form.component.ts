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
  SpinnerComponent,
} from '@coreui/angular';
import {
  DhcpReservationsService,
  DhcpScopeKindEnum,
  DhcpScopesService,
} from '../../../../../api-client';
import { extractErrorMessage } from '../../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../../shared/notification/notification.service';

/**
 * This component is the add and edit page for a DHCP reservation
 * (`/dhcp-scopes/:scopeId/reservations/new` and `.../reservations/:reservationId/edit`,
 * `dhcpscopes:write`); edit mode is chosen by the `reservationId` route parameter. It saves with
 * `POST` or `PUT /api/dhcp-scopes/{scopeId}/reservations/{id}`. The backend answers 409 for a
 * client identifier or IP address already reserved in the scope, shown in {@link errorMessage}.
 *
 * <p>RFC 8415 has no reservation for a delegated prefix, so the page loads the scope's `kind`
 * ({@link loadScopeKind}) and, for a Prefix Delegation scope, shows a notice instead of the form.
 * The list page already hides "Add reservation" there; this covers a typed-in URL or bookmark.
 */
@Component({
  selector: 'app-dhcp-reservation-form',
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
    SpinnerComponent,
  ],
  templateUrl: './dhcp-reservation-form.component.html',
  styleUrl: './dhcp-reservation-form.component.scss',
})
export class DhcpReservationFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly reservationsApi = inject(DhcpReservationsService);
  private readonly scopesApi = inject(DhcpScopesService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly scopeId = this.route.snapshot.paramMap.get('scopeId') ?? '';
  private readonly reservationId = this.route.snapshot.paramMap.get('reservationId');

  /** Whether this instance is editing an existing reservation rather than adding a new one. */
  readonly isEditMode = this.reservationId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether the scope is a Prefix Delegation scope; see the class comment. */
  readonly isPrefixDelegationScope = signal(false);

  readonly form = this.formBuilder.group({
    clientIdentifier: ['', [Validators.required]],
    ipAddress: ['', [Validators.required]],
    hostName: [''],
    isDisabled: [false],
    comments: [''],
  });

  ngOnInit(): void {
    void this.loadScopeKind();
    if (this.isEditMode) void this.loadExistingReservation();
  }

  /** This method adds the reservation (`POST`) or replaces it (`PUT`), then returns to the reservation list. */
  async save(): Promise<void> {
    if (this.isPrefixDelegationScope() || this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const request = {
      clientIdentifier: value.clientIdentifier,
      ipAddress: value.ipAddress,
      hostName: value.hostName || null,
      isDisabled: value.isDisabled,
      comments: value.comments || null,
    };

    try {
      if (this.isEditMode && this.reservationId) {
        await firstValueFrom(
          this.reservationsApi.apiDhcpScopesScopeIdReservationsIdPut({
            scopeId: this.scopeId,
            id: this.reservationId,
            dhcpReservationWriteRequest: request,
          }),
        );
        this.notification.success('Reservation saved.');
      } else {
        await firstValueFrom(
          this.reservationsApi.apiDhcpScopesScopeIdReservationsPost({
            scopeId: this.scopeId,
            dhcpReservationWriteRequest: request,
          }),
        );
        this.notification.success('Reservation added.');
      }

      await this.router.navigate(['/dhcp-scopes', this.scopeId, 'reservations']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this reservation.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadScopeKind(): Promise<void> {
    try {
      const scope = await firstValueFrom(this.scopesApi.apiDhcpScopesIdGet({ id: this.scopeId }));
      this.isPrefixDelegationScope.set(scope.kind === DhcpScopeKindEnum.PrefixDelegation);
    } catch {
      // On failure the guard stays false; the backend still rejects a bad scopeId on save.
    }
  }

  private async loadExistingReservation(): Promise<void> {
    if (!this.reservationId) return;

    try {
      const reservation = await firstValueFrom(
        this.reservationsApi.apiDhcpScopesScopeIdReservationsIdGet({
          scopeId: this.scopeId,
          id: this.reservationId,
        }),
      );

      this.form.patchValue({
        clientIdentifier: reservation.clientIdentifier ?? '',
        ipAddress: reservation.ipAddress ?? '',
        hostName: reservation.hostName ?? '',
        isDisabled: reservation.isDisabled ?? false,
        comments: reservation.comments ?? '',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this reservation.'));
    } finally {
      this.loading.set(false);
    }
  }
}

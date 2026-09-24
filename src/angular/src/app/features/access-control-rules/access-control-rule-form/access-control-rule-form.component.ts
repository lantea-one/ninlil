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
import { DnsAccessControlRulesService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component is both the "create a rule" and "edit a rule" page -- one component for both,
 * distinguished by whether the route supplies an `id` (see `app.routes.ts`), mirroring
 * `ClientGroupFormComponent`'s own shape exactly since this is likewise a flat, single-entity,
 * no-secrets form.
 * <p>
 * The two known capabilities (`RecursionAccess`/`RateLimitBypass` --
 * `DnsAccessControlCapabilityEnum` is extensible to a third later, at which point this form gains
 * a third checkbox) are each their own boolean checkbox, combined into the
 * `capabilities: string[]` array `DnsAccessControlRuleWriteRequest` actually carries only at
 * submit time, and expanded back into the two booleans on load -- the same
 * `WebListenSettingsComponent` pattern this project already uses for its own
 * `allowedProtocolTls12`/`allowedProtocolTls13` checkboxes feeding a single `allowedProtocols`
 * array field, chosen here over a generic multi-select since there are only ever two (soon three)
 * well-known values, not an open-ended list.
 * </p>
 */
@Component({
  selector: 'app-access-control-rule-form',
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
  templateUrl: './access-control-rule-form.component.html',
  styleUrl: './access-control-rule-form.component.scss',
})
export class AccessControlRuleFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly accessControlRulesApi = inject(DnsAccessControlRulesService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly ruleId = this.route.snapshot.paramMap.get('id');

  /** Whether this instance is editing an existing rule rather than creating a new one. */
  readonly isEditMode = this.ruleId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    cidr: ['', [Validators.required]],
    grantsRecursionAccess: [false],
    grantsRateLimitBypass: [false],
    isDisabled: [false],
  });

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingRule();
  }

  /** This method creates (`POST`) or fully replaces (`PUT`) the rule, per {@link isEditMode}. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const request = {
      name: value.name,
      cidr: value.cidr,
      capabilities: [
        ...(value.grantsRecursionAccess ? ['RecursionAccess'] : []),
        ...(value.grantsRateLimitBypass ? ['RateLimitBypass'] : []),
      ],
      isDisabled: value.isDisabled,
    };

    try {
      if (this.isEditMode && this.ruleId) {
        await firstValueFrom(
          this.accessControlRulesApi.apiAccessControlRulesIdPut({
            id: this.ruleId,
            dnsAccessControlRuleWriteRequest: request,
          }),
        );
        this.notification.success('Rule saved.');
      } else {
        await firstValueFrom(
          this.accessControlRulesApi.apiAccessControlRulesPost({
            dnsAccessControlRuleWriteRequest: request,
          }),
        );
        this.notification.success('Rule created.');
      }

      await this.router.navigate(['/access-control-rules']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this rule.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadExistingRule(): Promise<void> {
    if (!this.ruleId) return;

    try {
      const rule = await firstValueFrom(
        this.accessControlRulesApi.apiAccessControlRulesIdGet({ id: this.ruleId }),
      );

      const capabilities = rule.capabilities ?? [];
      this.form.patchValue({
        name: rule.name ?? '',
        cidr: rule.cidr ?? '',
        grantsRecursionAccess: capabilities.includes('RecursionAccess'),
        grantsRateLimitBypass: capabilities.includes('RateLimitBypass'),
        isDisabled: rule.isDisabled ?? false,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this rule.'));
    } finally {
      this.loading.set(false);
    }
  }
}

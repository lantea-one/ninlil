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
 * This component is the create and edit page for a DNS access control rule
 * (`/access-control-rules/new` and `/access-control-rules/:id/edit`); edit mode is chosen by the
 * `id` route parameter. It loads a rule with `GET /api/access-control-rules/{id}` and saves with
 * `POST /api/access-control-rules` or `PUT /api/access-control-rules/{id}`
 * (`accesscontrol:write`, which the route also requires).
 *
 * <p>A rule grants capabilities to a network. The two capabilities (`RecursionAccess` and
 * `RateLimitBypass`, from `DnsAccessControlCapabilityEnum`) are separate checkboxes, combined into
 * the request's `capabilities` array on save and split back out on load. A new capability needs a
 * new checkbox here.
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

  /** This method creates the rule (`POST`) or replaces it (`PUT`), then returns to the list. */
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

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
import { DnsRewritesService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component is both the "create a rewrite" and "edit a rewrite" page -- one component for
 * both, distinguished by whether the route supplies an `id` (see `app.routes.ts`).
 *
 * `domain` and `target` are both plain text fields, not structured builders -- matching this
 * project's own established "accept a string, let the real backend validator/classifier decide"
 * pattern (the same one `ForwarderFormComponent`'s own `definitionString` field and
 * `RecordFormComponent`'s own `data` field already follow). Any rejection
 * `DnsRewritesController`'s own domain/target validation returns (an invalid domain name, or a
 * target that is neither a valid IP address nor a valid domain name) is surfaced here verbatim
 * via {@link extractErrorMessage}, not re-validated client-side beyond "not empty." A `target`'s
 * own record kind (A/AAAA/CNAME) is never asked for here -- it is always computed server-side
 * from the value actually submitted, per `DnsRewriteDetail.TargetKind`'s own doc comment.
 */
@Component({
  selector: 'app-dns-rewrite-form',
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
  templateUrl: './dns-rewrite-form.component.html',
  styleUrl: './dns-rewrite-form.component.scss',
})
export class DnsRewriteFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly rewritesApi = inject(DnsRewritesService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly rewriteId = this.route.snapshot.paramMap.get('id');

  /** Whether this instance is editing an existing rewrite rather than creating a new one. */
  readonly isEditMode = this.rewriteId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    domain: ['', [Validators.required]],
    target: ['', [Validators.required]],
    isDisabled: [false],
    comments: [''],
  });

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingRewrite();
  }

  /** This method creates (`POST`) or fully replaces (`PUT`) the rewrite, per {@link isEditMode}. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const request = {
      domain: value.domain,
      target: value.target,
      isDisabled: value.isDisabled,
      comments: value.comments.trim().length > 0 ? value.comments : null,
    };

    try {
      if (this.isEditMode && this.rewriteId) {
        await firstValueFrom(
          this.rewritesApi.apiDnsRewritesIdPut({
            id: this.rewriteId,
            dnsRewriteWriteRequest: request,
          }),
        );
        this.notification.success('Rewrite saved.');
      } else {
        await firstValueFrom(
          this.rewritesApi.apiDnsRewritesPost({ dnsRewriteWriteRequest: request }),
        );
        this.notification.success('Rewrite created.');
      }

      await this.router.navigate(['/dns-rewrites']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this rewrite.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadExistingRewrite(): Promise<void> {
    if (!this.rewriteId) return;

    try {
      const rewrite = await firstValueFrom(
        this.rewritesApi.apiDnsRewritesIdGet({ id: this.rewriteId }),
      );

      this.form.patchValue({
        domain: rewrite.domain ?? '',
        target: rewrite.target ?? '',
        isDisabled: rewrite.isDisabled ?? false,
        comments: rewrite.comments ?? '',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this rewrite.'));
    } finally {
      this.loading.set(false);
    }
  }
}

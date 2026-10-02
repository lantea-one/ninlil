import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { firstValueFrom } from 'rxjs';
import {
  DnsFilterBlockResponseModeEnum,
  DnsFilterListPrecedenceEnum,
  FilteringSettingsService,
} from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Filtering" settings page (`/settings/filtering`). It edits
 * `DnsFilterOptions` through `GET` and `PATCH /api/filtering-settings` (`filteringsettings:read`,
 * `filteringsettings:write`): whether the filter engine is used, how a blocked name is answered,
 * allow/block precedence and CNAME-cloaking blocking. The lists and entries themselves are on the
 * Filter Lists page.
 *
 * <p>Every field is hot-reloaded: `DnsFilterEngine` is always in the query pipeline and reads these
 * options on each check, whatever `Enable` was at startup.
 */
@Component({
  selector: 'app-filtering-settings',
  imports: [
    IfPermissionDirective,
    ReactiveFormsModule,
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
  ],
  templateUrl: './filtering-settings.component.html',
  styleUrl: './filtering-settings.component.scss',
})
export class FilteringSettingsComponent implements OnInit {
  private readonly filteringApi = inject(FilteringSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** The block-response modes offered, one per backend enum value. */
  readonly blockResponseModeOptions = [
    { value: DnsFilterBlockResponseModeEnum.NxDomain, label: 'NXDOMAIN' },
    { value: DnsFilterBlockResponseModeEnum.SinkholeAddress, label: 'Sinkhole address' },
  ];

  /** The precedences offered, one per backend enum value. */
  readonly precedenceOptions = [
    { value: DnsFilterListPrecedenceEnum.AllowWins, label: 'Allow wins' },
    { value: DnsFilterListPrecedenceEnum.BlockWins, label: 'Block wins' },
  ];

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enable: [false],
    blockResponseMode: [DnsFilterBlockResponseModeEnum.NxDomain as DnsFilterBlockResponseModeEnum],
    // A loose shape check, as in the A and AAAA record forms; the backend parses the address and
    // rejects one of the wrong family.
    sinkholeAddressV4: [
      '0.0.0.0',
      [Validators.required, Validators.pattern(/^(\d{1,3}\.){3}\d{1,3}$/)],
    ],
    sinkholeAddressV6: ['::', [Validators.required, Validators.pattern(/^[0-9a-fA-F:]+$/)]],
    // DnsFilterOptions.SinkholeTimeToLiveSeconds is a 32-bit unsigned integer.
    sinkholeTimeToLiveSeconds: [
      60,
      [Validators.required, Validators.min(0), Validators.max(4294967295)],
    ],
    precedence: [DnsFilterListPrecedenceEnum.AllowWins as DnsFilterListPrecedenceEnum],
    // On by default on the backend too (DnsFilterOptions.BlockCnameCloaking): a list that blocks
    // a domain should block it even when it is reached through another name's CNAME.
    blockCnameCloaking: [true],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.filteringApi.apiFilteringSettingsPatch({ body: value }),
      );
      this.notification.success(result.note ?? 'Filtering settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save filtering settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.filteringApi.apiFilteringSettingsGet());
      this.form.patchValue({
        enable: settings.enable ?? false,
        blockResponseMode: settings.blockResponseMode ?? DnsFilterBlockResponseModeEnum.NxDomain,
        sinkholeAddressV4: settings.sinkholeAddressV4 ?? '0.0.0.0',
        sinkholeAddressV6: settings.sinkholeAddressV6 ?? '::',
        sinkholeTimeToLiveSeconds: settings.sinkholeTimeToLiveSeconds ?? 60,
        precedence: settings.precedence ?? DnsFilterListPrecedenceEnum.AllowWins,
        blockCnameCloaking: settings.blockCnameCloaking ?? true,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load filtering settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}

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
 * This component reads and writes this host's own `DnsFilterOptions` -- the engine-level toggle
 * and block-response tuning for block/allow-list filtering, via `GET`/`PATCH /api/filtering-settings`
 * (`filteringsettings:read`/`filteringsettings:write`). Filter list and entry CRUD (the actual
 * block/allow rows) is a separate page (Filter Lists); this page covers only whether the filter
 * engine consults them at query time at all, plus how a block match is answered.
 *
 * <p>Every field this form edits takes effect immediately, with no restart -- `DnsFilterEngine` is
 * now always constructed and consulted by the query pipeline regardless of `Enable`'s own value at
 * process startup, reading its own live `Enable`/block-response/precedence configuration fresh on
 * every check via `IOptionsMonitor`. The backend's own PATCH response note (surfaced directly in
 * the success notification, never suppressed) confirms this explicitly.
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

  /** Every block-response mode this picker offers, mirroring the real backend enum. */
  readonly blockResponseModeOptions = [
    { value: DnsFilterBlockResponseModeEnum.NxDomain, label: 'NXDOMAIN' },
    { value: DnsFilterBlockResponseModeEnum.SinkholeAddress, label: 'Sinkhole address' },
  ];

  /** Every precedence this picker offers, mirroring the real backend enum. */
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
    // A loose, presentation-level pattern check, matching this project's own established
    // A/AAAA-record-form convention (a-record-form.component.ts/aaaa-record-form.component.ts) --
    // the real backend rejects a syntactically-valid-but-wrong-family address (an IPv6 literal in
    // the V4 field, and vice versa) via a real System.Net.IPAddress parse + AddressFamily check.
    sinkholeAddressV4: [
      '0.0.0.0',
      [Validators.required, Validators.pattern(/^(\d{1,3}\.){3}\d{1,3}$/)],
    ],
    sinkholeAddressV6: ['::', [Validators.required, Validators.pattern(/^[0-9a-fA-F:]+$/)]],
    // 4294967295 is the real backend bound: DnsFilterOptions.SinkholeTimeToLiveSeconds is a
    // 32-bit unsigned integer.
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

  /** This method PATCHes every field this form holds, then surfaces the backend's own hot-reload note. */
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

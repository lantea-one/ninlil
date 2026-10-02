import { Component, inject, signal } from '@angular/core';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { PresetSummary, PresetsService } from '../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/confirm-dialog/confirm-dialog.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "DNS Security Presets" page, the settings landing page (`/settings`). It
 * lists the five built-in presets (`Open`, `Baseline`, `Protective`, `Family`, `FIPS`) from
 * `GET /api/presets` (`presets:read`). With `presets:write` the operator can apply one through
 * `POST /api/presets/{name}/apply`, after a confirmation, since applying overwrites the settings
 * and filter-list subscriptions the preset names. The response note, which says which settings
 * need a restart, is shown in the success notification.
 */
@Component({
  selector: 'app-preset-list',
  imports: [
    IfPermissionDirective,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    SpinnerComponent,
    ConfirmDialogComponent,
  ],
  templateUrl: './preset-list.component.html',
  styleUrl: './preset-list.component.scss',
})
export class PresetListComponent {
  private readonly presetsApi = inject(PresetsService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is shown for a pending preset apply. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly presets = signal<PresetSummary[]>([]);
  readonly loading = signal(true);
  readonly applyingName = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadPresets();
  }

  /** This method asks for confirmation, then applies one preset. */
  async apply(preset: PresetSummary): Promise<void> {
    if (!preset.name) return;

    const confirmed = await this.confirm({
      title: `Apply the ${preset.name} preset?`,
      message: `This overwrites the ${preset.configurationSettingCount ?? 0} configuration setting(s) and attaches the ${preset.filterListSubscriptionCount ?? 0} filter-list subscription(s) this preset names. Every other setting stays untouched and remains fully editable afterward. This cannot be undone.`,
      confirmLabel: 'Apply',
    });

    if (!confirmed) return;

    this.applyingName.set(preset.name);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.presetsApi.apiPresetsNameApplyPost({ name: preset.name }),
      );
      // The backend's note says which settings need a restart; it is shown as is.
      this.notification.success(
        result.note ? `${preset.name} applied. ${result.note}` : `${preset.name} applied.`,
      );
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not apply this preset.'));
    } finally {
      this.applyingName.set(null);
    }
  }

  /**
   * This method shows {@link ConfirmDialogComponent} with the given text and resolves once the
   * operator confirms or dismisses it.
   */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private async loadPresets(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const presets = await firstValueFrom(this.presetsApi.apiPresetsGet());
      this.presets.set(presets ?? []);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load presets.'));
    } finally {
      this.loading.set(false);
    }
  }
}

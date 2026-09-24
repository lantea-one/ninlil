import { Component, Input, OnInit, inject, signal } from '@angular/core';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { SafeSearchDetail, SafeSearchService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component lists Ninlil's own bundled Safe Search / parental-control catalog
 * (`SafeSearchCatalog`/`SafeSearchController`) and lets an operator force network-wide safe
 * search on a major search engine with a single click, via each engine's own publicly documented
 * DNS-based mechanism. Enabling an entry also turns this server's single global DNS rewrite
 * toggle on if it was off -- surfaced directly in the success notification, since it's a real,
 * documented side effect (see `SafeSearchController`'s own doc comment) this page never hides.
 *
 * <p>Reused as a scoped panel, not just the global Settings page: an optional {@link clientId}/
 * {@link clientGroupId} input (mutually exclusive; set at most one) narrows every toggle to a
 * rewrite scoped to that single target instead of an unscoped, global one -- see
 * `SafeSearchController`'s own doc comment for the exact backend contract this thin wrapper
 * follows. Neither set (the global Settings page's own usage) reproduces this component's
 * pre-scoping behavior exactly.
 */
@Component({
  selector: 'app-safe-search',
  imports: [
    AlertComponent,
    BadgeComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    SpinnerComponent,
  ],
  templateUrl: './safe-search.component.html',
  styleUrl: './safe-search.component.scss',
})
export class SafeSearchComponent implements OnInit {
  private readonly safeSearchApi = inject(SafeSearchService);
  private readonly notification = inject(NotificationService);

  /** The single client this panel's every toggle targets, or `undefined` for the shared, global default. */
  @Input() clientId?: string;

  /** The single client group this panel's every toggle targets, or `undefined` for the shared, global default. */
  @Input() clientGroupId?: string;

  readonly engines = signal<SafeSearchDetail[]>([]);
  readonly loading = signal(true);
  readonly togglingKey = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  /** This method enables or disables a single catalog entry, per its own current state, within this panel's own target scope (if any). */
  async toggle(engine: SafeSearchDetail): Promise<void> {
    const key = engine.key;
    if (!key) return;

    this.togglingKey.set(key);
    this.errorMessage.set(null);

    try {
      if (engine.isEnabled) {
        await firstValueFrom(
          this.safeSearchApi.apiSafeSearchKeyDelete({
            key,
            clientId: this.clientId,
            clientGroupId: this.clientGroupId,
          }),
        );
        this.notification.success(`${engine.displayName} Safe Search disabled.`);
      } else {
        await firstValueFrom(
          this.safeSearchApi.apiSafeSearchKeyPost({
            key,
            clientId: this.clientId,
            clientGroupId: this.clientGroupId,
          }),
        );
        this.notification.success(
          `${engine.displayName} Safe Search enabled. DNS rewrites are now turned on network-wide if they weren't already.`,
        );
      }
      await this.load();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not update this engine.'));
    } finally {
      this.togglingKey.set(null);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const engines = await firstValueFrom(
        this.safeSearchApi.apiSafeSearchGet({
          clientId: this.clientId,
          clientGroupId: this.clientGroupId,
        }),
      );
      this.engines.set(engines ?? []);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load the Safe Search catalog.'));
    } finally {
      this.loading.set(false);
    }
  }
}

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
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Safe Search" settings page (`/settings/safe-search`). It lists the
 * bundled catalog from `GET /api/safe-search` (`dnsrewrites:read`) and, with `dnsrewrites:write`,
 * forces safe search on a search engine with one click, using the DNS mechanism each engine
 * documents. Enabling an entry also turns the global DNS rewrite switch on if it was off; the
 * success notification says so (see `SafeSearchController`).
 *
 * <p>The client and client group forms embed it as a panel with {@link clientId} or
 * {@link clientGroupId} set (at most one), so each toggle creates or removes rewrites scoped to
 * that target. On the settings page neither is set and the rewrites are global.
 */
@Component({
  selector: 'app-safe-search',
  imports: [
    IfPermissionDirective,
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

  /** The client every toggle targets, or `undefined` for the global setting. */
  @Input() clientId?: string;

  /** The client group every toggle targets, or `undefined` for the global setting. */
  @Input() clientGroupId?: string;

  readonly engines = signal<SafeSearchDetail[]>([]);
  readonly loading = signal(true);
  readonly togglingKey = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  /** This method enables or disables one catalog entry, depending on its state, for this panel's target. */
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

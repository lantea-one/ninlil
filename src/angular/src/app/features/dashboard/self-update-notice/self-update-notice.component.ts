import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AlertComponent } from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { SelfUpdateService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * This component renders a small, dismissible-by-navigation banner on the Dashboard when this
 * server's own self-update checker ({@link SelfUpdateService}, `GET /api/self-update`) has found
 * a genuinely newer release than the one currently running -- a real, visible notice, not just a
 * log line an unattended homelab operator would never read. Purely a notice: it links to the
 * Self-Update settings page, which is the only place this project surfaces "what to do about it";
 * this component never offers an "update now" action of its own.
 * <p>
 * Fetches its own data (unlike {@link QuickStatsComponent}/{@link HealthTileGridComponent},
 * which {@link DashboardComponent} feeds), since this is a single, small, independent read with
 * no shared state any sibling tile needs. Gated by the `selfupdate:read` permission -- a caller
 * without it simply never sees this card, the same "additive by permission" shape
 * {@link HealthTileGridComponent} already establishes, rather than surfacing a confusing
 * permission-denied error on a page that otherwise loaded fine.
 */
@Component({
  selector: 'app-self-update-notice',
  imports: [RouterLink, AlertComponent],
  templateUrl: './self-update-notice.component.html',
  styleUrl: './self-update-notice.component.scss',
})
export class SelfUpdateNoticeComponent implements OnInit {
  private readonly selfUpdateApi = inject(SelfUpdateService);
  private readonly auth = inject(AuthService);

  /** Whether a genuinely newer release is currently known -- the one condition this card renders for. */
  readonly updateAvailable = signal(false);

  /** The newest known version, shown in the banner's own text. */
  readonly latestKnownVersion = signal<string | null>(null);

  ngOnInit(): void {
    if (!this.auth.hasPermission('selfupdate:read')) return;
    void this.loadStatus();
  }

  private async loadStatus(): Promise<void> {
    try {
      const status = await firstValueFrom(this.selfUpdateApi.apiSelfUpdateGet());
      this.updateAvailable.set(status.updateAvailable ?? false);
      this.latestKnownVersion.set(status.latestKnownVersion ?? null);
    } catch {
      // A failed read here is silent, deliberately -- this is a purely additive notice, not a
      // page an operator depends on for correctness; the card just stays hidden.
    }
  }
}

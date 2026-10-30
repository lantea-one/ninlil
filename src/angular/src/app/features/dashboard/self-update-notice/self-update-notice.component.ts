import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AlertComponent } from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { SelfUpdateService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * This component shows a banner on the Dashboard when the self-update checker
 * (`GET /api/self-update`, `selfupdate:read`) knows of a newer release than the one running, so
 * an operator sees it without reading logs. It links to the Self-Update settings page only when
 * the user holds `selfupdatesettings:read`, the permission that route needs; otherwise the text
 * tells the user to ask an administrator. It offers no update action itself.
 *
 * <p>Unlike the other dashboard pieces, it loads its own data, since nothing else on the page
 * needs it. Without `selfupdate:read` it makes no request and shows nothing.
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

  /** Whether a newer release is known; the banner shows only then. */
  readonly updateAvailable = signal(false);

  /** Whether the user may open the Self-Update settings page (`selfupdatesettings:read`). */
  readonly canOpenSettings = this.auth.hasPermission('selfupdatesettings:read');

  /** The newest known version, shown in the banner text. */
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
      // A failed read keeps the banner hidden; it is only a notice.
    }
  }
}

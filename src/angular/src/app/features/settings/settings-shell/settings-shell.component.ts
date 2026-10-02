import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AlertComponent, NavComponent, NavItemComponent, NavLinkDirective } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { filter, map } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import {
  SETTINGS_NAV_GROUPS,
  SettingsNavGroup,
  SettingsTab,
  settingsTabWritePermission,
} from './settings-nav';

/**
 * This component is the "Presets & Settings" area (`/settings`): every settings page renders in
 * its `<router-outlet>`, beside a navigation list built from {@link SETTINGS_NAV_GROUPS}. Each
 * category is a native `<details>` disclosure, so it can be collapsed without extra state. Like
 * the main navigation in `AppShellComponent`, tabs the user cannot read are hidden, on top of the
 * route guards. When the user can read but not change the page showing, the shell shows one
 * read-only notice above it.
 */
@Component({
  selector: 'app-settings-shell',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    AlertComponent,
    NavComponent,
    NavItemComponent,
    NavLinkDirective,
    IconDirective,
  ],
  templateUrl: './settings-shell.component.html',
  styleUrl: './settings-shell.component.scss',
})
export class SettingsShellComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  /** The current URL's path, without any query string or fragment. */
  private readonly currentPath = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url.split(/[?#]/)[0]),
    ),
    { initialValue: this.router.url.split(/[?#]/)[0] },
  );

  /** The tab whose page is showing, or `null` for a path no tab owns. */
  readonly activeTab = computed<SettingsTab | null>(
    () =>
      SETTINGS_NAV_GROUPS.flatMap((group) => group.tabs).find(
        (tab) => tab.path === this.currentPath(),
      ) ?? null,
  );

  /**
   * The write permission the user lacks for the page showing, or `null` when they may change it.
   * Each page hides its Save and other changing controls in that case (see
   * `IfPermissionDirective`); the shell explains why once, above the page.
   */
  readonly missingWritePermission = computed(() => {
    const tab = this.activeTab();
    if (!tab) return null;
    const writePermission = settingsTabWritePermission(tab);
    return this.authService.hasPermission(writePermission) ? null : writePermission;
  });

  /**
   * The navigation groups with only the tabs the user can read. A group with no readable tab is
   * left out rather than shown as an empty heading.
   */
  readonly visibleGroups = computed<readonly SettingsNavGroup[]>(() =>
    SETTINGS_NAV_GROUPS.map((group) => ({
      label: group.label,
      tabs: group.tabs.filter((tab) => this.authService.hasPermission(tab.permission)),
    })).filter((group) => group.tabs.length > 0),
  );
}

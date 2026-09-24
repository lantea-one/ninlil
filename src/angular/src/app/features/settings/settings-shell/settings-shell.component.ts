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
 * This component is the small shell every settings sub-page (presets, the recursive resolver,
 * rate-limiting, event broker, DNS Cookies, query logging, reverse proxy, appearance, storage, DHCP,
 * and the rest of the 38-page catalog) renders under -- one shared `<router-outlet>` beside a
 * grouped, categorized navigation list (8 labeled categories, each a native `<details>`/`<summary>`
 * disclosure so an operator can collapse a category they don't need without any custom
 * open/closed-state tracking), replacing the old flat, wrapping 37-tab row. Mirrors
 * `AppShellComponent`'s own "hide, don't just guard" nav-item pattern at a smaller scale, since
 * these sub-pages sit under one shared top-level nav item ("Presets & Settings") rather than each
 * getting its own.
 *
 * <p>The "Appearance" tab edits this host's own *global default* appearance
 * (`GET`/`PATCH /api/appearance-settings`, `appearance:read`/`appearance:write`) -- distinct from
 * the per-user override the header's own {@link ThemeSwitcherComponent} edits
 * (`GET`/`PATCH /api/appearance-settings/me`), which any signed-in caller can already reach with
 * no `appearance:read` permission at all. This tab is the deliverable an operator with
 * administrative access uses to set the appearance every user without their own override inherits.
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
   * The permission this session lacks to change the showing page, or `null` when it may change
   * it. Each page hides its own Save and other changing controls in that case (see
   * `IfPermissionDirective`); the shell explains why, once, above whichever page it is, rather
   * than every page carrying its own copy of the notice.
   */
  readonly missingWritePermission = computed(() => {
    const tab = this.activeTab();
    if (!tab) return null;
    const writePermission = settingsTabWritePermission(tab);
    return this.authService.hasPermission(writePermission) ? null : writePermission;
  });

  /**
   * The settings navigation groups the current session's own permissions actually grant access
   * to -- each group's own `tabs` array is filtered to only the tabs a held permission unlocks,
   * and a group left with zero visible tabs is dropped entirely rather than rendered as an empty
   * category header.
   */
  readonly visibleGroups = computed<readonly SettingsNavGroup[]>(() =>
    SETTINGS_NAV_GROUPS.map((group) => ({
      label: group.label,
      tabs: group.tabs.filter((tab) => this.authService.hasPermission(tab.permission)),
    })).filter((group) => group.tabs.length > 0),
  );
}

import {
  AfterViewInit,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DomSanitizer } from '@angular/platform-browser';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import {
  ButtonDirective,
  ContainerComponent,
  DropdownComponent,
  DropdownDividerDirective,
  DropdownHeaderDirective,
  DropdownItemDirective,
  DropdownMenuDirective,
  DropdownToggleDirective,
  HeaderComponent,
  HeaderTogglerDirective,
  SidebarBrandComponent,
  SidebarComponent,
  SidebarFooterComponent,
  SidebarHeaderComponent,
  SidebarService,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { AuthService } from '../../core/auth/auth.service';
import { BrandingService } from '../../core/branding/branding.service';
import { ThemeSwitcherComponent } from '../../shared/theme-switcher/theme-switcher.component';
import { NAV_ITEMS } from './nav-item.model';

/**
 * This component is the frame around every signed-in page: a CoreUI sidebar with the nav, and a
 * header with a link to the API documentation (`/documentation`), the {@link ThemeSwitcherComponent}
 * and an account menu (Profile, Security, Log out). Pages render in its `<router-outlet>`. It is
 * the component of the `authGuard`-protected parent route in `app.routes.ts`.
 *
 * <p>The nav shows only the {@link NAV_ITEMS} whose permission the user holds. Hiding is for
 * tidiness; {@link permissionGuard} on each route is what blocks access.
 *
 * <p>The nav is plain `nav-item`/`nav-link` markup instead of the sidebar's `[navItems]` input,
 * because that input renders icons through CoreUI's icon-font convention, while the app uses the
 * SVG `cIcon` everywhere.
 *
 * <p>Two CoreUI behaviors need work-arounds:
 *
 * - The desktop icon-rail collapse is {@link sidebarNarrow}, bound to both `narrow` and
 *   `unfoldable`; see that property for why. On some cold page loads the sidebar's internal
 *   `unfoldable` signal still ends up `true` while the input is `false`, so
 *   {@link forceSidebarUnfoldableSync} sets it directly after the view initializes, after every
 *   navigation and after each toggle. It does nothing when the two already agree.
 * - The mobile off-canvas sidebar is opened through {@link SidebarService.toggle}. Below the
 *   mobile breakpoint the sidebar manages visibility itself, and neither a `[visible]` binding
 *   nor the `cSidebarToggle` directives open it reliably there.
 *
 * <p>CoreUI's mobile breakpoint is `lg` (992px), so the header's menu button is `d-lg-none` and
 * the sidebar footer's collapse button is `d-lg-flex`. The menu button holds a `cilMenu` icon:
 * CoreUI's header toggler draws nothing by itself, so without the icon the button is invisible
 * and the navigation unreachable below `lg`. Its label is set through the directive's `ariaLabel`
 * input; a plain `aria-label` attribute is overwritten by the directive's default.
 *
 * <p>The sidebar brand and the header title show the server's branding ({@link BrandingService}):
 * the site title and, in the sidebar, the logo in place of the icon, each with a default.
 */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    ButtonDirective,
    ContainerComponent,
    DropdownComponent,
    DropdownDividerDirective,
    DropdownHeaderDirective,
    DropdownItemDirective,
    DropdownMenuDirective,
    DropdownToggleDirective,
    HeaderComponent,
    HeaderTogglerDirective,
    SidebarBrandComponent,
    SidebarComponent,
    SidebarFooterComponent,
    SidebarHeaderComponent,
    IconDirective,
    ThemeSwitcherComponent,
  ],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent implements AfterViewInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly brandingService = inject(BrandingService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly sidebarService = inject(SidebarService);
  private readonly destroyRef = inject(DestroyRef);

  /** The template's {@link SidebarComponent}, used by {@link forceSidebarUnfoldableSync}. */
  private readonly sidebarRef = viewChild(SidebarComponent);

  constructor() {
    // The shell stays mounted across child navigations, so ngAfterViewInit runs only once.
    // Re-sync the sidebar after every navigation too; see the class comment for why.
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.forceSidebarUnfoldableSync());
  }

  /** The nav items the current user's permissions allow. */
  readonly visibleNavItems = computed(() =>
    NAV_ITEMS.filter(
      (item) =>
        item.permission === null ||
        (typeof item.permission === 'string'
          ? this.authService.hasPermission(item.permission)
          : this.authService.hasAnyPermission(item.permission)),
    ),
  );

  /** The sidebar brand text: the configured site title, or "Ninlil" when none is set. */
  readonly brandTitle = computed(() => this.brandingService.siteTitle() ?? 'Ninlil');

  /** The header title: the configured site title, or "Ninlil Admin" when none is set. */
  readonly headerTitle = computed(() => this.brandingService.siteTitle() ?? 'Ninlil Admin');

  /**
   * The configured logo as an image source, or `null` when none is set (the sidebar then shows
   * its icon). The server only checks that the data URI is an image, and Angular's URL sanitizer
   * rejects SVG data URIs, so the value is trusted explicitly. An `<img>` never runs script from
   * a data URI, and only an administrator with the branding permission can set it.
   */
  readonly logoUrl = computed(() => {
    const logo = this.brandingService.logoDataUri();
    return logo ? this.sanitizer.bypassSecurityTrustUrl(logo) : null;
  });

  /** The current user's sign-in name, shown in the account menu. */
  readonly userName = this.authService.userName;

  /**
   * Whether the sidebar is collapsed to an icon-only rail on desktop. The sidebar's footer
   * button toggles it; it starts `false` on every load and is not remembered.
   *
   * <p>The template binds it to both `narrow` and `unfoldable`. `unfoldable` must not stay
   * `true`: CoreUI adds the `sidebar-narrow-unfoldable` class from `unfoldable` alone, which
   * collapses the sidebar to 64px. That class also stops CoreUI publishing
   * `--cui-sidebar-occupy-start`, the width `.shell-wrapper` reserves, so the sidebar would cover
   * page content. Tying both inputs to this signal applies the collapsed, hover-to-peek styling
   * only while the sidebar is collapsed.
   */
  readonly sidebarNarrow = signal(false);

  /** This method toggles the desktop icon-rail collapse. */
  toggleSidebarNarrow(): void {
    this.sidebarNarrow.update((narrow) => !narrow);
    this.forceSidebarUnfoldableSync();
  }

  /** @inheritdoc */
  ngAfterViewInit(): void {
    this.forceSidebarUnfoldableSync();
  }

  /**
   * This method sets the sidebar's internal `unfoldable` signal to {@link sidebarNarrow}, since
   * the input binding alone is not always applied on a cold load (see the class comment).
   */
  private forceSidebarUnfoldableSync(): void {
    this.sidebarRef()?.unfoldable.set(this.sidebarNarrow());
  }

  /**
   * This method opens or closes the mobile off-canvas sidebar through {@link SidebarService},
   * the same action CoreUI's toggle directives send; see the class comment for why.
   */
  toggleMobileSidebar(): void {
    this.sidebarService.toggle({ visible: 'toggle' });
  }

  /** This method signs out and returns to the login page. */
  async logout(): Promise<void> {
    await this.authService.logout();
    await this.router.navigateByUrl('/login');
  }
}

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
import { ThemeSwitcherComponent } from '../../shared/theme-switcher/theme-switcher.component';
import { NAV_ITEMS } from './nav-item.model';

/**
 * This component is the authenticated app shell: a real CoreUI for Angular layout (a dark,
 * palette-colored {@link SidebarComponent} beside a light {@link HeaderComponent}/content area)
 * hosting every top-level page behind a `<router-outlet>`. It establishes the icon-led card/badge
 * visual language every page applies uniformly, and gives
 * {@link ThemeSwitcherComponent} its real, permanent home (the header's own nav), rather than the
 * account-security page it was previously mounted on as a temporary "Appearance" card before this
 * shell existed.
 *
 * <p>A nav item is only rendered when the signed-in user actually holds its own required
 * permission (`NAV_ITEMS`'s own `permission` field) -- hiding it from someone without access, on
 * top of (not instead of) {@link permissionGuard} actually blocking direct navigation to it, per
 * this app's own explicit "hidden AND blocked, not just one or the other" requirement.
 *
 * <p>The sidebar nav itself is hand-authored markup (plain `nav-item`/`nav-link` elements styled
 * by CoreUI's own compiled CSS) rather than {@link SidebarComponent}'s alternate data-driven
 * `[navItems]` input -- that input's icon rendering is tied to CoreUI's classic icon-font/sprite
 * convention, a different mechanism than the `cIcon` SVG-based icon set this app already
 * standardized on for the login page and theme switcher; hand-authoring keeps icon rendering
 * consistent across the whole app on one mechanism.
 *
 * <p>The sidebar's desktop icon-rail collapse and mobile off-canvas show/hide are driven by two
 * different mechanisms, chosen per a real-browser check made during this shell's own
 * verification: the desktop collapse is this component's own {@link sidebarNarrow} signal, bound
 * to both {@link SidebarComponent}'s `narrow` *and* `unfoldable` inputs together (see
 * {@link sidebarNarrow}'s own doc comment for why binding `unfoldable` to a permanent `true`,
 * this shell's own original approach, was a real bug rather than a stylistic choice). The mobile
 * off-canvas show/hide instead calls {@link SidebarService.toggle} directly (`{ visible: 'toggle'
 * }`, the exact action shape CoreUI's own toggle *directives* construct internally) rather than
 * using those directives on the trigger button -- `SidebarComponent` runs its own internal,
 * viewport-breakpoint-driven visibility logic below the mobile breakpoint, and that same
 * real-browser check found neither a plain external `[visible]` binding nor the
 * `cSidebarToggle`/`cSidebarToggler` directives reliably flipped it there, while calling the
 * service's own `toggle()` method directly does.
 *
 * <p>That mobile breakpoint is CoreUI's own compiled-CSS default, `--cui-mobile-breakpoint: lg`
 * (992px) -- the header's own mobile nav-toggle button (`cHeaderToggler` in the template) is
 * sized to match it exactly (`d-lg-none`, hidden at 992px and above) rather than Bootstrap's more
 * commonly reached-for `md` (768px) cutoff, which this shell originally used and which left every
 * viewport width between 768px and 992px with neither an inline sidebar nor a working toggle
 * button to reach one -- a real, confirmed responsive dead zone with no visible affordance to
 * primary navigation at all in that range.
 *
 * <p>The `[unfoldable]="sidebarNarrow()"` binding above is necessary but was found, on live
 * verification, not to be sufficient on every genuinely cold page load (a brand-new tab or a
 * fresh sign-in, as opposed to a client-side route change within an already-running session):
 * {@link SidebarComponent}'s own internal `unfoldable` state is a `linkedSignal` mirroring that
 * binding, and on some cold loads it was observed rendering the collapsed
 * `sidebar-narrow-unfoldable` class even though this component's own {@link sidebarNarrow} signal
 * read `false` throughout -- a genuine divergence between CoreUI's internal state and the real
 * input driving it, not a stale reading (confirmed via the collapse-toggle button's own
 * `aria-label`, which tracks {@link sidebarNarrow} directly and correctly said "Collapse sidebar"
 * the whole time). Reading CoreUI's own compiled source found no code path in this app's actual
 * usage that should set that internal signal `true` given a `false` input -- no
 * `cSidebarToggle`/`cSidebarToggler` directive is used, and the one internal code path that does
 * call `unfoldable.set(...)` directly (a `{ toggle: 'unfoldable' }` action on the shared,
 * root-singleton {@link SidebarService}) is never dispatched anywhere in this app. Rather than
 * leave the binding to resolve this alone, {@link ngAfterViewInit} and every subsequent
 * `NavigationEnd` explicitly re-assert {@link SidebarComponent.unfoldable} (a plain, public
 * `WritableSignal`) to match {@link sidebarNarrow} directly, via {@link sidebarRef} -- a cheap,
 * idempotent no-op when the two already agree (confirmed the common case), and a real correction
 * on the rare cold load where they don't, so this component's own intended state always wins
 * rather than depending solely on input-binding propagation timing.
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
  private readonly sidebarService = inject(SidebarService);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * A live reference to the template's own {@link SidebarComponent} instance -- used only by
   * {@link forceSidebarUnfoldableSync} to reach its public `unfoldable` signal directly. See this
   * class's own doc comment for why a plain `[unfoldable]` input binding alone was not reliable
   * on every genuinely cold page load.
   */
  private readonly sidebarRef = viewChild(SidebarComponent);

  constructor() {
    // A route change within an already-mounted app shell never re-runs ngAfterViewInit (the
    // shell itself, and its one c-sidebar, stay mounted across every child-route navigation --
    // only <router-outlet>'s own content swaps), so ngAfterViewInit alone only guards a
    // genuinely fresh mount. Re-asserting on every NavigationEnd too costs nothing on the common
    // path (a no-op once the sidebar's own internal state already agrees) and closes any window
    // where CoreUI's internal state could still drift after that first check -- see this class's
    // own doc comment for why this explicit correction exists at all.
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.forceSidebarUnfoldableSync());
  }

  /** The nav items the current session's own permissions actually grant access to. */
  readonly visibleNavItems = computed(() =>
    NAV_ITEMS.filter(
      (item) =>
        item.permission === null ||
        (typeof item.permission === 'string'
          ? this.authService.hasPermission(item.permission)
          : this.authService.hasAnyPermission(item.permission)),
    ),
  );

  /** The current session's own sign-in name, shown in the header's user menu. */
  readonly userName = this.authService.userName;

  /**
   * Whether the sidebar is collapsed to an icon-only rail on desktop -- toggled by the sidebar's
   * own footer button, and always `false` on a fresh load (a deliberate, non-persisted default;
   * this app never remembers a prior session's own collapse choice). The template binds this
   * same signal to both {@link SidebarComponent}'s `narrow` *and* `unfoldable` inputs together,
   * not `unfoldable` alone left permanently `true` as this shell originally did.
   *
   * <p>That original binding was a real, two-part bug, root-caused directly against CoreUI's own
   * compiled source (`SidebarComponent.hostClasses`): the `sidebar-narrow-unfoldable` CSS class
   * is driven by the `unfoldable` input alone, completely independent of `narrow` -- so a
   * permanently-`true` `unfoldable` input rendered that class, and its default-collapsed
   * (`width: 64px`) styling, on every load regardless of {@link sidebarNarrow}'s own `false`
   * default (the sidebar-default-closed bug). Worse, CoreUI's own CSS deliberately excludes
   * `.sidebar-narrow-unfoldable` from the rule that publishes `--cui-sidebar-occupy-start` (the
   * custom property `app-shell.component.scss`'s own `.shell-wrapper` reads to reserve margin for
   * the sidebar) -- by design, so a genuinely-collapsed rail's own hover-to-peek can float as an
   * overlay without shoving page content sideways on every hover. With `unfoldable` pinned
   * permanently `true`, that "hover is an overlay" behavior applied even in the sidebar's supposed
   * *expanded* state, hiding real page content underneath it (the sidebar-overlays-content bug).
   * Binding `unfoldable` to this same signal instead means the `sidebar-narrow-unfoldable` class,
   * and its overlay-on-hover styling, is only ever present once the sidebar is genuinely,
   * intentionally collapsed -- the expanded (default) state renders as a plain, full-width,
   * non-narrow sidebar that properly displaces content via the standard `--cui-sidebar-occupy-start`
   * margin, fixing both bugs from the same one-line root cause.
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
   * This method forces {@link SidebarComponent}'s own internal `unfoldable` signal to match
   * {@link sidebarNarrow} directly, rather than trusting the `[unfoldable]` input binding alone
   * to have already propagated by the time this runs -- see this class's own doc comment for why.
   * A no-op when the two already agree (the common case, confirmed on every live check made
   * while diagnosing this); a real correction the rare time they don't.
   */
  private forceSidebarUnfoldableSync(): void {
    this.sidebarRef()?.unfoldable.set(this.sidebarNarrow());
  }

  /**
   * This method toggles the mobile off-canvas sidebar via {@link SidebarService} directly -- see
   * this class's own doc comment for why a plain `[visible]` binding or the toggle directives
   * don't reliably work here, while calling the service is what CoreUI's own directives do
   * internally anyway.
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

import { Injectable } from '@angular/core';

/**
 * This service wraps the one, narrow browser API call this app ever needs for a genuine
 * full-page navigation -- leaving this Angular app entirely, rather than an in-app `Router`
 * navigation -- currently used only by `LoginComponent.startSsoSignIn` to hand off to
 * `GET /api/auth/sso/challenge`'s own server-side redirect chain.
 *
 * <p>Exists purely so that call site is unit-testable: a real browser (confirmed directly
 * against a real failing test, not assumed) does not allow `window.location`'s own properties or
 * methods to be replaced or spied on at all, in any of the several ways that would ordinarily
 * work -- `Object.defineProperty` on `location.href`, `spyOnProperty` on its setter, `spyOn` on
 * `location.assign` -- every one throws in a genuine Chrome instance, only ever "working" against
 * `jsdom`-style test environments this project's own real-Chrome Karma setup does not use. This
 * service is the one seam between that immovable browser API and a plain, ordinary Jasmine fake
 * object, the same shape every other injected dependency in this app's own spec files already
 * uses.
 */
@Injectable({ providedIn: 'root' })
export class BrowserNavigationService {
  /**
   * This method performs a real, full-page browser navigation to `url` -- the browser leaves
   * this Angular app entirely, exactly as if the caller had typed the address bar themselves.
   */
  navigateFullPage(url: string): void {
    window.location.assign(url);
  }
}

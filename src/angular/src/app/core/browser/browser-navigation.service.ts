import { Injectable } from '@angular/core';

/**
 * This service performs a full-page navigation that leaves the Angular app, as opposed to a
 * `Router` navigation. `LoginComponent.startSsoSignIn` uses it to hand off to the server-side
 * redirects of `GET /api/auth/sso/challenge`.
 *
 * <p>It exists so that call can be tested. Chrome, which the tests run in, does not let a test
 * replace or spy on `window.location` (`Object.defineProperty` on `href`, `spyOnProperty` and
 * `spyOn(location, 'assign')` all throw), so specs inject a fake of this service instead.
 */
@Injectable({ providedIn: 'root' })
export class BrowserNavigationService {
  /**
   * This method navigates the browser to `url` as a full page load, leaving the Angular app.
   */
  navigateFullPage(url: string): void {
    window.location.assign(url);
  }
}

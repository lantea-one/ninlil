import { TestBed } from '@angular/core/testing';
import { provideCoreUiIcons } from './app/core/icons/core-ui-icons';

/**
 * Global spec setup: every spec in the bundle shares this root-level `beforeEach`. It registers
 * the app's CoreUI icon set in each TestBed, the way the initializer in `app.config.ts` does at
 * run time. Without it `IconSetService` is empty and every `cIcon` in a rendered template logs a
 * "CoreUI WARN: Icon ... is not registered" line, which buries real warnings.
 */
beforeEach(() => {
  TestBed.configureTestingModule({
    providers: [provideCoreUiIcons()],
  });
});

import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { CardBodyComponent, CardComponent, CardHeaderComponent } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';

/**
 * This component is a deliberately minimal stand-in for any route that does not yet have real
 * page content built for it -- rendered behind a real, permission-gated route, so this app's
 * nav-hiding-and-guard-blocking pattern has somewhere real to demonstrate itself against. No
 * route in `app.routes.ts` currently points at this component (every backend resource now has a
 * real feature page), but it stays in `shared/` as this app's own established stand-in for
 * whatever a future resource needs before its real page exists.
 */
@Component({
  selector: 'app-placeholder',
  imports: [CardBodyComponent, CardComponent, CardHeaderComponent, IconDirective],
  template: `
    <c-card class="placeholder-card">
      <c-card-header class="d-flex align-items-center gap-2">
        <svg cIcon name="cilCog" size="lg"></svg>
        <span>{{ title() }}</span>
      </c-card-header>
      <c-card-body>
        <p>
          This page is not built yet. You can see it because your account holds the permission this
          route requires -- the permission-gating itself is real, only this page's own content is
          still a stand-in.
        </p>
      </c-card-body>
    </c-card>
  `,
  styles: `
    .placeholder-card {
      max-width: var(--ninlil-form-max-width-compact);
      margin: var(--ninlil-space-5) auto;
    }
  `,
})
export class PlaceholderComponent {
  private readonly route = inject(ActivatedRoute);

  /** The page title, read from this route's own static `data.title`. */
  readonly title = toSignal(
    this.route.data.pipe(map((data) => (data['title'] as string | undefined) ?? 'Coming Soon')),
    { initialValue: 'Coming Soon' },
  );
}

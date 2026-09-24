import { Component, input } from '@angular/core';
import { IconDirective } from '@coreui/icons-angular';

/**
 * This component is the app-wide "nothing here yet" treatment for a list-shaped page -- an icon,
 * a short heading, an optional one-line description, and a projected call-to-action (typically the
 * same "create one" button the page's own header already offers), replacing the bare single line
 * of body text ("No zones yet. Create one to get started.") every list page used before this.
 *
 * <p>Kept in `shared/` rather than any one feature folder because the shape it fills -- a resource
 * type that can legitimately start empty on a fresh homelab/router deployment, before an operator
 * has created their first zone/record/filter list/whatever else -- recurs across nearly every list
 * page in this app, not just the `zones` feature this component was first built for. A later
 * feature sub-phase should reach for this component directly rather than writing its own
 * plain-text empty state.
 *
 * <p>Usage: `<app-empty-state icon="cilStorage" heading="No records yet" description="...">`
 * with the page's own "create" button/link projected as this component's own content, e.g.:
 * ```html
 * <app-empty-state icon="cilList" heading="No zones yet" description="...">
 *   <a cButton color="primary" routerLink="/zones/new">New zone</a>
 * </app-empty-state>
 * ```
 */
@Component({
  selector: 'app-empty-state',
  imports: [IconDirective],
  templateUrl: './empty-state.component.html',
  styleUrl: './empty-state.component.scss',
})
export class EmptyStateComponent {
  /** The `cIcon` name shown above the heading, e.g. `"cilStorage"`. */
  readonly icon = input.required<string>();

  /** The short, bolded headline, e.g. `"No zones yet"`. */
  readonly heading = input.required<string>();

  /** An optional one- or two-sentence elaboration shown under the heading. */
  readonly description = input<string | null>(null);
}

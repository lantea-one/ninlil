import { Component, input } from '@angular/core';
import { IconDirective } from '@coreui/icons-angular';

/**
 * This component is the shared "nothing here yet" block for an empty list: an icon, a heading, an
 * optional description and projected content, usually the page's "create" button. Use it on any
 * list page instead of a plain line of text.
 *
 * <p>Usage, with the create link projected as content:
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

  /** The short, bold heading, e.g. `"No zones yet"`. */
  readonly heading = input.required<string>();

  /** An optional sentence or two shown under the heading. */
  readonly description = input<string | null>(null);
}

import { Component, input } from '@angular/core';
import { TooltipDirective } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';

/**
 * This component renders cluster config sync's own "when was this last changed, and by which
 * cluster node" indicator -- a small, unobtrusive icon carrying a tooltip, dropped into an
 * existing list table's own row rather than a new dedicated page. Shown for any of the five
 * operational-config categories cluster config sync covers (filter lists, filter list entries,
 * access-control rules, rewrites, forwarders) and, on a settings tab, a settings section.
 *
 * Deliberately renders nothing at all (not even a placeholder dash) when
 * {@link lastModifiedUtc} is absent -- an entity created before cluster config sync existed, or
 * on a deployment where sync bookkeeping was never written, has nothing honest to report, and a
 * placeholder would look like a real (if empty) value rather than "no data".
 */
@Component({
  selector: 'app-cluster-sync-info',
  imports: [TooltipDirective, IconDirective],
  template: `
    @if (lastModifiedUtc()) {
      <span
        class="cluster-sync-icon"
        tabindex="0"
        role="img"
        [attr.aria-label]="tooltipText()"
        [cTooltip]="tooltipText()"
      >
        <svg cIcon name="cilReload" size="sm" aria-hidden="true"></svg>
      </span>
    }
  `,
  styles: `
    // A plain \`<svg>\` icon with only a hover-triggered tooltip is invisible to keyboard/screen-
    // reader operators -- neither can trigger a \`:hover\`-only affordance, and an \`<svg>\` with no
    // focusable ancestor never receives focus at all. Wrapping it in a focusable \`<span>\` carrying
    // both the tooltip and a real \`aria-label\` (the svg itself marked \`aria-hidden\` so it is never
    // announced twice) makes this same "last modified / by which node" information reachable by
    // Tab, with the tooltip's own text also available as the element's accessible name.
    .cluster-sync-icon {
      display: inline-flex;
      color: var(--cui-secondary-color);
      cursor: help;
      border-radius: 0.2rem;
    }

    .cluster-sync-icon:focus-visible {
      outline: 2px solid var(--cui-primary);
      outline-offset: 2px;
    }
  `,
})
export class ClusterSyncInfoComponent {
  /** The instant this entity (or settings section) was last created or modified, if known. */
  readonly lastModifiedUtc = input<string | null | undefined>();

  /** The cluster node that made that last change, if known. */
  readonly originatingNodeId = input<string | null | undefined>();

  /**
   * This method builds the tooltip's own text -- the full timestamp plus the originating node's
   * own identifier (truncated to its first segment, the same shortened form
   * `ClusterComponent`'s own peer list already shows for a node id, since the full 36-character
   * GUID is rarely useful to read at a glance).
   */
  tooltipText(): string {
    const timestamp = this.lastModifiedUtc();
    const nodeId = this.originatingNodeId();
    const formattedTimestamp = timestamp ? new Date(timestamp).toLocaleString() : 'an unknown time';

    if (!nodeId) return `Last modified ${formattedTimestamp}`;

    const shortNodeId = nodeId.split('-')[0];
    return `Last modified ${formattedTimestamp} by node ${shortNodeId}`;
  }
}

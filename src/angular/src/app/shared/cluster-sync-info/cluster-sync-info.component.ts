import { Component, input } from '@angular/core';
import { TooltipDirective } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';

/**
 * This component shows when a cluster-synced item was last changed and by which cluster node:
 * a small icon with a tooltip. List pages put it in a row (filter lists and their entries, access
 * control rules, DNS rewrites, forwarders, users, roles), and some settings pages (DNS cookies,
 * event broker, rate limiting, reverse proxy) put it beside a section.
 *
 * It renders nothing when {@link lastModifiedUtc} is absent, since a placeholder would look like
 * a value.
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
  /** When the item or settings section was last created or changed, if known. */
  readonly lastModifiedUtc = input<string | null | undefined>();

  /** The cluster node that made that last change, if known. */
  readonly originatingNodeId = input<string | null | undefined>();

  /**
   * This method builds the tooltip text: the local time and the node id shortened to its first
   * segment, since a full GUID is hard to read at a glance.
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

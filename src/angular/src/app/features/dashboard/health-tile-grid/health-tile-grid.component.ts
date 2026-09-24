import { NgClass } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { BadgeComponent, CardBodyComponent, CardComponent } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { StatusResponse } from '../../../api-client';

/** This interface describes one rendered tile, precomputed once per {@link status} change. */
interface TileViewModel {
  name: string;
  status: string;
  description: string;
  error: string | null;
  cssClass: string;
  icon: string;
  badgeColor: string;
}

/** This function maps a health-check status string to the CSS class its tile is rendered with. */
function statusCssClass(status: string | null | undefined): string {
  switch (status) {
    case 'Healthy':
      return 'status-healthy';
    case 'Degraded':
      return 'status-degraded';
    case 'Unhealthy':
      return 'status-unhealthy';
    default:
      return 'status-unknown';
  }
}

/** This function maps a health-check status string to the `@coreui/icons` icon shown on its tile. */
function statusIcon(status: string | null | undefined): string {
  switch (status) {
    case 'Healthy':
      return 'cilCheckCircle';
    case 'Degraded':
      return 'cilBellExclamation';
    case 'Unhealthy':
      return 'cilXCircle';
    default:
      return 'cilBan';
  }
}

/** This function maps a health-check status string to the CoreUI themed color its badge/icon uses. */
function statusBadgeColor(status: string | null | undefined): string {
  switch (status) {
    case 'Healthy':
      return 'success';
    case 'Degraded':
      return 'warning';
    case 'Unhealthy':
      return 'danger';
    default:
      return 'secondary';
  }
}

/**
 * This component renders `GET /status`'s own response as an icon-led card grid: one summary card
 * for the overall aggregate status, plus one card per component when the signed-in caller holds
 * `status:read` (the backend's own additive-detail design, per `EndpointRouteBuilderStatusExtension`).
 * A caller without that permission still sees the overall card -- `status.components` is simply
 * `null` in that case, per the backend's own confirmed response shape -- with a short note
 * explaining why the rest of the grid is absent, rather than the grid silently looking empty.
 */
@Component({
  selector: 'app-health-tile-grid',
  imports: [NgClass, CardComponent, CardBodyComponent, BadgeComponent, IconDirective],
  templateUrl: './health-tile-grid.component.html',
  styleUrl: './health-tile-grid.component.scss',
})
export class HealthTileGridComponent {
  /** The `/status` response this grid renders, or `null` while still loading. */
  readonly status = input<StatusResponse | null>(null);

  /** The overall tile's own CSS status class. */
  readonly overallCssClass = computed(() => statusCssClass(this.status()?.status));

  /** The overall tile's own `@coreui/icons` icon. */
  readonly overallIcon = computed(() => statusIcon(this.status()?.status));

  /** The overall tile's own CoreUI themed badge/icon color. */
  readonly overallBadgeColor = computed(() => statusBadgeColor(this.status()?.status));

  /** Every per-component tile, precomputed from {@link status}`.components`. */
  readonly tiles = computed<TileViewModel[]>(() => {
    const components = this.status()?.components;
    if (!components) return [];

    return components.map((component) => ({
      name: component.name ?? '',
      status: component.status ?? 'Unknown',
      description: component.description ?? '',
      error: component.error ?? null,
      cssClass: statusCssClass(component.status),
      icon: statusIcon(component.status),
      badgeColor: statusBadgeColor(component.status),
    }));
  });

  /** Whether the per-component breakdown is present -- `false` for a caller without `status:read`. */
  readonly hasDetail = computed(() => this.status()?.components != null);
}

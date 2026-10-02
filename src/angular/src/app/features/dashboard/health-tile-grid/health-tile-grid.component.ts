import { NgClass } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { BadgeComponent, CardBodyComponent, CardComponent } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { StatusResponse } from '../../../api-client';

/** This interface describes one tile, recomputed when the status changes. */
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

/** This function maps a health-check status string to the CoreUI color of its badge and icon. */
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
 * This component renders the `GET /status` response on the Dashboard as a grid of cards: one for
 * the overall status, and one per component when the user holds `status:read`
 * (`EndpointRouteBuilderStatusExtension`). Without that permission `components` is `null`, and the
 * page shows the overall card with a note saying why the rest is missing.
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

  /** The overall tile's CSS status class. */
  readonly overallCssClass = computed(() => statusCssClass(this.status()?.status));

  /** The overall tile's icon. */
  readonly overallIcon = computed(() => statusIcon(this.status()?.status));

  /** The overall tile's CoreUI badge and icon color. */
  readonly overallBadgeColor = computed(() => statusBadgeColor(this.status()?.status));

  /** The per-component tiles, built from `status.components`. */
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

  /** Whether the per-component tiles are present; `false` without `status:read`. */
  readonly hasDetail = computed(() => this.status()?.components != null);
}

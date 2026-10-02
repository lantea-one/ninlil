import { Component, inject, signal } from '@angular/core';
import { Clipboard } from '@angular/cdk/clipboard';
import { CdkTableModule } from '@angular/cdk/table';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  ClusterAggregateQueryStatsDetail,
  ClusterNodeDetail,
  ClusterNodeStatusEnum,
  ClusterService,
} from '../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/confirm-dialog/confirm-dialog.component';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../shared/http-error/http-error.util';
import { NotificationService } from '../../shared/notification/notification.service';
import {
  RevealSecretDialogComponent,
  RevealSecretDialogData,
} from '../../shared/reveal-secret-dialog/reveal-secret-dialog.component';
import { EditNodeDialogComponent } from './edit-node-dialog/edit-node-dialog.component';
import { IfPermissionDirective } from '../../shared/if-permission/if-permission.directive';

/** The page size the peer list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the Cluster page (`/cluster`, `clusternodes:read`). It lists this node's
 * known peers (`GET /api/cluster/nodes`) and, with `clusternodes:write`, runs both halves of the
 * invite-code join (`POST .../invite` on the inviting node, `POST .../join` on the joining one),
 * renames a peer through {@link EditNodeDialogComponent} (`PATCH .../{id}`) and removes one
 * (`DELETE .../{id}`). Clustering must first be enabled, with an advertised host, on the
 * Settings > Cluster page (`ClusterSettingsComponent`). A peer's address, TSIG key and pinned
 * certificate are learned during the join and cannot be edited.
 *
 * <p>The page also shows the cluster dashboard from `GET /api/cluster/nodes/stats`: query counts
 * for this node and for each peer, collected over the TSIG-authenticated, TLS-encrypted cluster
 * channel. An unreachable peer shows a dash. The stats load once with the page and refresh on
 * request, not on a timer, since each load contacts every peer.
 */
@Component({
  selector: 'app-cluster',
  imports: [
    IfPermissionDirective,
    ReactiveFormsModule,
    CdkTableModule,
    DatePipe,
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    RevealSecretDialogComponent,
    EmptyStateComponent,
    EditNodeDialogComponent,
  ],
  templateUrl: './cluster.component.html',
  styleUrl: './cluster.component.scss',
})
export class ClusterComponent {
  private readonly clusterApi = inject(ClusterService);
  private readonly clipboard = inject(Clipboard);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly displayedColumns = [
    'name',
    'host',
    'status',
    'lastHeartbeat',
    'missedHeartbeats',
    'identity',
    'actions',
  ];

  /** The node whose thumbprint was just copied, to swap that row's copy icon for a check. */
  readonly copiedThumbprintNodeId = signal<string | null>(null);
  readonly statuses = ClusterNodeStatusEnum;
  readonly shortThumbprint = shortThumbprint;

  readonly nodes = signal<ClusterNodeDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** The cluster dashboard rows: this node first, then one per peer. */
  readonly statsRows = signal<ClusterAggregateQueryStatsDetail[]>([]);
  readonly statsLoading = signal(true);
  readonly statsErrorMessage = signal<string | null>(null);

  readonly inviting = signal(false);
  readonly joining = signal(false);

  readonly joinForm = this.formBuilder.group({
    inviteCode: ['', [Validators.required]],
    displayName: [''],
  });

  /** Whether the remove confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /** Whether the reveal-once dialog is shown for a new invite code. */
  readonly revealDialogVisible = signal(false);

  /** The new invite code, shown once in {@link RevealSecretDialogComponent}. */
  readonly revealDialogData = signal<RevealSecretDialogData | null>(null);

  /** Whether the edit dialog is shown. */
  readonly editDialogVisible = signal(false);

  /** The peer the edit dialog is editing, if any. */
  readonly editTarget = signal<ClusterNodeDetail | null>(null);

  constructor() {
    void this.loadPage();
    void this.loadStats();
  }

  /**
   * This method loads or reloads the cluster dashboard rows. A failure shows on the stats card
   * only; the peer list is unaffected.
   */
  async loadStats(): Promise<void> {
    this.statsLoading.set(true);
    this.statsErrorMessage.set(null);

    try {
      const rows = await firstValueFrom(this.clusterApi.apiClusterNodesStatsGet());
      this.statsRows.set(rows ?? []);
    } catch (error) {
      this.statsErrorMessage.set(extractErrorMessage(error, 'Could not load cluster query stats.'));
    } finally {
      this.statsLoading.set(false);
    }
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /**
   * This method creates an invite with `POST /api/cluster/nodes/invite` and shows its one-time
   * code once, with its expiry. The code is for the "Join a cluster" form on the other node.
   */
  async createInvite(): Promise<void> {
    this.inviting.set(true);

    try {
      const invite = await firstValueFrom(this.clusterApi.apiClusterNodesInvitePost());
      this.revealDialogData.set({
        title: 'Cluster invite created',
        message:
          'Copy this code now and paste it into the joining node\'s own "Join a cluster" form. It expires ' +
          new Date(invite.expiresAtUtc ?? '').toLocaleString() +
          ' and will never be shown again.',
        secrets: [{ label: 'Invite code', value: invite.inviteCode ?? '' }],
      });
      this.revealDialogVisible.set(true);
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not create an invite.'));
    } finally {
      this.inviting.set(false);
    }
  }

  /** This method hides the invite-code dialog. */
  onRevealDialogClosed(): void {
    this.revealDialogVisible.set(false);
  }

  /** This method joins the cluster with an invite code from another node (`POST /api/cluster/nodes/join`). */
  async join(): Promise<void> {
    if (this.joinForm.invalid || this.joining()) return;

    this.joining.set(true);
    this.errorMessage.set(null);

    const value = this.joinForm.getRawValue();

    try {
      await firstValueFrom(
        this.clusterApi.apiClusterNodesJoinPost({
          clusterJoinRequest: {
            inviteCode: value.inviteCode,
            displayName: value.displayName || undefined,
          },
        }),
      );
      this.notification.success('Joined the cluster.');
      this.joinForm.reset({ inviteCode: '', displayName: '' });
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not join with that invite code.'));
    } finally {
      this.joining.set(false);
    }
  }

  /** This method opens {@link EditNodeDialogComponent} for a peer's name and notes. */
  editNode(node: ClusterNodeDetail): void {
    this.editTarget.set(node);
    this.editDialogVisible.set(true);
  }

  /** This method reloads the peer list after the edit dialog saves. */
  async onEditSaved(): Promise<void> {
    await this.loadPage();
  }

  /** This method asks for confirmation, then removes a peer from this node's list only. */
  async removeNode(node: ClusterNodeDetail): Promise<void> {
    if (!node.id) return;

    const confirmed = await this.confirm({
      title: `Remove ${node.name}?`,
      message:
        "This removes the peer from this node's own registry only -- it does not notify the peer, and does not affect its own registry entry for this node. This cannot be undone.",
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.clusterApi.apiClusterNodesIdDelete({ id: node.id }));
      this.notification.success('Peer removed.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not remove this peer.'));
    }
  }

  /**
   * This method shows {@link ConfirmDialogComponent} with the given text and resolves once the
   * operator confirms or dismisses it.
   */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method hides the confirmation dialog and settles the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.clusterApi.apiClusterNodesGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.nodes.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load cluster peers.'));
    } finally {
      this.loading.set(false);
    }
  }

  /** This method copies a peer's full pinned certificate thumbprint. */
  copyThumbprint(node: ClusterNodeDetail): void {
    if (!node.tlsCertificateThumbprint) return;
    if (this.clipboard.copy(node.tlsCertificateThumbprint)) {
      this.copiedThumbprintNodeId.set(node.id ?? null);
    }
  }
}

/**
 * This function shortens a SHA-256 thumbprint to its first and last 8 hex digits, enough to
 * compare by eye against the peer's own; the full value is in the tooltip and on the clipboard.
 */
export function shortThumbprint(thumbprint: string | null | undefined): string {
  if (!thumbprint) return '';
  return thumbprint.length <= 20 ? thumbprint : `${thumbprint.slice(0, 8)}…${thumbprint.slice(-8)}`;
}

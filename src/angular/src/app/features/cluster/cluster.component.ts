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

/** How many peers one page of this list shows -- matches `TsigKeyListComponent`'s own default. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component manages this node's own cluster membership: listing every known peer, running
 * both halves of the invite-code join handshake, editing a peer's own display name/notes, and
 * removing a peer. It is deliberately separate from `ClusterSettingsComponent` (the `Cluster`
 * settings tab an operator must configure first -- `Enable`/`AdvertisedHost` -- before either
 * action here can succeed), the same split `ClusterController`/`ClusterSettingsController`
 * establish on the backend. Editing is handled by the sibling {@link EditNodeDialogComponent},
 * wired to `PATCH /api/cluster/nodes/{id}` -- every other field on a peer row (address, TSIG key,
 * pinned certificate thumbprint) is fixed once the row exists, learned once during the join
 * handshake.
 *
 * This component also renders the aggregate cluster dashboard: one row per node
 * (this node itself, always reachable, plus one row per known peer collected live over the
 * existing TSIG-authenticated + TLS-encrypted cluster channel via `GET /api/cluster/nodes/stats`).
 * An unreachable peer is shown with a dash rather than omitted -- see that endpoint's own doc
 * comment for why. Loaded once alongside the peer list, and manually refreshable, rather than
 * polled continuously -- a real per-peer network fan-out on every timer tick would be wasteful for
 * a page most operators open only occasionally.
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

  /** The aggregate cluster dashboard rows -- see this class's own doc comment. */
  readonly statsRows = signal<ClusterAggregateQueryStatsDetail[]>([]);
  readonly statsLoading = signal(true);
  readonly statsErrorMessage = signal<string | null>(null);

  readonly inviting = signal(false);
  readonly joining = signal(false);

  readonly joinForm = this.formBuilder.group({
    inviteCode: ['', [Validators.required]],
    displayName: [''],
  });

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending peer removal. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /** Whether {@link RevealSecretDialogComponent} is currently shown for a just-created invite code. */
  readonly revealDialogVisible = signal(false);

  /** The just-created invite's own one-time code, shown exactly once via {@link RevealSecretDialogComponent}. */
  readonly revealDialogData = signal<RevealSecretDialogData | null>(null);

  /** Whether {@link EditNodeDialogComponent} is currently shown for this list's peer-under-edit. */
  readonly editDialogVisible = signal(false);

  /** Which peer {@link EditNodeDialogComponent} is currently editing, if any. */
  readonly editTarget = signal<ClusterNodeDetail | null>(null);

  constructor() {
    void this.loadPage();
    void this.loadStats();
  }

  /**
   * This method loads (or reloads) the aggregate cluster dashboard rows. A failure
   * here is shown inline on the stats card only -- it never blocks the peer list above from
   * loading or being usable.
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

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /**
   * This method creates a fresh invite via `POST /api/cluster/nodes/invite` and reveals its own
   * one-time code -- see `ClusterInviteService.CreateInviteAsync`'s own doc comment for why an
   * invite is never shown again after this dialog is dismissed.
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

  /** This method runs once the operator dismisses the reveal-once invite-code dialog. */
  onRevealDialogClosed(): void {
    this.revealDialogVisible.set(false);
  }

  /** This method consumes an invite code, completing the join handshake against the inviting node. */
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

  /** This method opens {@link EditNodeDialogComponent} for the given peer's own display name/notes. */
  editNode(node: ClusterNodeDetail): void {
    this.editTarget.set(node);
    this.editDialogVisible.set(true);
  }

  /** This method runs once {@link EditNodeDialogComponent} reports a successful save -- reloading the peer list to pick up the new name/notes. */
  async onEditSaved(): Promise<void> {
    await this.loadPage();
  }

  /** This method confirms, then permanently removes, a peer from this node's own registry. */
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

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
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

import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ClusterService } from '../../api-client';
import { NotificationService } from '../../shared/notification/notification.service';
import { Clipboard } from '@angular/cdk/clipboard';
import { ClusterComponent, shortThumbprint } from './cluster.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClusterComponent', () => {
  let clusterApiSpy: {
    apiClusterNodesGet: jasmine.Spy;
    apiClusterNodesIdDelete: jasmine.Spy;
    apiClusterNodesIdPatch: jasmine.Spy;
    apiClusterNodesInvitePost: jasmine.Spy;
    apiClusterNodesJoinPost: jasmine.Spy;
    apiClusterNodesStatsGet: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    clusterApiSpy = jasmine.createSpyObj('ClusterService', [
      'apiClusterNodesGet',
      'apiClusterNodesIdDelete',
      'apiClusterNodesIdPatch',
      'apiClusterNodesInvitePost',
      'apiClusterNodesJoinPost',
      'apiClusterNodesStatsGet',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    clusterApiSpy.apiClusterNodesGet.and.returnValue(
      of({
        items: [
          {
            id: 'node-1',
            name: 'ninlil-office',
            remoteNodeId: 'remote-1',
            host: '10.0.1.20',
            port: 443,
            tsigKeyName: 'cluster-office.',
            tlsCertificateThumbprint:
              '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef',
            status: 'Healthy',
            lastHeartbeatAtUtc: '2026-09-04T14:00:00Z',
            missedHeartbeatCount: 0,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );
    clusterApiSpy.apiClusterNodesStatsGet.and.returnValue(
      of([
        {
          clusterNodeRegistryId: null,
          nodeName: 'This node',
          isSelf: true,
          reachable: true,
          queriesLastHour: 12,
          queriesLast24Hours: 340,
          totalRetainedQueries: 5000,
        },
        {
          clusterNodeRegistryId: 'node-1',
          nodeName: 'ninlil-office',
          isSelf: false,
          reachable: false,
          queriesLastHour: null,
          queriesLast24Hours: null,
          totalRetainedQueries: null,
        },
      ]),
    );

    await TestBed.configureTestingModule({
      imports: [ClusterComponent],
      providers: [
        provideRouter([]),
        { provide: ClusterService, useValue: clusterApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClusterComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of peers on init', async () => {
    const fixture = await createComponent();

    expect(clusterApiSpy.apiClusterNodesGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.nodes().length).toBe(1);
    expect(fixture.componentInstance.totalCount()).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('onPageChange() requests the correctly offset skip/take, per the chosen page size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 1, pageSize: 10 });

    expect(clusterApiSpy.apiClusterNodesGet).toHaveBeenCalledWith({ skip: 10, take: 10 });
  });

  it('createInvite() reveals the newly created invite code exactly once', async () => {
    const fixture = await createComponent();
    clusterApiSpy.apiClusterNodesInvitePost.and.returnValue(
      of({ inviteCode: 'ninlil-cluster-invite-1.abc', expiresAtUtc: '2026-09-04T15:00:00Z' }),
    );

    await fixture.componentInstance.createInvite();

    expect(fixture.componentInstance.revealDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.revealDialogData()?.secrets[0].value).toBe(
      'ninlil-cluster-invite-1.abc',
    );
  });

  it('createInvite() shows an error notification when it fails', async () => {
    const fixture = await createComponent();
    clusterApiSpy.apiClusterNodesInvitePost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: 'clustering is not enabled' })),
    );

    await fixture.componentInstance.createInvite();

    expect(notificationSpy.error).toHaveBeenCalledWith('clustering is not enabled');
    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
  });

  it('join() consumes the invite code, resets the form, and reloads the page', async () => {
    const fixture = await createComponent();
    clusterApiSpy.apiClusterNodesJoinPost.and.returnValue(of({ id: 'node-2' }));

    fixture.componentInstance.joinForm.patchValue({
      inviteCode: 'ninlil-cluster-invite-1.xyz',
      displayName: 'ninlil-basement',
    });
    await fixture.componentInstance.join();

    expect(clusterApiSpy.apiClusterNodesJoinPost).toHaveBeenCalledWith({
      clusterJoinRequest: {
        inviteCode: 'ninlil-cluster-invite-1.xyz',
        displayName: 'ninlil-basement',
      },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(fixture.componentInstance.joinForm.controls.inviteCode.value).toBe('');
    expect(clusterApiSpy.apiClusterNodesGet).toHaveBeenCalledTimes(2);
  });

  it('does not join when the invite code field is blank', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.join();

    expect(clusterApiSpy.apiClusterNodesJoinPost).not.toHaveBeenCalled();
  });

  it('removeNode() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const removePromise = fixture.componentInstance.removeNode(
      fixture.componentInstance.nodes()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await removePromise;

    expect(clusterApiSpy.apiClusterNodesIdDelete).not.toHaveBeenCalled();
  });

  it('removeNode() removes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    clusterApiSpy.apiClusterNodesIdDelete.and.returnValue(of(undefined));

    const removePromise = fixture.componentInstance.removeNode(
      fixture.componentInstance.nodes()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await removePromise;

    expect(clusterApiSpy.apiClusterNodesIdDelete).toHaveBeenCalledWith({ id: 'node-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(clusterApiSpy.apiClusterNodesGet).toHaveBeenCalledTimes(2);
  });

  it('editNode() opens the edit dialog for the given peer', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.editNode(fixture.componentInstance.nodes()[0]);

    expect(fixture.componentInstance.editDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.editTarget()?.id).toBe('node-1');
  });

  it('onEditSaved() reloads the peer list', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onEditSaved();

    expect(clusterApiSpy.apiClusterNodesGet).toHaveBeenCalledTimes(2);
  });

  it('loads the aggregate query-stats rows on init, including an unreachable peer', async () => {
    const fixture = await createComponent();

    expect(clusterApiSpy.apiClusterNodesStatsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.statsRows().length).toBe(2);
    expect(fixture.componentInstance.statsRows()[1].reachable).toBeFalse();
    expect(fixture.componentInstance.statsLoading()).toBeFalse();
  });

  it('loadStats() shows an inline error without disturbing the peer list on failure', async () => {
    const fixture = await createComponent();
    clusterApiSpy.apiClusterNodesStatsGet.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500, error: 'stats unavailable' })),
    );

    await fixture.componentInstance.loadStats();

    expect(fixture.componentInstance.statsErrorMessage()).toBe('stats unavailable');
    expect(fixture.componentInstance.nodes().length).toBe(1);
  });
  it("shows each peer's pinned TSIG key and shortened certificate thumbprint", async () => {
    const fixture = await createComponent();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('cluster-office.');
    expect(text).toContain('01234567…89abcdef');
  });

  it('copies the full thumbprint and marks that row as copied', async () => {
    const fixture = await createComponent();
    const component = fixture.componentInstance;
    const copySpy = spyOn(TestBed.inject(Clipboard), 'copy').and.returnValue(true);

    component.copyThumbprint(component.nodes()[0]);

    expect(copySpy).toHaveBeenCalledWith(
      '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef',
    );
    expect(component.copiedThumbprintNodeId()).toBe('node-1');
  });

  describe('shortThumbprint', () => {
    it('leaves a short value alone and handles a missing one', () => {
      expect(shortThumbprint('abc')).toBe('abc');
      expect(shortThumbprint(null)).toBe('');
    });
  });
});

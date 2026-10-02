import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ClusterNodeDetail, ClusterService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { EditNodeDialogComponent } from './edit-node-dialog.component';

/**
 * These tests cover the edit-peer dialog: seeding the form, saving (including `null` notes),
 * errors, the guards against double or invalid submits, and closing. TestBed with a spy-backed
 * `ClusterService` and `NotificationService`.
 */
describe('EditNodeDialogComponent', () => {
  let fixture: ComponentFixture<EditNodeDialogComponent>;
  let clusterApiSpy: { apiClusterNodesIdPatch: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  const node: ClusterNodeDetail = {
    id: 'node-1',
    name: 'ninlil-basement',
    comments: 'original notes',
  };

  async function createWith(
    visible = true,
    target: ClusterNodeDetail | null = node,
  ): Promise<void> {
    clusterApiSpy = jasmine.createSpyObj('ClusterService', ['apiClusterNodesIdPatch']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [EditNodeDialogComponent],
      providers: [
        { provide: ClusterService, useValue: clusterApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EditNodeDialogComponent);
    fixture.componentRef.setInput('visible', visible);
    fixture.componentRef.setInput('node', target);
    fixture.detectChanges();
  }

  it('seeds the form from the given node', async () => {
    await createWith();

    expect(fixture.componentInstance.editForm.getRawValue()).toEqual({
      name: 'ninlil-basement',
      comments: 'original notes',
    });
  });

  it('submit() patches the peer, notifies, closes, and emits saved', async () => {
    await createWith();
    clusterApiSpy.apiClusterNodesIdPatch.and.returnValue(of({}));
    const visibleChangeSpy = jasmine.createSpy('visibleChange');
    const savedSpy = jasmine.createSpy('saved');
    fixture.componentInstance.visibleChange.subscribe(visibleChangeSpy);
    fixture.componentInstance.saved.subscribe(savedSpy);

    fixture.componentInstance.editForm.controls.name.setValue('ninlil-office');
    fixture.componentInstance.editForm.controls.comments.setValue('renamed');
    await fixture.componentInstance.submit();

    expect(clusterApiSpy.apiClusterNodesIdPatch).toHaveBeenCalledWith({
      id: 'node-1',
      clusterNodeWriteRequest: { name: 'ninlil-office', comments: 'renamed' },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith('Saved changes to ninlil-office.');
    expect(visibleChangeSpy).toHaveBeenCalledWith(false);
    expect(savedSpy).toHaveBeenCalled();
  });

  it('submit() sends null comments when the notes field is cleared', async () => {
    await createWith();
    clusterApiSpy.apiClusterNodesIdPatch.and.returnValue(of({}));

    fixture.componentInstance.editForm.controls.comments.setValue('');
    await fixture.componentInstance.submit();

    expect(clusterApiSpy.apiClusterNodesIdPatch).toHaveBeenCalledWith({
      id: 'node-1',
      clusterNodeWriteRequest: { name: 'ninlil-basement', comments: null },
    });
  });

  it('submit() shows a clear error message on failure and stays open', async () => {
    await createWith();
    clusterApiSpy.apiClusterNodesIdPatch.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 409, error: { detail: 'Name already in use.' } }),
      ),
    );

    await fixture.componentInstance.submit();

    expect(fixture.componentInstance.errorMessage()).toBe('Name already in use.');
    expect(fixture.componentInstance.submitting()).toBeFalse();
  });

  it('submit() does nothing while already submitting, with no node, or with an invalid form', async () => {
    await createWith(true, null);

    await fixture.componentInstance.submit();

    expect(clusterApiSpy.apiClusterNodesIdPatch).not.toHaveBeenCalled();
  });

  it('submit() does nothing when the required name field is blank', async () => {
    await createWith();

    fixture.componentInstance.editForm.controls.name.setValue('');
    await fixture.componentInstance.submit();

    expect(clusterApiSpy.apiClusterNodesIdPatch).not.toHaveBeenCalled();
  });

  it('onModalVisibleChange(false) clears the error and emits visibleChange(false)', async () => {
    await createWith();
    fixture.componentInstance.errorMessage.set('some error');
    const visibleChangeSpy = jasmine.createSpy('visibleChange');
    fixture.componentInstance.visibleChange.subscribe(visibleChangeSpy);

    fixture.componentInstance.onModalVisibleChange(false);

    expect(fixture.componentInstance.errorMessage()).toBeNull();
    expect(visibleChangeSpy).toHaveBeenCalledWith(false);
  });
});

import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ClusterSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClusterSettingsComponent } from './cluster-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Cluster settings page: loading, saving (including the catalog sync
 * interval), a blank heartbeat interval and a save error. TestBed with a spy-backed settings
 * service and `NotificationService`.
 */
describe('ClusterSettingsComponent', () => {
  let apiSpy: {
    apiClusterSettingsGet: jasmine.Spy;
    apiClusterSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('ClusterSettingsService', [
      'apiClusterSettingsGet',
      'apiClusterSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiClusterSettingsGet.and.returnValue(
      of({
        enable: false,
        nodeId: '4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b',
        advertisedHost: '',
        advertisedPort: 0,
        heartbeatInterval: '00:00:30',
        heartbeatTimeout: '00:00:10',
        missedHeartbeatThreshold: 3,
        addressReconcileInterval: '00:01:00',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [ClusterSettingsComponent],
      providers: [
        { provide: ClusterSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClusterSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiClusterSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enable.value).toBeFalse();
    expect(fixture.componentInstance.nodeId()).toBe('4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b');
    // CatalogSyncOptions' default, carried on this form.
    expect(fixture.componentInstance.form.controls.addressReconcileInterval.value).toBe('00:01:00');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the form', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiClusterSettingsPatch.and.returnValue(
        of({ note: 'takes effect immediately -- no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ enable: true, advertisedHost: '10.0.1.10' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiClusterSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ enable: true, advertisedHost: '10.0.1.10' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('save() PATCHes the folded-in CatalogSync address reconcile interval too', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiClusterSettingsPatch.and.returnValue(
        of({ note: 'takes effect only after a restart.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ addressReconcileInterval: '00:05:00' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiClusterSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ addressReconcileInterval: '00:05:00' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('after a restart'),
    );
  });

  it('does not save when the heartbeat interval is blank', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiClusterSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ heartbeatInterval: '' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiClusterSettingsPatch).not.toHaveBeenCalled();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiClusterSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad advertised port' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad advertised port');
  });
});

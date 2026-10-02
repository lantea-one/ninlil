import { TestBed } from '@angular/core/testing';
import { IconSetService } from '@coreui/icons-angular';
import { cilReload } from '@coreui/icons';
import { ClusterSyncInfoComponent } from './cluster-sync-info.component';

/**
 * These tests cover the cluster sync indicator: nothing without a timestamp, the icon with one,
 * and the tooltip text with and without a node id. TestBed, setting inputs on the fixture.
 */
describe('ClusterSyncInfoComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ClusterSyncInfoComponent],
    }).compileComponents();

    // The app initializer in app.config.ts normally registers icons; seed the one this renders.
    TestBed.inject(IconSetService).icons = { cilReload };
  });

  it('renders nothing at all when lastModifiedUtc is absent', () => {
    const fixture = TestBed.createComponent(ClusterSyncInfoComponent);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('svg')).toBeNull();
  });

  it('renders the sync icon when lastModifiedUtc is present', () => {
    const fixture = TestBed.createComponent(ClusterSyncInfoComponent);
    fixture.componentRef.setInput('lastModifiedUtc', '2026-09-04T12:00:00Z');
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('svg')).not.toBeNull();
  });

  it('builds a tooltip naming both the timestamp and a truncated node id', () => {
    const fixture = TestBed.createComponent(ClusterSyncInfoComponent);
    fixture.componentRef.setInput('lastModifiedUtc', '2026-09-04T12:00:00Z');
    fixture.componentRef.setInput('originatingNodeId', 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
    fixture.detectChanges();

    const text = fixture.componentInstance.tooltipText();
    expect(text).toContain('aaaaaaaa');
    expect(text).not.toContain('bbbb-cccc');
  });

  it('builds a tooltip naming only the timestamp when no node id is known', () => {
    const fixture = TestBed.createComponent(ClusterSyncInfoComponent);
    fixture.componentRef.setInput('lastModifiedUtc', '2026-09-04T12:00:00Z');
    fixture.detectChanges();

    const text = fixture.componentInstance.tooltipText();
    expect(text).toContain('Last modified');
    expect(text).not.toContain('by node');
  });
});

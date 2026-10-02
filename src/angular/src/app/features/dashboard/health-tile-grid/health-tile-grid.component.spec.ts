import { TestBed } from '@angular/core/testing';
import { HealthTileGridComponent } from './health-tile-grid.component';

/**
 * These tests cover the health grid: the state before loading, the summary-only note, the
 * per-component tiles and the overall card's style. TestBed with the `status` input set.
 */
describe('HealthTileGridComponent', () => {
  async function createComponent() {
    await TestBed.configureTestingModule({
      imports: [HealthTileGridComponent],
    }).compileComponents();
    return TestBed.createComponent(HealthTileGridComponent);
  }

  it('renders "Unknown" and no tiles before any status has loaded', async () => {
    const fixture = await createComponent();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Unknown');
    expect(fixture.componentInstance.tiles()).toEqual([]);
    expect(fixture.componentInstance.hasDetail()).toBeFalse();
  });

  it('shows the detail hint and no per-component tiles when components is null', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('status', { status: 'Healthy', components: null });
    fixture.detectChanges();

    expect(fixture.componentInstance.hasDetail()).toBeFalse();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('status:read');
  });

  it('renders one tile per component, with a status-derived CSS class and icon, when detail is present', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('status', {
      status: 'Degraded',
      components: [
        { name: 'storage', status: 'Healthy', description: 'Reachable', error: null },
        {
          name: 'dnssec',
          status: 'Degraded',
          description: 'Signature nearing expiry',
          error: null,
        },
        { name: 'forwarders', status: 'Unhealthy', description: 'All down', error: 'timeout' },
      ],
    });
    fixture.detectChanges();

    expect(fixture.componentInstance.tiles().length).toBe(3);
    expect(fixture.componentInstance.tiles()[0].cssClass).toBe('status-healthy');
    expect(fixture.componentInstance.tiles()[1].cssClass).toBe('status-degraded');
    expect(fixture.componentInstance.tiles()[2].cssClass).toBe('status-unhealthy');
    expect(fixture.componentInstance.hasDetail()).toBeTrue();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('timeout');
    expect(html.textContent).not.toContain('status:read');
  });

  it('reflects the overall status in its own CSS class and icon', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('status', { status: 'Unhealthy', components: [] });
    fixture.detectChanges();

    expect(fixture.componentInstance.overallCssClass()).toBe('status-unhealthy');
    expect(fixture.componentInstance.overallIcon()).toBe('cilXCircle');
  });
});

import { TestBed } from '@angular/core/testing';
import { QuickStatsComponent } from './quick-stats.component';

/** These tests cover the quick-stat tiles, including the em dash for an unavailable count. */
describe('QuickStatsComponent', () => {
  async function createComponent() {
    await TestBed.configureTestingModule({ imports: [QuickStatsComponent] }).compileComponents();
    return TestBed.createComponent(QuickStatsComponent);
  }

  it('renders no tiles when given an empty stat list', async () => {
    const fixture = await createComponent();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.stat-tile').length).toBe(0);
  });

  it('renders one tile per stat, showing its count and label', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('stats', [
      { label: 'Zones', icon: 'dns', count: 12 },
      { label: 'Forwarders', icon: 'call_split', count: 3 },
    ]);
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelectorAll('.stat-tile').length).toBe(2);
    expect(html.textContent).toContain('12');
    expect(html.textContent).toContain('Zones');
    expect(html.textContent).toContain('3');
    expect(html.textContent).toContain('Forwarders');
  });

  it('renders an em dash, not "0", for a stat that could not be fetched', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('stats', [{ label: 'Webhooks', icon: 'webhook', count: null }]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('—');
  });
});

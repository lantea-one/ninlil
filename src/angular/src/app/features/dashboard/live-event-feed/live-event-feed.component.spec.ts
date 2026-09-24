import { TestBed } from '@angular/core/testing';
import { LiveEventFeedComponent } from './live-event-feed.component';

describe('LiveEventFeedComponent', () => {
  async function createComponent() {
    await TestBed.configureTestingModule({ imports: [LiveEventFeedComponent] }).compileComponents();
    return TestBed.createComponent(LiveEventFeedComponent);
  }

  it('labels itself as session-only, live activity', async () => {
    const fixture = await createComponent();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('resets on reload');
    expect(text).toContain('No events observed yet in this session.');
  });

  it('shows the current connection state in its own header badge', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('connectionState', 'reconnecting');
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.connection-badge') as HTMLElement;
    expect(badge.textContent?.trim()).toBe('reconnecting');
    expect(badge.classList).toContain('connection-reconnecting');
  });

  it('renders every feed entry, most-recent-first order preserved from the input', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('feed', [
      {
        id: 2,
        eventType: 'DnsZoneChanged',
        occurredAtUtc: '2026-08-30T12:00:01Z',
        summary: 'Second',
      },
      {
        id: 1,
        eventType: 'DnsZoneChanged',
        occurredAtUtc: '2026-08-30T12:00:00Z',
        summary: 'First',
      },
    ]);
    fixture.detectChanges();

    const items = fixture.nativeElement.querySelectorAll('.feed-summary');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toBe('Second');
    expect(items[1].textContent).toBe('First');
  });
});

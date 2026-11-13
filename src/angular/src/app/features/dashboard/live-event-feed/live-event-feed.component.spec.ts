import { TestBed } from '@angular/core/testing';
import { LiveEventFeedComponent } from './live-event-feed.component';

/** These tests cover the live feed card: the live-only label, the state badge and the entries. */
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

  it('draws the Live Activity heading in the size of the card header around it', async () => {
    const fixture = await createComponent();
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    const heading = element.querySelector('h2') as HTMLElement;
    const header = element.querySelector('c-card-header') as HTMLElement;
    // A bare `h2` is 1.5 times the surrounding text, which made it larger than the plain-text
    // header of the Getting Started card.
    expect(getComputedStyle(heading).fontSize).toBe(getComputedStyle(header).fontSize);
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

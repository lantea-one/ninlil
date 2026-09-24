import { TestBed } from '@angular/core/testing';
import { ClientSummaryCardsComponent } from './client-summary-cards.component';
import { ClientListFilter } from '../shared/client-list-filter.model';

describe('ClientSummaryCardsComponent', () => {
  function create(summary: object | null, activeFilter: ClientListFilter = 'all') {
    TestBed.configureTestingModule({ imports: [ClientSummaryCardsComponent] });
    const fixture = TestBed.createComponent(ClientSummaryCardsComponent);
    fixture.componentRef.setInput('summary', summary);
    fixture.componentRef.setInput('activeFilter', activeFilter);
    fixture.detectChanges();
    return fixture;
  }

  function card(fixture: ReturnType<typeof create>, filter: ClientListFilter): HTMLButtonElement {
    return fixture.nativeElement.querySelector(`[data-testid="summary-card-${filter}"]`);
  }

  it('renders every count from the summary', () => {
    const fixture = create({
      totalCount: 24,
      onlineCount: 17,
      offlineCount: 7,
      newCount: 2,
      favoriteCount: 5,
    });

    expect(card(fixture, 'all').textContent).toContain('24');
    expect(card(fixture, 'online').textContent).toContain('17');
    expect(card(fixture, 'offline').textContent).toContain('7');
    expect(card(fixture, 'new').textContent).toContain('2');
    expect(card(fixture, 'favorites').textContent).toContain('5');
  });

  it('shows an em dash rather than zero while the summary is unavailable', () => {
    const fixture = create(null);

    expect(card(fixture, 'online').textContent).toContain('—');
    expect(card(fixture, 'online').textContent).not.toContain('0');
  });

  it('marks only the active filter card as pressed', () => {
    const fixture = create(null, 'offline');

    expect(card(fixture, 'offline').getAttribute('aria-pressed')).toBe('true');
    expect(card(fixture, 'offline').classList).toContain('active');
    expect(card(fixture, 'online').getAttribute('aria-pressed')).toBe('false');
  });

  it('emits the selected card filter', () => {
    const fixture = create(null);
    const selected: ClientListFilter[] = [];
    fixture.componentInstance.filterSelected.subscribe((filter) => selected.push(filter));

    card(fixture, 'favorites').click();

    expect(selected).toEqual(['favorites']);
  });
});

import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { GettingStartedItem } from '../dashboard.model';
import { GettingStartedChecklistComponent } from './getting-started-checklist.component';

const DISMISSED_STORAGE_KEY = 'ninlil:dashboard:getting-started-dismissed';

const incompleteItems: GettingStartedItem[] = [
  { id: 'add-zone', label: 'Add your first zone', complete: false, routerLink: '/zones' },
  { id: 'create-user', label: 'Create a non-admin user', complete: true, routerLink: '/users' },
];

const allCompleteItems: GettingStartedItem[] = [
  { id: 'add-zone', label: 'Add your first zone', complete: true, routerLink: '/zones' },
  { id: 'create-user', label: 'Create a non-admin user', complete: true, routerLink: '/users' },
];

/**
 * These tests cover the checklist card: rows and links, the complete style, hiding when all rows
 * are done, and dismissal stored in `localStorage`. TestBed with a test router; the stored flag
 * is removed after each test.
 */
describe('GettingStartedChecklistComponent', () => {
  async function createComponent() {
    await TestBed.configureTestingModule({
      imports: [GettingStartedChecklistComponent],
      providers: [provideRouter([])],
    }).compileComponents();
    return TestBed.createComponent(GettingStartedChecklistComponent);
  }

  afterEach(() => {
    localStorage.removeItem(DISMISSED_STORAGE_KEY);
  });

  it('renders nothing when given an empty item list', async () => {
    const fixture = await createComponent();
    fixture.detectChanges();

    expect(fixture.componentInstance.visible()).toBeFalse();
    expect(fixture.nativeElement.querySelector('.getting-started-card')).toBeNull();
  });

  it('renders the card with one row per item, each linking to its own route, when at least one item is incomplete', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('items', incompleteItems);
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.visible()).toBeTrue();
    const rows = html.querySelectorAll('.getting-started-item');
    expect(rows.length).toBe(2);
    expect(html.textContent).toContain('Add your first zone');
    expect(html.textContent).toContain('Create a non-admin user');

    // A bound `[routerLink]` is not written to the DOM as an attribute; RouterLink sets `href`
    // instead, so the test checks that.
    const links = html.querySelectorAll('.getting-started-item a');
    expect(links.length).toBe(2);
    expect((links[0] as HTMLAnchorElement).getAttribute('href')).toBe('/zones');
    expect((links[1] as HTMLAnchorElement).getAttribute('href')).toBe('/users');
  });

  it('marks a complete row with the "complete" CSS class, and an incomplete row without it', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('items', incompleteItems);
    fixture.detectChanges();

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('.getting-started-item');
    expect(rows[0].classList.contains('complete')).toBeFalse();
    expect(rows[1].classList.contains('complete')).toBeTrue();
  });

  it('auto-hides once every item is complete, even though the card was never dismissed', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('items', allCompleteItems);
    fixture.detectChanges();

    expect(fixture.componentInstance.allComplete()).toBeTrue();
    expect(fixture.componentInstance.visible()).toBeFalse();
    expect(fixture.nativeElement.querySelector('.getting-started-card')).toBeNull();
  });

  it('hides the card and persists the choice to localStorage when dismissed', async () => {
    const fixture = await createComponent();
    fixture.componentRef.setInput('items', incompleteItems);
    fixture.detectChanges();

    const dismissButton = (fixture.nativeElement as HTMLElement).querySelector(
      'button[aria-label="Dismiss the Getting Started checklist"]',
    ) as HTMLButtonElement;
    dismissButton.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.visible()).toBeFalse();
    expect(localStorage.getItem(DISMISSED_STORAGE_KEY)).toBe('true');
  });

  it('stays hidden across a fresh component instance once localStorage already holds a dismissal', async () => {
    localStorage.setItem(DISMISSED_STORAGE_KEY, 'true');

    const fixture = await createComponent();
    fixture.componentRef.setInput('items', incompleteItems);
    fixture.detectChanges();

    expect(fixture.componentInstance.visible()).toBeFalse();
  });
});

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { IconSetService } from '@coreui/icons-angular';
import { cilStorage } from '@coreui/icons';
import { EmptyStateComponent } from './empty-state.component';

// A host component, so a test can check content projection into the action slot.
@Component({
  selector: 'app-empty-state-test-host',
  imports: [EmptyStateComponent],
  template: `
    <app-empty-state icon="cilStorage" heading="No zones yet" description="Create one to start.">
      <button type="button">New zone</button>
    </app-empty-state>
  `,
})
class EmptyStateTestHostComponent {}

/**
 * These tests cover the empty state: icon, heading and optional description, and projected
 * content. TestBed, directly and through a small host component.
 */
describe('EmptyStateComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EmptyStateComponent, EmptyStateTestHostComponent],
    }).compileComponents();

    // The app initializer in app.config.ts normally registers icons; seed the one this renders so
    // cIcon does not warn.
    TestBed.inject(IconSetService).icons = { cilStorage };
  });

  it('renders the icon, heading, and description', () => {
    const fixture = TestBed.createComponent(EmptyStateComponent);
    fixture.componentRef.setInput('icon', 'cilStorage');
    fixture.componentRef.setInput('heading', 'No records yet');
    fixture.componentRef.setInput('description', 'Add one to get started.');
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('svg')).not.toBeNull();
    expect(element.querySelector('.empty-state-heading')?.textContent?.trim()).toBe(
      'No records yet',
    );
    expect(element.querySelector('.empty-state-description')?.textContent?.trim()).toBe(
      'Add one to get started.',
    );
  });

  it('omits the description paragraph entirely when none is supplied', () => {
    const fixture = TestBed.createComponent(EmptyStateComponent);
    fixture.componentRef.setInput('icon', 'cilStorage');
    fixture.componentRef.setInput('heading', 'No records yet');
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.empty-state-description')).toBeNull();
  });

  it('projects a caller-supplied action into the action slot', () => {
    const fixture = TestBed.createComponent(EmptyStateTestHostComponent);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const action = element.querySelector('.empty-state-action button');
    expect(action?.textContent?.trim()).toBe('New zone');
  });
});

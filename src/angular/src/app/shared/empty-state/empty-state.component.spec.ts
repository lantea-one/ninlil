import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { IconSetService } from '@coreui/icons-angular';
import { cilStorage } from '@coreui/icons';
import { EmptyStateComponent } from './empty-state.component';

// A trivial host lets these tests exercise real content projection (the `<ng-content>` action
// slot) the same way every real caller uses this component, rather than only its own inputs.
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

describe('EmptyStateComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EmptyStateComponent, EmptyStateTestHostComponent],
    }).compileComponents();

    // IconSetService starts empty outside a full app bootstrap (app.config.ts's app initializer is
    // what normally registers the icons), so seed the one icon this component renders; otherwise
    // cIcon warns about an unregistered name on every render.
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

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { IfPermissionDirective } from './if-permission.directive';

@Component({
  imports: [IfPermissionDirective],
  template: `
    <button *appIfPermission="'zones:write'" data-testid="plain">Delete</button>
    <button *appIfPermission="'zones:write'; else readOnly" data-testid="with-else">Save</button>
    <ng-template #readOnly><span data-testid="else">Read-only</span></ng-template>
  `,
})
class HostComponent {}

/**
 * These tests cover `*appIfPermission`: rendering with the permission, the else template without
 * it, and re-rendering when the permissions change. A host component with a signal-backed fake
 * `AuthService`.
 */
describe('IfPermissionDirective', () => {
  // A signal-backed fake, so a test can change permissions after the first render, as a token
  // refresh or sign-out does.
  const grantedPermissions = signal<string[]>([]);

  beforeEach(() => {
    grantedPermissions.set([]);
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        {
          provide: AuthService,
          useValue: {
            hasPermission: (permission: string) => grantedPermissions().includes(permission),
          },
        },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    return fixture;
  }

  function has(fixture: { nativeElement: HTMLElement }, testId: string): boolean {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) !== null;
  }

  it('renders the element when the permission is held', () => {
    grantedPermissions.set(['zones:write']);
    const fixture = render();

    expect(has(fixture, 'plain')).toBeTrue();
    expect(has(fixture, 'with-else')).toBeTrue();
    expect(has(fixture, 'else')).toBeFalse();
  });

  it('renders nothing, or the else template, when the permission is missing', () => {
    grantedPermissions.set(['zones:read']);
    const fixture = render();

    expect(has(fixture, 'plain')).toBeFalse();
    expect(has(fixture, 'with-else')).toBeFalse();
    expect(has(fixture, 'else')).toBeTrue();
  });

  it('follows a change to the session permissions without re-creating the host', () => {
    const fixture = render();
    expect(has(fixture, 'plain')).toBeFalse();

    grantedPermissions.set(['zones:write']);
    fixture.detectChanges();
    expect(has(fixture, 'plain')).toBeTrue();
    expect(has(fixture, 'else')).toBeFalse();

    grantedPermissions.set([]);
    fixture.detectChanges();
    expect(has(fixture, 'plain')).toBeFalse();
    expect(has(fixture, 'else')).toBeTrue();
  });
});

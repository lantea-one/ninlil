import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ConfirmEmailChangeComponent } from './confirm-email-change.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the e-mail change confirmation page: posting the query values unchanged and
 * exactly once, the success message, clearing a local session, the error from the server, and
 * the links with missing values that send nothing. TestBed with a spy `AuthService` and a
 * stubbed route.
 */
describe('ConfirmEmailChangeComponent', () => {
  let fixture: ComponentFixture<ConfirmEmailChangeComponent>;
  let authServiceSpy: {
    confirmEmailChange: jasmine.Spy;
    logout: jasmine.Spy;
    isAuthenticated: jasmine.Spy;
  };

  async function createWith(
    queryParams: Record<string, string>,
    options: { signedIn?: boolean; failWith?: unknown } = {},
  ): Promise<void> {
    authServiceSpy = jasmine.createSpyObj('AuthService', [
      'confirmEmailChange',
      'logout',
      'isAuthenticated',
    ]);
    authServiceSpy.isAuthenticated.and.returnValue(options.signedIn ?? false);
    authServiceSpy.logout.and.resolveTo();
    if (options.failWith) {
      authServiceSpy.confirmEmailChange.and.rejectWith(options.failWith);
    } else {
      authServiceSpy.confirmEmailChange.and.resolveTo();
    }

    await TestBed.configureTestingModule({
      imports: [ConfirmEmailChangeComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ConfirmEmailChangeComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
  }

  const fullQuery = { userId: 'user-1', email: 'new@example.com', token: 'Abc_-123' };

  it('posts userId, email and token from the query unchanged, once', async () => {
    await createWith(fullQuery);
    fixture.detectChanges();
    await flushMicrotasks();

    expect(authServiceSpy.confirmEmailChange).toHaveBeenCalledOnceWith(
      'user-1',
      'new@example.com',
      'Abc_-123',
    );
  });

  it('shows the success message with a sign-in link', async () => {
    await createWith(fullQuery);
    const root = fixture.nativeElement as HTMLElement;

    expect(root.textContent).toContain('Your e-mail address has been changed');
    expect(root.querySelector('a[href="/login"]')).not.toBeNull();
    expect(authServiceSpy.logout).not.toHaveBeenCalled();
  });

  it('clears the local session when the browser is signed in', async () => {
    await createWith(fullQuery, { signedIn: true });

    expect(authServiceSpy.logout).toHaveBeenCalledTimes(1);
  });

  it('shows the server error for a bad token', async () => {
    await createWith(fullQuery, {
      failWith: new HttpErrorResponse({ status: 400, error: { detail: 'The link is not valid.' } }),
    });

    expect(fixture.componentInstance.errorMessage()).toBe('The link is not valid.');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('The link is not valid.');
    expect(authServiceSpy.logout).not.toHaveBeenCalled();
  });

  it('shows an error and posts nothing when a query value is missing', async () => {
    await createWith({ userId: 'user-1', email: 'new@example.com' });

    expect(authServiceSpy.confirmEmailChange).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toContain('missing or incomplete');
  });
});

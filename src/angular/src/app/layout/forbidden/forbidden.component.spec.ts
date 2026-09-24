import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ForbiddenComponent } from './forbidden.component';

describe('ForbiddenComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ForbiddenComponent],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('renders an access-denied message and a link back to the dashboard', () => {
    const fixture = TestBed.createComponent(ForbiddenComponent);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Access Denied');

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[routerLink]');
    expect(link).not.toBeNull();
  });
});

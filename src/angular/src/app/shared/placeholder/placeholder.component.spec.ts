import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { IconSetService } from '@coreui/icons-angular';
import { cilCog } from '@coreui/icons';
import { of } from 'rxjs';
import { PlaceholderComponent } from './placeholder.component';

describe('PlaceholderComponent', () => {
  it("renders the title from the activated route's own static data", async () => {
    await TestBed.configureTestingModule({
      imports: [PlaceholderComponent],
      providers: [{ provide: ActivatedRoute, useValue: { data: of({ title: 'Zones' }) } }],
    }).compileComponents();

    // This component renders a real `cIcon` (`cilCog`) as its placeholder artwork. Outside a
    // full app bootstrap, `IconSetService.icons` starts empty -- `app.config.ts`'s own
    // `provideAppInitializer` is what normally populates it -- so this spec seeds the one icon
    // it actually renders directly, mirroring that same population step at unit-test scope
    // rather than leaving `cIcon` to warn about an unregistered name on every render.
    TestBed.inject(IconSetService).icons = { cilCog };

    const fixture = TestBed.createComponent(PlaceholderComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.title()).toBe('Zones');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Zones');
  });

  it('falls back to a generic title when the route provides none', async () => {
    await TestBed.configureTestingModule({
      imports: [PlaceholderComponent],
      providers: [{ provide: ActivatedRoute, useValue: { data: of({}) } }],
    }).compileComponents();

    // See the icon-seeding note in the spec above -- this render path hits the same `cIcon`.
    TestBed.inject(IconSetService).icons = { cilCog };

    const fixture = TestBed.createComponent(PlaceholderComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.title()).toBe('Coming Soon');
  });
});

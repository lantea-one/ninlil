import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { BrandingSettingsService as BrandingApiService } from '../../api-client';
import { BrandingService } from './branding.service';

/**
 * This spec's own hand-rolled fake for the generated `BrandingApiService`, mirroring
 * `ThemeService`'s own spec -- see that spec's doc comment for why a plain object is used instead
 * of `jasmine.SpyObj`.
 */
interface FakeBrandingApiService {
  apiBrandingSettingsEffectiveGet: jasmine.Spy;
}

describe('BrandingService', () => {
  let service: BrandingService;
  let brandingApiSpy: FakeBrandingApiService;
  const originalTitle = document.title;

  beforeEach(() => {
    brandingApiSpy = {
      apiBrandingSettingsEffectiveGet: jasmine.createSpy('apiBrandingSettingsEffectiveGet'),
    };

    TestBed.configureTestingModule({
      providers: [BrandingService, { provide: BrandingApiService, useValue: brandingApiSpy }],
    });

    service = TestBed.inject(BrandingService);
  });

  afterEach(() => {
    document.title = originalTitle;
  });

  it('starts with document.title untouched until initialize() actually runs', () => {
    expect(service.siteTitle()).toBeNull();
    expect(service.logoDataUri()).toBeNull();
  });

  it("initialize() falls back to this app's own compiled-in title when the read fails (e.g. an anonymous caller pre-sign-in)", async () => {
    brandingApiSpy.apiBrandingSettingsEffectiveGet.and.returnValue(
      throwError(() => new Error('401 Unauthorized')),
    );

    await service.initialize();

    expect(document.title).toBe('Ninlil Admin');
    expect(service.siteTitle()).toBeNull();
    expect(service.logoDataUri()).toBeNull();
  });

  it('initialize() applies a real configured site title to document.title on success', async () => {
    brandingApiSpy.apiBrandingSettingsEffectiveGet.and.returnValue(
      of({ siteTitle: 'Acme Corp DNS', logoDataUri: 'data:image/png;base64,AAAA' }),
    );

    await service.initialize();

    expect(document.title).toBe('Acme Corp DNS');
    expect(service.siteTitle()).toBe('Acme Corp DNS');
    expect(service.logoDataUri()).toBe('data:image/png;base64,AAAA');
  });

  it('initialize() falls back to the compiled-in title when no custom title is configured', async () => {
    brandingApiSpy.apiBrandingSettingsEffectiveGet.and.returnValue(
      of({ siteTitle: null, logoDataUri: null }),
    );

    await service.initialize();

    expect(document.title).toBe('Ninlil Admin');
  });
});

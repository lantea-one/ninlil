import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { HasPermissionPipe } from './has-permission.pipe';

describe('HasPermissionPipe', () => {
  it("answers with AuthService.hasPermission's own answer for the given permission", () => {
    const authServiceSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authServiceSpy.hasPermission.and.callFake((permission: string) => permission === 'zones:write');
    TestBed.configureTestingModule({
      providers: [HasPermissionPipe, { provide: AuthService, useValue: authServiceSpy }],
    });
    const pipe = TestBed.inject(HasPermissionPipe);

    expect(pipe.transform('zones:write')).toBeTrue();
    expect(pipe.transform('users:write')).toBeFalse();
  });
});

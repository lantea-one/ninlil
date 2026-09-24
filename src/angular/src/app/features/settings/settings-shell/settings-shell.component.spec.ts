import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { SettingsShellComponent } from './settings-shell.component';

/** A stand-in for whichever settings page the shell's own outlet would show. */
@Component({ template: '' })
class SettingsPageStubComponent {}

describe('SettingsShellComponent', () => {
  async function createComponent(permissions: readonly string[], url?: string) {
    const authServiceSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authServiceSpy.hasPermission.and.callFake((permission: string) =>
      permissions.includes(permission),
    );

    await TestBed.configureTestingModule({
      imports: [SettingsShellComponent],
      providers: [
        provideRouter([{ path: 'settings/:page', component: SettingsPageStubComponent }]),
        { provide: AuthService, useValue: authServiceSpy },
      ],
    }).compileComponents();

    if (url) await TestBed.inject(Router).navigateByUrl(url);

    const fixture = TestBed.createComponent(SettingsShellComponent);
    fixture.detectChanges();
    return fixture;
  }

  /** Flattens every visible group's own tabs into one ordered list, for assertions that don't care about grouping. */
  function flatTabLabels(
    fixture: ReturnType<typeof TestBed.createComponent<SettingsShellComponent>>,
  ) {
    return fixture.componentInstance
      .visibleGroups()
      .flatMap((group) => group.tabs)
      .map((tab) => tab.label);
  }

  it('shows only the tabs whose own permission the current session holds', async () => {
    const fixture = await createComponent(['presets:read', 'dnscookies:read']);

    expect(flatTabLabels(fixture)).toEqual(['DNS Security Presets', 'DNS Cookies']);
  });

  it('shows every tab when every permission is held', async () => {
    const fixture = await createComponent([
      'presets:read',
      'ratelimiting:read',
      'eventbroker:read',
      'dnscookies:read',
      'querylog:read',
      'reverseproxy:read',
      'appearance:read',
    ]);

    expect(flatTabLabels(fixture).length).toBe(7);
  });

  it('shows no groups when no settings permission is held', async () => {
    const fixture = await createComponent([]);

    expect(fixture.componentInstance.visibleGroups().length).toBe(0);
  });

  it('drops a group entirely once every one of its own tabs is filtered out, rather than rendering an empty category', async () => {
    const fixture = await createComponent(['presets:read']);

    // 'DNS Security Presets' is the sole tab in the 'Overview' group -- holding only its own
    // permission must leave every other group (which grants no tab at all here) absent from the
    // result, not present with an empty `tabs` array.
    expect(fixture.componentInstance.visibleGroups().map((group) => group.label)).toEqual([
      'Overview',
    ]);
  });

  it('shows the DNSSEC Policy and Web Listener tabs, in their own groups own relative order, whose own permission the current session holds', async () => {
    const fixture = await createComponent(['dnssecpolicysettings:read', 'weblistensettings:read']);

    expect(flatTabLabels(fixture)).toEqual(['DNSSEC Policy', 'Web Listener']);
  });

  it('shows the Admin Login Rate Limit tab only when adminloginratelimit:read is held', async () => {
    const fixture = await createComponent(['adminloginratelimit:read']);

    expect(flatTabLabels(fixture)).toEqual(['Admin Login Rate Limit']);
  });

  it('shows the DNSSEC Key Rollover tab only when dnsseckeyrolloversettings:read is held', async () => {
    const fixture = await createComponent(['dnsseckeyrolloversettings:read']);

    expect(flatTabLabels(fixture)).toEqual(['DNSSEC Key Rollover']);
  });

  function readOnlyNotice(
    fixture: ReturnType<typeof TestBed.createComponent<SettingsShellComponent>>,
  ) {
    return (fixture.nativeElement as HTMLElement).querySelector('c-alert');
  }

  it("explains that the showing page is read-only when its write permission isn't held", async () => {
    const fixture = await createComponent(['smtpsettings:read'], '/settings/smtp');

    expect(fixture.componentInstance.missingWritePermission()).toBe('smtpsettings:write');
    expect(readOnlyNotice(fixture)?.textContent).toContain('smtpsettings:write');
  });

  it('shows no read-only notice when the showing page may be changed', async () => {
    const fixture = await createComponent(
      ['smtpsettings:read', 'smtpsettings:write'],
      '/settings/smtp',
    );

    expect(fixture.componentInstance.missingWritePermission()).toBeNull();
    expect(readOnlyNotice(fixture)).toBeNull();
  });

  it("uses the storage page's own manage permission rather than a storage:write that doesn't exist", async () => {
    const fixture = await createComponent(['storage:read'], '/settings/storage');

    expect(fixture.componentInstance.missingWritePermission()).toBe('storage:manage');
  });
});

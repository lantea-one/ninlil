import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Clipboard } from '@angular/cdk/clipboard';
import {
  RevealSecretDialogComponent,
  RevealSecretDialogData,
} from './reveal-secret-dialog.component';

/**
 * These tests cover the reveal-once dialog: rendering one or several secrets, copying (with the
 * per-row "copied" mark) and closing. TestBed with a spy `Clipboard`.
 */
describe('RevealSecretDialogComponent', () => {
  let fixture: ComponentFixture<RevealSecretDialogComponent>;
  let clipboardSpy: jasmine.SpyObj<Clipboard>;

  async function createWith(data: RevealSecretDialogData, visible = true): Promise<void> {
    clipboardSpy = jasmine.createSpyObj('Clipboard', ['copy']);

    await TestBed.configureTestingModule({
      imports: [RevealSecretDialogComponent],
      providers: [{ provide: Clipboard, useValue: clipboardSpy }],
    }).compileComponents();

    fixture = TestBed.createComponent(RevealSecretDialogComponent);
    fixture.componentRef.setInput('data', data);
    fixture.componentRef.setInput('visible', visible);
    fixture.detectChanges();
  }

  it('renders the supplied title, message, and a single secret row while visible', async () => {
    await createWith({
      title: 'TSIG key created',
      message: 'This secret is shown once and never again.',
      secrets: [{ label: 'Shared secret', value: 'c29tZS1zZWNyZXQ=' }],
    });

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('TSIG key created');
    expect(text).toContain('This secret is shown once and never again.');
    expect(text).toContain('Shared secret');
    expect(text).toContain('c29tZS1zZWNyZXQ=');
  });

  it('renders one row per secret when several are supplied', async () => {
    await createWith({
      title: 'Credential set created',
      message: 'These values are shown once and never again.',
      secrets: [
        { label: 'Access Key ID', value: 'AKIAEXAMPLE' },
        { label: 'Secret Access Key', value: 'super-secret-value' },
      ],
    });

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('.secret-row');
    expect(rows.length).toBe(2);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('AKIAEXAMPLE');
    expect(text).toContain('super-secret-value');
  });

  it('copy() copies the exact secret value to the clipboard and briefly marks it copied', async () => {
    await createWith({
      title: 'Credential set created',
      message: 'Shown once.',
      secrets: [
        { label: 'Access Key ID', value: 'AKIAEXAMPLE' },
        { label: 'Secret Access Key', value: 'super-secret-value' },
      ],
    });

    fixture.componentInstance.copy({ label: 'Secret Access Key', value: 'super-secret-value' });

    expect(clipboardSpy.copy).toHaveBeenCalledWith('super-secret-value');
    expect(fixture.componentInstance.copiedLabel()).toBe('Secret Access Key');
  });

  it('copy() does not confuse two different rows -- only the clicked row is marked copied', async () => {
    await createWith({
      title: 'Credential set created',
      message: 'Shown once.',
      secrets: [
        { label: 'Access Key ID', value: 'AKIAEXAMPLE' },
        { label: 'Secret Access Key', value: 'super-secret-value' },
      ],
    });

    fixture.componentInstance.copy({ label: 'Access Key ID', value: 'AKIAEXAMPLE' });

    expect(clipboardSpy.copy).toHaveBeenCalledWith('AKIAEXAMPLE');
    expect(fixture.componentInstance.copiedLabel()).toBe('Access Key ID');
    expect(fixture.componentInstance.copiedLabel()).not.toBe('Secret Access Key');
  });

  it('dismiss() emits visibleChange(false) and closed', async () => {
    await createWith({
      title: 'TSIG key created',
      message: 'Shown once.',
      secrets: [{ label: 'Shared secret', value: 'c29tZS1zZWNyZXQ=' }],
    });

    const visibleChangeSpy = jasmine.createSpy('visibleChange');
    const closedSpy = jasmine.createSpy('closed');
    fixture.componentInstance.visibleChange.subscribe(visibleChangeSpy);
    fixture.componentInstance.closed.subscribe(closedSpy);

    fixture.componentInstance.dismiss();

    expect(visibleChangeSpy).toHaveBeenCalledWith(false);
    expect(closedSpy).toHaveBeenCalled();
  });
});

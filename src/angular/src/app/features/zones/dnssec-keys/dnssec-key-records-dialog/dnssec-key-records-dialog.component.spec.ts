import { TestBed } from '@angular/core/testing';
import { Clipboard } from '@angular/cdk/clipboard';
import { DnssecAlgorithmEnum, DnssecKeyDetail, DnssecKeyRoleEnum } from '../../../../api-client';
import { DnssecKeyRecordsDialogComponent } from './dnssec-key-records-dialog.component';

const DIGEST = '186EA634D4CF62F2671F3C757F507F8D3FE985FE431A1181B661F8EED27A59CA';

const KSK: DnssecKeyDetail = {
  id: 'key-1',
  role: DnssecKeyRoleEnum.Ksk,
  algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
  keyTag: 12345,
  publishedAtUtc: '2026-08-01T09:00:00Z',
  dnsKeyRecord: 'example.com. IN DNSKEY 257 3 13 AAEC',
  dsRecord: `example.com. IN DS 12345 13 2 ${DIGEST}`,
  dsDigest: DIGEST,
};

describe('DnssecKeyRecordsDialogComponent', () => {
  let clipboardSpy: { copy: jasmine.Spy };

  async function createComponent(key: DnssecKeyDetail) {
    clipboardSpy = jasmine.createSpyObj('Clipboard', ['copy']);

    await TestBed.configureTestingModule({
      imports: [DnssecKeyRecordsDialogComponent],
      providers: [{ provide: Clipboard, useValue: clipboardSpy }],
    }).compileComponents();

    const fixture = TestBed.createComponent(DnssecKeyRecordsDialogComponent);
    fixture.componentRef.setInput('key', key);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    return fixture;
  }

  it('shows a KSK DS record, its parts and its DNSKEY', async () => {
    const fixture = await createComponent(KSK);

    expect(fixture.componentInstance.parentFields().map((field) => field.label)).toEqual([
      'DS record',
      'Key tag',
      'Algorithm',
      'Digest type',
      'Digest',
    ]);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(`example.com. IN DS 12345 13 2 ${DIGEST}`);
    expect(text).toContain('example.com. IN DNSKEY 257 3 13 AAEC');
  });

  it('copies the bare number for the algorithm, not its name', async () => {
    const fixture = await createComponent(KSK);
    const algorithm = fixture.componentInstance
      .parentFields()
      .find((field) => field.label === 'Algorithm')!;

    fixture.componentInstance.copy(algorithm);

    expect(clipboardSpy.copy).toHaveBeenCalledWith('13');
    expect(fixture.componentInstance.copiedLabel()).toBe('Algorithm');
  });

  it('copies the whole DS record from its copy button', async () => {
    const fixture = await createComponent(KSK);

    const button: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[aria-label="Copy DS record to clipboard"]',
    )!;
    button.click();

    expect(clipboardSpy.copy).toHaveBeenCalledWith(`example.com. IN DS 12345 13 2 ${DIGEST}`);
  });

  it('shows only the DNSKEY for a ZSK, which the parent never references', async () => {
    const fixture = await createComponent({
      ...KSK,
      role: DnssecKeyRoleEnum.Zsk,
      dnsKeyRecord: 'example.com. IN DNSKEY 256 3 13 AAEC',
      dsRecord: null,
      dsDigest: null,
    });

    expect(fixture.componentInstance.parentFields()).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('example.com. IN DNSKEY 256 3 13 AAEC');
    expect(
      fixture.nativeElement.querySelector('[aria-label="Copy DS record to clipboard"]'),
    ).toBeNull();
  });
});

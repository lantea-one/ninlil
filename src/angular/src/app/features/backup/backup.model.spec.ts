import { buildBackupFileName, triggerBlobDownload } from './backup.model';

describe('buildBackupFileName', () => {
  it('builds a name matching the backend own ninlil-backup-{yyyyMMdd-HHmmss}.ltbk convention', () => {
    const fixedDate = new Date(2026, 7, 31, 9, 5, 3);

    expect(buildBackupFileName(fixedDate)).toBe('ninlil-backup-20260831-090503.ltbk');
  });

  it('zero-pads every date/time component below 10', () => {
    const fixedDate = new Date(2027, 0, 2, 3, 4, 5);

    expect(buildBackupFileName(fixedDate)).toBe('ninlil-backup-20270102-030405.ltbk');
  });
});

describe('triggerBlobDownload', () => {
  it('creates an object URL, clicks a temporary anchor, then revokes the URL', () => {
    const blob = new Blob(['archive bytes'], { type: 'application/octet-stream' });
    const objectUrl = 'blob:fake-url';

    spyOn(URL, 'createObjectURL').and.returnValue(objectUrl);
    const revokeSpy = spyOn(URL, 'revokeObjectURL');
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

    triggerBlobDownload(blob, 'archive.ltbk');

    expect(URL.createObjectURL).toHaveBeenCalledWith(blob);
    expect(clickSpy).toHaveBeenCalled();
    expect(revokeSpy).toHaveBeenCalledWith(objectUrl);
  });

  it('sets the anchor own download attribute to the supplied file name', () => {
    const blob = new Blob(['archive bytes']);
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake-url');
    spyOn(URL, 'revokeObjectURL');

    let capturedDownloadAttribute = '';
    spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (
      this: HTMLAnchorElement,
    ): void {
      capturedDownloadAttribute = this.download;
    });

    triggerBlobDownload(blob, 'my-archive.ltbk');

    expect(capturedDownloadAttribute).toBe('my-archive.ltbk');
  });
});

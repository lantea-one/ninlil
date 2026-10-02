import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BackupDownloadService } from './backup-download.service';

/**
 * These tests cover the archive download: the request and blob response, and reading error
 * messages from JSON, plain-text and missing bodies. `HttpTestingController` with a real
 * `HttpClient`.
 */
describe('BackupDownloadService', () => {
  let service: BackupDownloadService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(BackupDownloadService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('posts the passphrase to /api/backup and resolves with the raw archive blob', async () => {
    const resultPromise = service.downloadBackup('a-strong-test-passphrase');

    const request = httpMock.expectOne('/api/backup');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ passphrase: 'a-strong-test-passphrase' });
    expect(request.request.responseType).toBe('blob');

    const archiveBlob = new Blob(['archive bytes'], { type: 'application/octet-stream' });
    request.flush(archiveBlob);

    expect(await resultPromise).toBe(archiveBlob);
  });

  it('reads the detail field out of a JSON ProblemDetails blob error body', async () => {
    const resultPromise = service.downloadBackup('short');

    const request = httpMock.expectOne('/api/backup');
    const errorBody = new Blob([JSON.stringify({ detail: 'Passphrase too short.' })], {
      type: 'application/problem+json',
    });
    request.flush(errorBody, { status: 400, statusText: 'Bad Request' });

    await expectAsync(resultPromise).toBeRejectedWithError('Passphrase too short.');
  });

  it('falls back to the raw blob text when the error body is not JSON', async () => {
    const resultPromise = service.downloadBackup('a-strong-test-passphrase');

    const request = httpMock.expectOne('/api/backup');
    const errorBody = new Blob(['A plain-text failure message.'], { type: 'text/plain' });
    request.flush(errorBody, { status: 500, statusText: 'Internal Server Error' });

    await expectAsync(resultPromise).toBeRejectedWithError('A plain-text failure message.');
  });

  it('falls back to a generic message for a network failure with no body at all', async () => {
    const resultPromise = service.downloadBackup('a-strong-test-passphrase');

    const request = httpMock.expectOne('/api/backup');
    request.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    await expectAsync(resultPromise).toBeRejectedWithError(
      'Could not reach the server. Check your connection and try again.',
    );
  });

  it('is provided as a singleton so every consumer shares the same HttpClient wiring', () => {
    expect(TestBed.inject(BackupDownloadService)).toBe(service);
    expect(TestBed.inject(HttpClient)).toBeTruthy();
  });
});

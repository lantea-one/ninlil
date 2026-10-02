import {
  MAX_LOGO_SIZE_BYTES,
  readFileAsDataUri,
  validateLogoFile,
} from './branding-settings.model';

/** These tests cover logo file validation and reading a file into a data URI. Plain unit tests. */
describe('branding-settings.model', () => {
  describe('validateLogoFile', () => {
    it('accepts a real image file within the size bound', () => {
      const file = new File(['x'.repeat(1024)], 'logo.png', { type: 'image/png' });

      expect(validateLogoFile(file)).toBeNull();
    });

    it('rejects a non-image file', () => {
      const file = new File(['hello'], 'notes.txt', { type: 'text/plain' });

      const rejection = validateLogoFile(file);

      expect(rejection).toContain('notes.txt');
      expect(rejection).toContain('not an image');
    });

    it('rejects a file larger than the accepted size', () => {
      const oversized = new Uint8Array(MAX_LOGO_SIZE_BYTES + 1);
      const file = new File([oversized], 'big-logo.png', { type: 'image/png' });

      const rejection = validateLogoFile(file);

      expect(rejection).toContain('too large');
    });
  });

  describe('readFileAsDataUri', () => {
    it('resolves with a real data: URI carrying the file contents', async () => {
      const file = new File(['hello'], 'greeting.txt', { type: 'text/plain' });

      const dataUri = await readFileAsDataUri(file);

      expect(dataUri.startsWith('data:text/plain;base64,')).toBeTrue();
      // "hello" is "aGVsbG8=" in base64, so this checks the file content, not just the URI shape.
      expect(dataUri).toContain('aGVsbG8=');
    });
  });
});

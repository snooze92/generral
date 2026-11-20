import { describe, expect, it } from 'vitest';
import translate from 'Utilities/String/translate';

describe('translate', () => {
  it('returns the bundled English string', () => {
    expect(translate('MediaManagement')).toBe('Media Management');
  });

  it('replaces tokens, including appName', () => {
    expect(translate('VersionNumber', { version: '1.2.3' })).toBe(
      'Version 1.2.3'
    );
    expect(translate('RestartApp')).toBe('Restart Generral');
  });

  it('falls back to the key for unknown strings', () => {
    expect(translate('NotARealKey')).toBe('NotARealKey');
  });
});

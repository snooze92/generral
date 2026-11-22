import { describe, expect, it } from 'vitest';
import { toTextAreaValue } from './textAreaValue';

describe('toTextAreaValue', () => {
  it('renders objects and lists as text that parses back to the same structure', () => {
    const requests = [{ url: 'https://x.org/e', headers: { a: '1' } }];
    const fields = { key: { path: '$.id' }, title: { path: '$.name' } };

    for (const value of [requests, fields]) {
      const text = toTextAreaValue(value);
      expect(typeof text).toBe('string');
      expect(text).not.toContain('[object Object]');
      expect(JSON.parse(text as string)).toEqual(value);
    }
  });

  it('keeps strings and empty values as is', () => {
    expect(toTextAreaValue('requests: []')).toBe('requests: []');
    expect(toTextAreaValue(undefined)).toBeUndefined();
    expect(toTextAreaValue(null)).toBeNull();
  });
});

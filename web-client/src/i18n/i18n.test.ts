import { describe, expect, it } from 'vitest';
import { format, lookup } from './index';
import { WEB_STRINGS } from './web';

describe('i18n', () => {
  it('formats {n} placeholders', () => {
    expect(format('Lv.{0} · {1}', [5, 'x'])).toBe('Lv.5 · x');
    expect(format('{0} {2}', ['a'])).toBe('a {2}');
  });
  it('falls back vi -> web strings -> en -> key', () => {
    const tables = { en: { 'a.b': 'Hello {0}' }, vi: {} };
    expect(lookup(tables, 'vi', 'a.b', ['X'])).toBe('Hello X');
    expect(lookup(tables, 'vi', 'web.save')).toBe(WEB_STRINGS.vi['web.save']);
    expect(lookup(tables, 'en', 'nope.key')).toBe('nope.key');
  });
  it('every web string has both languages', () => {
    expect(Object.keys(WEB_STRINGS.en).sort()).toEqual(Object.keys(WEB_STRINGS.vi).sort());
    for (const v of Object.values(WEB_STRINGS.vi)) expect(v.trim().length).toBeGreaterThan(0);
  });
});

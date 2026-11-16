import { describe, expect, it } from 'vitest';
import { pushAvailability, urlB64ToUint8Array } from './push';

describe('web push client rules', () => {
  const base = { supported: true, ios: false, standalone: false, configured: true, permission: 'default' as NotificationPermission };
  it('iPhone Safari tab must install to the Home Screen first; installed iOS app may ask', () => {
    expect(pushAvailability({ ...base, ios: true })).toBe('ios-install-first');
    expect(pushAvailability({ ...base, ios: true, standalone: true })).toBe('ask');
  });
  it('unsupported, server not configured and blocked permission are reported, never faked', () => {
    expect(pushAvailability({ ...base, supported: false })).toBe('unsupported');
    expect(pushAvailability({ ...base, configured: false })).toBe('not-configured');
    expect(pushAvailability({ ...base, permission: 'denied' })).toBe('denied');
    expect(pushAvailability(base)).toBe('ask');
  });
  it('decodes the VAPID public key (base64url, no padding) to 65 raw bytes', () => {
    const key = 'BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8';
    const b = urlB64ToUint8Array(key);
    expect(b.length).toBe(65);
    expect(b[0]).toBe(4);
  });
});

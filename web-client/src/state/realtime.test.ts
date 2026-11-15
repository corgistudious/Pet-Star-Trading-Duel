import { describe, expect, it } from 'vitest';
import { backoff, wsUrl } from './realtime';
import { belongsTo } from '../screens/Chat';

describe('realtime helpers', () => {
  it('builds ws/wss urls from the api origin', () => {
    expect(wsUrl('https://api.example.com', 'abc')).toBe('wss://api.example.com/ws?ticket=abc');
    expect(wsUrl('', 't+1', { protocol: 'http:', host: 'localhost:5173' })).toBe('ws://localhost:5173/ws?ticket=t%2B1');
  });
  it('backs off exponentially with a cap', () => {
    expect(backoff(0, () => 0.5)).toBe(1000);
    expect(backoff(3, () => 0.5)).toBe(8000);
    expect(backoff(20, () => 0.5)).toBe(30000);
  });
  it('routes chat pushes to the open channel only', () => {
    expect(belongsTo('world', 'world', 5, 1)).toBe(true);
    expect(belongsTo('world', 'guild:3', 5, 1)).toBe(false);
    expect(belongsTo('guild', 'guild:3', 5, 1)).toBe(true);
    expect(belongsTo('dm:5', 'dm:10:11', 5, 1)).toBe(true);
    expect(belongsTo('dm:5', 'dm:10:12', 6, 1)).toBe(false);
  });
});

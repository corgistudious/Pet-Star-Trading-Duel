import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, api, newIdempotencyKey, qs, tokens } from './client';

afterEach(() => { vi.restoreAllMocks(); tokens.clear(); });

describe('api client', () => {
  it('builds query strings skipping empty values', () => {
    expect(qs({ a: 1, b: '', c: undefined, d: 'x y' })).toBe('?a=1&d=x+y');
    expect(qs({})).toBe('');
  });
  it('idempotency keys match the server format', () => {
    const k = newIdempotencyKey();
    expect(k).toMatch(/^[A-Za-z0-9_-]{8,64}$/);
    expect(newIdempotencyKey()).not.toBe(k);
  });
  it('maps error bodies to ApiError codes and sends the idempotency header', async () => {
    const f = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ code: 'insufficient_mini_green_bean' }), { status: 409 }));
    await expect(api.post('/api/packs/purchase', { packTypeId: 'basic_pack' }, { idem: 'key12345' })).rejects.toMatchObject({ code: 'insufficient_mini_green_bean', status: 409 });
    const init = f.mock.calls[0][1] as RequestInit;
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('key12345');
  });
  it('refreshes once on 401 and retries with the new token', async () => {
    tokens.apply({ uid: 1, accessToken: 'old', accessTokenExpiresAt: new Date(Date.now() + 600_000).toISOString(), refreshToken: 'r1', emailVerified: true });
    const f = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response('{}', { status: 401 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ uid: 1, accessToken: 'new', accessTokenExpiresAt: new Date(Date.now() + 600_000).toISOString(), refreshToken: 'r2', emailVerified: true })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ok: true })));
    expect(await api.get('/api/player')).toEqual({ ok: true });
    expect(((f.mock.calls[2][1] as RequestInit).headers as Record<string, string>).Authorization).toBe('Bearer new');
    expect(tokens.refresh).toBe('r2');
  });
  it('network failures become ApiError(network)', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'));
    const e = (await api.get("/x").catch((x: unknown) => x)) as ApiError;
    expect(e).toBeInstanceOf(ApiError);
    expect(e.code).toBe('network');
  });
});

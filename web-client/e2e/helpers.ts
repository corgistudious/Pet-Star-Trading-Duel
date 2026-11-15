import type { APIRequestContext, BrowserContext } from '@playwright/test';

export const API = process.env.PETSTAR_E2E_API ?? 'http://127.0.0.1:5080';
const rnd = () => Math.random().toString(36).slice(2, 8);
const key = () => 'e2e' + Math.random().toString(36).slice(2) + Date.now().toString(36);

/** Creates a real player through the API (register → PS-1 → open 3 starter packs → default 3+2 deck). */
export async function apiPlayer(request: APIRequestContext, name = 'Vp' + rnd()) {
  const reg = await (await request.post(`${API}/api/auth/register`, { data: { email: `${name}-${rnd()}@e2e.local`, password: 'Passw0rd1', displayName: name } })).json();
  const enter = await (await request.post(`${API}/api/servers/1/enter`, { data: {}, headers: { Authorization: `Bearer ${reg.accessToken}` } })).json();
  const auth = { Authorization: `Bearer ${enter.accessToken}` };
  for (let i = 0; i < 3; i++) await request.post(`${API}/api/packs/open`, { data: { packTypeId: 'basic_pack' }, headers: { ...auth, 'Idempotency-Key': key() } });
  const cards = (await (await request.get(`${API}/api/cards?pageSize=50&sort=power`, { headers: auth })).json()).items as { id: number; familyId: string }[];
  const pick: typeof cards = [];
  for (const c of cards) if (pick.length < 5 && pick.filter((x) => x.familyId === c.familyId).length < 2) pick.push(c);
  await request.post(`${API}/api/decks`, { data: { name: 'Main', isDefault: true, slots: pick.map((c, i) => ({ position: i, cardId: c.id })) }, headers: auth });
  return { refresh: reg.refreshToken as string, uid: reg.uid as number, auth };
}

/** Signs the browser context in with that player's refresh token (set once; the app rotates it afterwards). */
export async function signIn(ctx: BrowserContext, refresh: string) {
  await ctx.addInitScript(([r]) => {
    if (!localStorage.getItem('petstar.refresh')) { localStorage.setItem('petstar.refresh', r); localStorage.setItem('petstar.server', '1'); }
  }, [refresh]);
}

export const PORTRAIT = { width: 390, height: 844 };
export const LANDSCAPE = { width: 844, height: 390 };

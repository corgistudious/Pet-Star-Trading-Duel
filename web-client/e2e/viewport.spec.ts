// Official viewport rules on a phone (Chromium iPhone 13 emulation; real WebKit/Safari is not available here):
// Portrait 9:16 outside duels, Landscape 16:9 only in PvE/PvP, pure-black rotate overlay when held wrong.
import { expect, test, type Page } from '@playwright/test';
import { LANDSCAPE, PORTRAIT, apiPlayer, signIn } from './helpers';

async function expectBoardFits(page: Page) {
  const vp = page.viewportSize()!;
  for (const side of ['mine', 'enemy']) {
    await expect(page.locator(`.side.${side} .brow.three .slot`)).toHaveCount(3);
    await expect(page.locator(`.side.${side} .brow.two .slot`)).toHaveCount(2);
  }
  const boxes = await page.locator('.board .slot, [data-testid=end-turn]').evaluateAll((els) => els.map((e) => ({ ...e.getBoundingClientRect().toJSON(), slot: e.classList.contains('slot') })));
  for (const b of boxes) {
    expect(b.left).toBeGreaterThanOrEqual(-0.5);
    expect(b.top).toBeGreaterThanOrEqual(-0.5);
    expect(b.right).toBeLessThanOrEqual(vp.width + 0.5);
    expect(b.bottom).toBeLessThanOrEqual(vp.height + 0.5);
    if (b.slot) expect(Math.abs(b.width - b.height)).toBeLessThan(1.5); // square slots: no distortion
  }
  const stage = await page.getByTestId('battle-stage').boundingBox();
  expect(Math.abs(stage!.width / stage!.height - 16 / 9)).toBeLessThan(0.02);
}

async function expectBlackOverlay(page: Page, to: 'landscape' | 'portrait') {
  const o = page.getByTestId('rotate-overlay');
  await expect(o).toBeVisible();
  await expect(o).toHaveClass(new RegExp(`to-${to}`));
  const style = await o.evaluate((el) => { const s = getComputedStyle(el); const r = el.getBoundingClientRect(); return { bg: s.backgroundColor, img: s.backgroundImage, r: r.toJSON() }; });
  expect(style.bg).toBe('rgb(0, 0, 0)');
  expect(style.img).toBe('none'); // no gradient / artwork
  const vp = page.viewportSize()!;
  expect(style.r.left).toBeLessThanOrEqual(0); expect(style.r.top).toBeLessThanOrEqual(0);
  expect(style.r.right).toBeGreaterThanOrEqual(vp.width); expect(style.r.bottom).toBeGreaterThanOrEqual(vp.height);
  await expect(o.getByText(/Xoay (ngang|dọc)/)).toBeVisible();
  await expect(o.getByText(/Rotate your phone/)).toBeVisible();
  // nothing behind it can be hit: the point under every corner and the centre belongs to the overlay
  const hits = await page.evaluate(({ w, h }) => [[w / 2, h / 2], [5, 5], [w - 5, h - 5]].map(([x, y]) => !!document.elementFromPoint(x, y)?.closest('[data-testid=rotate-overlay]')), { w: vp.width, h: vp.height });
  expect(hits).toEqual([true, true, true]);
}

test.describe('phone viewport rules', () => {
  test('portrait → enter duel shows the black overlay; rotate to play; rotate many times; leave → portrait lobby', async ({ page, context, request }) => {
    const p = await apiPlayer(request);
    await signIn(context, p.refresh);
    await page.setViewportSize(PORTRAIT);
    await page.goto('/');
    await expect(page.getByTestId('bal-MINI_GREEN_BEAN')).toBeVisible(); // session ready (token rotation finished)
    await expect(page.getByTestId('portrait-frame')).toBeVisible();
    await expect(page.getByTestId('rotate-overlay')).toHaveCount(0);

    await page.goto('/duel');
    await page.getByTestId('fight-c1_s1').click();
    await expect(page).toHaveURL(/\/battle\//);
    await expectBlackOverlay(page, 'landscape');
    expect(await page.getByTestId('battle-stage').getAttribute('inert')).toBe(''); // board unreachable (incl. keyboard focus)

    await page.setViewportSize(LANDSCAPE);
    await expect(page.getByTestId('rotate-overlay')).toHaveCount(0);
    await expectBoardFits(page);

    for (let i = 0; i < 4; i++) {
      await page.setViewportSize(PORTRAIT);
      await expectBlackOverlay(page, 'landscape');
      await page.setViewportSize(LANDSCAPE);
      await expect(page.getByTestId('rotate-overlay')).toHaveCount(0);
      await expectBoardFits(page);
    }
    // the duel is still playable after all the rotations
    await page.getByTestId('end-turn').click();
    await expect(page.getByText(/Round 2|Victory|Defeat|Draw/)).toBeVisible({ timeout: 20_000 });

    // leave the duel while still landscape: portrait rule returns (phone asks to rotate back)
    await page.getByRole('button', { name: 'back' }).click();
    await expect(page).toHaveURL(/\/duel$/);
    await expect(page.getByTestId('portrait-frame')).toBeVisible();
    await expectBlackOverlay(page, 'portrait');
    expect(await page.evaluate(() => document.documentElement.dataset.mode)).toBe('portrait');
    await page.setViewportSize(PORTRAIT);
    await expect(page.getByTestId('rotate-overlay')).toHaveCount(0);
    await page.getByRole('link', { name: /Home/ }).first().click();
    await expect(page.getByRole('button', { name: /Open pack|Get packs/ })).toBeVisible();
  });

  test('opened while already landscape: lobby asks for portrait, duel starts without overlay', async ({ page, context, request }) => {
    const p = await apiPlayer(request);
    await signIn(context, p.refresh);
    await page.setViewportSize(LANDSCAPE);
    await page.goto('/duel');
    await expect(page.getByTestId('fight-c1_s1')).toBeAttached(); // signed in (content is inert behind the overlay)
    await expectBlackOverlay(page, 'portrait');
    await page.setViewportSize(PORTRAIT);
    await expect(page.getByTestId('rotate-overlay')).toHaveCount(0);
    await page.getByTestId('fight-c1_s1').click();
    await expect(page).toHaveURL(/\/battle\//);
    await page.setViewportSize(LANDSCAPE);
    // reload directly into the duel while landscape (e.g. app reopened mid-battle)
    await page.reload();
    await expect(page.locator('.board .slot.unit').first()).toBeAttached();
    await expect(page.getByTestId('battle-stage')).toBeVisible();
    await expect(page.getByTestId('rotate-overlay')).toHaveCount(0);
    await expectBoardFits(page);
  });

  test('keyboard open/close keeps the chat input visible and never triggers an overlay', async ({ page, context, request }) => {
    const p = await apiPlayer(request);
    await signIn(context, p.refresh);
    await page.setViewportSize(PORTRAIT);
    await page.goto('/chat');
    await expect(page.locator('.chat-input input')).toBeEnabled();
    const input = page.locator('.chat-input input');
    await input.click();
    // Android-style keyboard: the viewport shrinks. (iOS shrinks only visualViewport: covered by unit tests.)
    await page.setViewportSize({ width: 390, height: 470 });
    await expect(page.getByTestId('rotate-overlay')).toHaveCount(0);
    const box = (await input.boundingBox())!;
    expect(box.y + box.height).toBeLessThanOrEqual(470);
    await input.fill('typing with the keyboard open');
    await expect(page.getByRole('button', { name: 'Send' })).toBeInViewport();
    await page.setViewportSize(PORTRAIT); // keyboard closed
    await expect(page.locator('.bottomnav')).toBeInViewport();
    await expect(page.getByTestId('rotate-overlay')).toHaveCount(0);
  });

  test('PWA: manifest, service worker, offline shell and update-safe first install', async ({ page, context, request }) => {
    const manifest = await (await request.get('/manifest.webmanifest')).json();
    expect(manifest).toMatchObject({ display: 'standalone', orientation: 'portrait', background_color: '#000000', start_url: '/' });
    expect(manifest.icons.some((i: { purpose: string }) => i.purpose === 'maskable')).toBe(true);
    for (const i of manifest.icons) expect((await request.get(i.src)).ok()).toBe(true);

    const p = await apiPlayer(request);
    await signIn(context, p.refresh);
    await page.setViewportSize(PORTRAIT);
    await page.goto('/');
    await expect(page.getByTestId('bal-MINI_GREEN_BEAN')).toBeVisible();
    // first install must not reload the page (would wipe forms / interrupt a duel)
    await page.evaluate(() => { (window as unknown as { marker: number }).marker = 42; });
    await page.evaluate(() => navigator.serviceWorker.ready);
    await page.waitForTimeout(800);
    expect(await page.evaluate(() => (window as unknown as { marker?: number }).marker)).toBe(42);

    // offline: the cached shell still opens and says the game needs the internet (no fake data)
    await page.reload();
    await expect.poll(() => page.evaluate(() => !!navigator.serviceWorker.controller)).toBe(true);
    await context.setOffline(true);
    await page.reload();
    await expect(page.getByTestId('portrait-frame')).toBeVisible();
    await expect(page.getByText(/Offline: Pet Star needs an internet connection/)).toBeVisible();
    await expect(page.getByText(/Cannot reach the Pet Star server/)).toBeVisible(); // session kept, not logged out
    await context.setOffline(false);
    await expect(page.getByTestId('bal-MINI_GREEN_BEAN')).toBeVisible({ timeout: 20_000 }); // back online: resumes
  });
});

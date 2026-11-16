// Web Push in the browser. Headless Chromium cannot create a real push subscription (it needs Google's push
// service), so the subscription itself is covered by backend tests (RFC 8291 vector, VAPID, delivery rules).
// Here: settings states, and the service worker turning a delivered push into a notification + tap navigation.
import { expect, test } from '@playwright/test';
import { apiPlayer, signIn } from './helpers';

test('iPhone Safari tab: notifications need the Home Screen app first (no permission prompt is shown)', async ({ page, context, request }) => {
  const p = await apiPlayer(request);
  await signIn(context, p.refresh);
  await page.goto('/');
  await expect(page.getByTestId('bal-MINI_GREEN_BEAN')).toBeVisible();
  await page.goto('/settings');
  await expect(page.getByTestId('push-settings')).toContainText('Add to Home Screen');
  await expect(page.getByTestId('push-toggle')).toHaveCount(0);
});

test('service worker shows a delivered push and a tapped notification opens its screen', async ({ page, context, request, browserName }) => {
  test.skip(browserName !== 'chromium', 'uses Chrome DevTools Protocol');
  const p = await apiPlayer(request);
  await signIn(context, p.refresh);
  await context.grantPermissions(['notifications']);
  await page.goto('/');
  await expect(page.getByTestId('bal-MINI_GREEN_BEAN')).toBeVisible();
  await page.evaluate(() => navigator.serviceWorker.ready);

  const cdp = await context.newCDPSession(page);
  const regId = new Promise<string>((resolve) => {
    cdp.on('ServiceWorker.workerRegistrationUpdated', (e: { registrations: { registrationId: string; scopeURL: string }[] }) => {
      const r = e.registrations.find((x) => x.scopeURL.startsWith('http://localhost:4173/'));
      if (r) resolve(r.registrationId);
    });
  });
  await cdp.send('ServiceWorker.enable');
  const registrationId = await regId;
  const payload = { title: 'Card sold', body: 'Your Emberkit sold for 1,234 Mini Green Bean.', url: '/market', tag: 'market_sold', type: 'market_sold', server: 'PS-1' };
  await cdp.send('ServiceWorker.deliverPushMessage', { origin: 'http://localhost:4173', registrationId, data: JSON.stringify(payload) });

  await expect.poll(async () => page.evaluate(async () => {
    const reg = await navigator.serviceWorker.ready;
    return (await reg.getNotifications()).map((n) => ({ title: n.title, body: n.body, url: (n.data as { url: string }).url, tag: n.tag }));
  }), { timeout: 10_000 }).toEqual([{ title: 'Card sold', body: payload.body, url: '/market', tag: 'market_sold' }]);

  // a malicious/absolute URL in a payload is neutralised to the home screen
  await cdp.send('ServiceWorker.deliverPushMessage', { origin: 'http://localhost:4173', registrationId, data: JSON.stringify({ title: 'x', url: 'https://evil.example/phish', tag: 'evil' }) });
  await expect.poll(async () => page.evaluate(async () => {
    const reg = await navigator.serviceWorker.ready;
    return (await reg.getNotifications({ tag: 'evil' })).map((n) => (n.data as { url: string }).url);
  })).toEqual(['/']);

  // tapping the notification: the worker messages the open app, which navigates (same-origin paths only)
  await page.evaluate(() => navigator.serviceWorker.dispatchEvent(new MessageEvent('message', { data: { type: 'navigate', url: '/missions' } })));
  await expect(page).toHaveURL(/\/missions$/);
  await page.evaluate(() => navigator.serviceWorker.dispatchEvent(new MessageEvent('message', { data: { type: 'navigate', url: '//evil.example' } })));
  await expect(page).toHaveURL(/\/missions$/);
});

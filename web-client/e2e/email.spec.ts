// Verification and password-reset e-mails: the HTTPS link in the mail opens the web client and completes the flow.
// The API's Development outbox (/dev/outbox, never mapped in Production) stands in for the mailbox.
import { expect, test, type APIRequestContext } from '@playwright/test';
import { API } from './helpers';

async function linkFor(request: APIRequestContext, to: string, subject: string): Promise<string> {
  for (let i = 0; i < 40; i++) {
    const mails = (await (await request.get(`${API}/dev/outbox`)).json()) as { to: string; subject: string; body: string }[];
    const m = mails.filter((x) => x.to === to && x.subject === subject).pop();
    if (m) {
      const url = m.body.match(/https?:\/\/\S+\?(verify|reset)=\S+/)![0];
      expect(m.body).toContain('Mở liên kết');      // Vietnamese
      expect(m.body).toContain('Open the link');    // English
      return url.replace(/^https?:\/\/[^/]+/, '');  // same path on the local preview
    }
    await new Promise((r) => setTimeout(r, 250));
  }
  throw new Error('no mail for ' + to);
}

test('email verification link and password reset link work in the web client', async ({ page, request }) => {
  const email = `mail-${Date.now()}@e2e.local`;
  await page.goto('/');
  await page.getByRole('button', { name: 'Create account' }).click();
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Display name').fill('Ml' + Date.now().toString(36).slice(-6));
  await page.getByLabel(/Password/).fill('Passw0rd1');
  await page.locator('form').getByRole('button', { name: 'Create account' }).click();
  await page.getByRole('button', { name: 'Join' }).first().click();
  await page.locator('form').getByRole('button', { name: 'Join' }).click();
  await expect(page.getByTestId('bal-MINI_GREEN_BEAN')).toBeVisible();

  // verify while signed in
  await page.goto(await linkFor(request, email, 'email.verify'));
  await expect(page.getByText('Email verified. Thank you!')).toBeVisible();
  expect(new URL(page.url()).search).toBe(''); // token removed from the address bar
  await page.goto('/settings');
  await expect(page.getByText(/Email verified$|· Email verified/)).toBeVisible();
  // the link is single use
  await page.goto(await linkFor(request, email, 'email.verify'));
  await expect(page.getByText(/invalid or expired/)).toBeVisible();

  // forgot password → link → new password; every old session ends
  await request.post(`${API}/api/auth/forgot-password`, { data: { email } });
  await page.goto(await linkFor(request, email, 'email.reset'));
  await page.getByLabel(/Password/).fill('N3wPassw0rd');
  await page.getByRole('button', { name: 'Set new password' }).click();
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible({ timeout: 15_000 });
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('Passw0rd1');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.locator('.toast-err')).toBeVisible(); // old password no longer works
  await page.getByLabel('Password').fill('N3wPassw0rd');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByText('Choose a server')).toBeVisible();
});

// Desktop: Web Admin Panel creates a gift code that a player then redeems, plus guild + daily login.
// The owner role is granted directly in the e2e database (PETSTAR_E2E_PG), exactly like an operator would
// bootstrap the first owner; everything else goes through the UI and the real API.
import { execFileSync } from 'node:child_process';
import { expect, test, type Page } from '@playwright/test';

const PG = process.env.PETSTAR_E2E_PG ?? 'postgresql://petstar:petstar_dev_only@localhost:5432/petstar_e2e';
const rnd = () => Math.random().toString(36).slice(2, 8);
const balance = async (page: Page, c: string) => Number((await page.getByTestId(`bal-${c}`).innerText()).replace(/\D/g, ''));

async function register(page: Page, name: string) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Create account' }).click();
  await page.getByLabel('Email').fill(`${name}-${rnd()}@e2e.local`);
  await page.getByLabel('Display name').fill(name);
  await page.getByLabel(/Password/).fill('Passw0rd1');
  await page.locator('form').getByRole('button', { name: 'Create account' }).click();
  await page.getByRole('button', { name: 'Join' }).first().click();
  await page.locator('form').getByRole('button', { name: 'Join' }).click();
  await expect(page.getByTestId('bal-MINI_GREEN_BEAN')).toBeVisible();
}

test('admin creates a gift code, player redeems it; guild and daily login', async ({ page }) => {
  const name = 'Owner' + rnd();
  await register(page, name);
  execFileSync('psql', [PG, '-c', `INSERT INTO account_roles (account_id, role, granted_at) SELECT id, 'owner', now() FROM accounts WHERE display_name = '${name}'`]);
  await page.reload(); // refresh rotates the token, which now carries the owner role
  await page.getByRole('button', { name: 'Admin' }).click();

  const code = 'E2E-' + rnd().toUpperCase();
  await page.getByRole('tab', { name: 'Gift codes' }).click();
  await page.getByPlaceholder(/CODE/).fill(code);
  await page.getByLabel('MINI_GREEN_BEAN').fill('123');
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page.getByRole('cell', { name: code })).toBeVisible();

  const before = await balance(page, 'MINI_GREEN_BEAN');
  await page.goto('/rewards');
  await page.getByLabel('Gift code').fill(code);
  await page.getByRole('button', { name: 'Redeem' }).click();
  await expect(page.getByText('Gift code redeemed!')).toBeVisible();
  await expect.poll(() => balance(page, 'MINI_GREEN_BEAN')).toBe(before + 123);
  // second redemption is refused by the server
  await page.getByLabel('Gift code').fill(code);
  await page.getByRole('button', { name: 'Redeem' }).click();
  await expect(page.getByText('You already redeemed this code.')).toBeVisible();

  // daily login: day 1 = 50 Mini Green Bean, once per day
  const b2 = await balance(page, 'MINI_GREEN_BEAN');
  await page.getByTestId('claim-login').click();
  await expect.poll(() => balance(page, 'MINI_GREEN_BEAN')).toBe(b2 + 50);
  await expect(page.getByTestId('claim-login')).toBeDisabled();

  // guild: create, post an announcement
  await page.goto('/guild');
  await page.getByPlaceholder('Guild name').fill('G' + rnd());
  await page.getByRole('button', { name: 'Create guild' }).click();
  await expect(page.getByText('Leader').first()).toBeVisible();
  await page.getByRole('tab', { name: 'Announcements' }).click();
  await page.getByPlaceholder('Write an announcement').fill('Welcome to the guild!');
  await page.getByRole('button', { name: 'Publish' }).click();
  await expect(page.getByText('Welcome to the guild!')).toBeVisible();

  // Vietnamese UI switch uses the shared localization tables
  await page.goto('/settings');
  await page.getByRole('button', { name: 'Tiếng Việt' }).click();
  await expect(page.getByText('Ngôn ngữ')).toBeVisible();
});

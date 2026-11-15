// End-to-end against the real backend (Postgres + Redis + WebSocket). No mocks, no fake balances:
// every number asserted here comes from the server ledger.
import { expect, test, type Browser, type Page } from '@playwright/test';

const uid = () => Math.random().toString(36).slice(2, 8);

async function register(page: Page, name: string) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Create account' }).click();
  await page.getByLabel('Email').fill(`${name}-${uid()}@e2e.local`);
  await page.getByLabel('Display name').fill(name);
  await page.getByLabel(/Password/).fill('Passw0rd1');
  await page.locator('form').getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByText('Choose a server')).toBeVisible();
  await page.getByRole('button', { name: 'Join' }).first().click();
  await page.locator('form').getByRole('button', { name: 'Join' }).click();
  await expect(page.getByTestId('bal-MINI_GREEN_BEAN')).toBeVisible();
}

const balance = async (page: Page, c: string) => Number((await page.getByTestId(`bal-${c}`).innerText()).replace(/\D/g, ''));

async function newPlayer(browser: Browser, name: string) {
  const ctx = await browser.newContext(test.info().project.use);
  const page = await ctx.newPage();
  await register(page, name);
  return page;
}

test('core loop on iPhone: register, shop, tear & flip a pack, deck 3+2, PvE duel', async ({ page }) => {
  await register(page, 'Tamer' + uid());
  expect(await balance(page, 'MINI_GREEN_BEAN')).toBe(500); // starter grant from the server
  expect(await balance(page, 'RAINBOW_STAR')).toBe(0);

  // buy one pack: price and new balance come from the server
  await page.getByRole('button', { name: 'Shop' }).click();
  await page.getByTestId('buy-basic_pack').click();
  await expect(page.getByText('Purchased ×1')).toBeVisible();
  await expect.poll(() => balance(page, 'MINI_GREEN_BEAN')).toBe(400);

  // open with the swipe-to-tear gesture, then flip all five cards one by one
  await page.goto('/open?pack=basic_pack');
  const strip = page.getByTestId('tear-strip');
  await expect(page.getByText('Swipe across the top edge to tear the pack')).toBeVisible();
  const box = (await strip.boundingBox())!;
  await page.mouse.move(box.x + 5, box.y + box.height / 2);
  await page.mouse.down();
  for (let i = 1; i <= 10; i++) await page.mouse.move(box.x + (box.width * i) / 10, box.y + box.height / 2);
  await page.mouse.up();
  const reveal = page.getByTestId('reveal');
  await expect(reveal).toBeVisible();
  for (let i = 0; i < 10; i++) await reveal.click();
  await expect(page.locator('.summary-card')).toHaveCount(5);
  await expect(page).toHaveURL(/\/open\/[0-9a-f-]{36}$/); // reload-safe URL for recovery

  // reload: the same opening is shown again (no second pack spent)
  await page.reload();
  await expect(page.locator('.summary-card')).toHaveCount(5);

  // open the remaining packs with Skip
  for (let n = 0; n < 3; n++) {
    await page.getByRole('button', { name: 'Open another' }).click();
    await page.getByRole('button', { name: 'Skip' }).click();
    await expect(page.locator('.summary-card')).toHaveCount(5);
  }

  // Pet Home shows 20 cards
  await page.goto('/home');
  await expect(page.getByText('20 cards')).toBeVisible();

  // deck: auto fill a 3 front + 2 back line-up and save
  await page.goto('/decks');
  await page.getByRole('button', { name: 'New deck' }).click();
  await page.getByRole('button', { name: 'Auto fill' }).click();
  await expect(page.locator('.deck-row.three .deck-slot.filled')).toHaveCount(3);
  await expect(page.locator('.deck-row.two .deck-slot.filled')).toHaveCount(2);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Default')).toBeVisible();

  // PvE duel on the code-drawn 3+2 board
  await page.goto('/duel');
  await page.getByTestId('fight-c1_s1').click();
  await expect(page).toHaveURL(/\/battle\//);
  await expect(page.locator('.side.mine .brow.three .slot')).toHaveCount(3);
  await expect(page.locator('.side.mine .brow.two .slot')).toHaveCount(2);
  await expect(page.locator('.side.enemy .slot.unit')).not.toHaveCount(0);
  for (let r = 0; r < 30; r++) {
    if (await page.locator('.result').isVisible()) break;
    // give the first actable unit a skill order when energy allows, targeting a legal enemy
    const mine = page.locator('.side.mine .slot.unit:not(.dead)').first();
    await mine.click();
    const skill = page.locator('.btn.act-Skill');
    if (await skill.isVisible() && await skill.isEnabled()) {
      await skill.click();
      const target = page.locator('.slot.targetable').first();
      if (await target.isVisible()) await target.click();
    }
    await page.getByTestId('end-turn').click();
    await page.waitForTimeout(300);
    await expect(page.getByTestId('end-turn').or(page.locator('.result'))).toBeEnabled({ timeout: 20_000 });
  }
  await expect(page.locator('.result h2')).toBeVisible();
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page).toHaveURL(/\/duel$/);
});

test('two players: market sale with live notification, world chat, live PvP round', async ({ browser }) => {
  const seller = await newPlayer(browser, 'Seller' + uid());
  const buyer = await newPlayer(browser, 'Buyer' + uid());

  // seller opens a starter pack and lists the first card for 40 Mini Green Bean
  await seller.goto('/open?pack=basic_pack');
  await seller.getByRole('button', { name: 'Skip' }).click();
  await seller.locator('.summary-card').first().click();
  await expect(seller).toHaveURL(/\/card\/\d+/);
  const serial = (await seller.locator('.serial').first().innerText()).match(/#([A-Z0-9]{12})/)![1];
  await seller.getByRole('button', { name: 'Sell' }).click();
  await seller.getByLabel('Price').fill('40');
  await expect(seller.getByText(/you receive 39/)).toBeVisible(); // 3% fee floor -> fee 1, net 39 (server quote)
  await seller.getByRole('button', { name: /List card|List anyway/ }).click();
  // a price far below the reference value needs an explicit second confirmation (server-enforced)
  const confirm = seller.getByRole('button', { name: 'List anyway' });
  const listed = seller.getByText('Card listed');
  await expect(listed.or(confirm)).toBeVisible();
  if (await confirm.isVisible()) await confirm.click();
  await expect(listed).toBeVisible();

  // buyer finds it by serial and buys it
  await buyer.goto('/market');
  await buyer.getByLabel('Search').fill(serial);
  await buyer.getByLabel('Search').press('Enter');
  await buyer.locator('.listing').first().click();
  await buyer.getByTestId('buy').click();
  await buyer.getByRole('button', { name: 'OK' }).click();
  await expect(buyer).toHaveURL(/\/card\/\d+/);
  // exact debit of the listed price is in the buyer's ledger (an achievement reward may follow it)
  await buyer.goto('/settings');
  await buyer.getByRole('tab', { name: 'Currency history' }).click();
  await expect(buyer.locator('.ledger .list-row', { hasText: 'Market purchase' })).toContainText('-40');

  // seller receives the sale notification over WebSocket and the net amount (39) via the ledger
  await expect(seller.getByText('Your card was sold')).toBeVisible({ timeout: 20_000 });
  await seller.goto('/settings');
  await seller.getByRole('tab', { name: 'Currency history' }).click();
  await expect(seller.locator('.ledger .list-row', { hasText: 'Market sale' })).toContainText('+39'); // 40 - 3% fee (floored to 1)

  // world chat delivered live
  await buyer.goto('/chat');
  await seller.goto('/chat');
  await expect(seller.locator('.chat-input input')).toBeEnabled();
  const msg = 'hello from e2e ' + uid();
  await seller.locator('.chat-input input').fill(msg);
  await seller.getByRole('button', { name: 'Send' }).click();
  await expect(buyer.getByText(msg)).toBeVisible({ timeout: 15_000 });

  // both need a default deck for live PvP: open packs + auto fill
  for (const p of [seller, buyer]) {
    for (let n = 0; n < 2; n++) { await p.goto('/open?pack=basic_pack'); await p.getByRole('button', { name: 'Skip' }).click(); await expect(p.locator('.summary-card')).toHaveCount(5); }
    await p.goto('/decks');
    await p.getByRole('button', { name: 'New deck' }).click();
    await p.getByRole('button', { name: 'Auto fill' }).click();
    await p.getByRole('button', { name: 'Save' }).click();
    await expect(p.getByText('Default')).toBeVisible();
  }

  // quick match: the second player is paired; both are pushed to the live board
  await seller.goto('/duel');
  await seller.getByRole('tab', { name: 'Live PvP' }).click();
  await seller.getByTestId('pvp-queue').click();
  await expect(seller.getByText('Searching for an opponent…')).toBeVisible();
  await buyer.goto('/duel');
  await buyer.getByRole('tab', { name: 'Live PvP' }).click();
  await buyer.getByTestId('pvp-queue').click();
  await expect(buyer).toHaveURL(/\/pvp\//, { timeout: 20_000 });
  await expect(seller).toHaveURL(/\/pvp\//, { timeout: 20_000 });
  await expect(seller.locator('.countdown')).toBeVisible();

  // sealed plans: first submit waits, second submit resolves the round for both
  await seller.getByTestId('end-turn').click();
  await expect(seller.getByText(/Waiting for opponent|Both ready/)).toBeVisible();
  await buyer.getByTestId('end-turn').click();
  await expect(seller.getByText('Round 2')).toBeVisible({ timeout: 20_000 });
  await expect(buyer.getByText('Round 2')).toBeVisible({ timeout: 20_000 });
});

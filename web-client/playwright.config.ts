import { defineConfig, devices } from '@playwright/test';

// E2E runs the production build (vite preview) against a REAL local backend (no mocks):
//   API:  PETSTAR_E2E_API (default http://127.0.0.1:5080) with Postgres + Redis, CORS allowing :4173
//   Web:  built with VITE_API_BASE pointing at that API
// Chromium with iPhone 13 emulation (viewport, touch, DPR, UA). Real WebKit/Safari is not available in this
// environment, so iOS Safari still needs a manual pass on a device.
const API = process.env.PETSTAR_E2E_API ?? 'http://127.0.0.1:5080';
const iphone = { ...devices['iPhone 13'] };
delete (iphone as { defaultBrowserType?: string }).defaultBrowserType;

export default defineConfig({
  testDir: 'e2e',
  timeout: 120_000,
  expect: { timeout: 15_000 },
  workers: 1,
  reporter: [['list']],
  use: { baseURL: 'http://localhost:4173', trace: 'off', screenshot: 'only-on-failure' },
  projects: [
    { name: 'iphone-chromium', use: { ...iphone, browserName: 'chromium' }, testIgnore: /desktop\.spec\.ts/ },
    { name: 'desktop-chromium', use: { ...devices['Desktop Chrome'] }, testMatch: /desktop\.spec\.ts/ },
  ],
  webServer: {
    command: `VITE_API_BASE=${API} npx vite build --logLevel warn && npx vite preview --port 4173 --strictPort`,
    url: 'http://localhost:4173',
    reuseExistingServer: true,
    timeout: 180_000,
  },
});

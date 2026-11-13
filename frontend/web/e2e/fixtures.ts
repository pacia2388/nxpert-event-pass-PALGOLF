import { test as base, expect, type APIRequestContext, type Page } from '@playwright/test';
import { ADMIN, API_URL } from './env';

/** Points the app at the e2e API. appSettings.json is read at runtime, so no rebuild is needed. */
export async function useE2eApi(page: Page): Promise<void> {
  await page.route('**/appSettings.json', async (route) => {
    const res = await route.fetch();
    const settings = await res.json();
    await route.fulfill({ response: res, json: { ...settings, apiBaseUrl: API_URL } });
  });
}

/** An API request context signed in through POST /api/auth/login. */
export async function signedInApi(
  playwright: { request: { newContext: (o: object) => Promise<APIRequestContext> } },
  creds: { email: string; password: string },
): Promise<APIRequestContext> {
  const anon = await playwright.request.newContext({ baseURL: API_URL });
  const res = await anon.post('/api/auth/login', { data: creds });
  expect(res.ok(), `login as ${creds.email}`).toBeTruthy();
  const { token } = (await res.json()) as { token: string };
  await anon.dispose();
  return playwright.request.newContext({
    baseURL: API_URL,
    extraHTTPHeaders: { Authorization: `Bearer ${token}` },
  });
}

type Fixtures = {
  /** Auto fixture: every page talks to the e2e API. */
  e2eApi: void;
  /** The API signed in as the seeded admin, for arranging test data. */
  adminApi: APIRequestContext;
  /** The API without a sign-in, for public endpoints. */
  publicApi: APIRequestContext;
};

export const test = base.extend<Fixtures>({
  e2eApi: [
    async ({ page }, use) => {
      await useE2eApi(page);
      await use();
      // A test can end while the page is still loading appSettings.json; drop the in-flight
      // route.fetch quietly instead of failing the run with "Test ended".
      await page.unrouteAll({ behavior: 'ignoreErrors' });
    },
    { auto: true },
  ],
  adminApi: async ({ playwright }, use) => {
    const api = await signedInApi(playwright, ADMIN);
    await use(api);
    await api.dispose();
  },
  publicApi: async ({ playwright }, use) => {
    const api = await playwright.request.newContext({ baseURL: API_URL });
    await use(api);
    await api.dispose();
  },
});

export { expect };

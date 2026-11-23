import { loadMenus, menuEnabled } from './config';

describe('menu.json', () => {
  const realFetch = globalThis.fetch;

  // Serve menu.json the way the web server would: a body, or an HTTP error.
  async function load(body: unknown, status = 200): Promise<void> {
    globalThis.fetch = (async () =>
      new Response(typeof body === 'string' ? body : JSON.stringify(body), {
        status,
      })) as typeof fetch;
    await loadMenus();
  }

  beforeEach(() => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(async () => {
    await load({ menus: {} }); // every menu back on for the other tests
    globalThis.fetch = realFetch;
    vi.restoreAllMocks();
  });

  it('switches off the menus set to false and keeps the rest on', async () => {
    await load({ menus: { voucher: false, dashboard: true } });
    expect(menuEnabled('voucher')).toBe(false);
    expect(menuEnabled('dashboard')).toBe(true);
    expect(menuEnabled('calendar')).toBe(true); // not in the file
  });

  it('turns a menu back on when the file changes', async () => {
    await load({ menus: { voucher: false } });
    await load({ menus: { voucher: true } });
    expect(menuEnabled('voucher')).toBe(true);
  });

  it('keeps every menu on when the file is missing', async () => {
    await load('Not found', 404);
    expect(menuEnabled('voucher')).toBe(true);
    expect(console.warn).toHaveBeenCalled();
  });

  it('keeps every menu on when the file is not valid JSON', async () => {
    await load('{ menus: ');
    expect(menuEnabled('voucher')).toBe(true);
    expect(console.error).toHaveBeenCalled();
  });

  it('keeps every menu on when "menus" is not an object', async () => {
    await load({ menus: ['voucher'] });
    expect(menuEnabled('voucher')).toBe(true);
    expect(console.error).toHaveBeenCalled();
  });

  it('keeps every menu on when a value is not true or false', async () => {
    await load({ menus: { dashboard: false, voucher: 'no' } });
    expect(menuEnabled('voucher')).toBe(true);
    expect(menuEnabled('dashboard')).toBe(true);
    expect(console.error).toHaveBeenCalled();
  });

  it('ignores unknown menus and the always-on ones, with a warning', async () => {
    await load({ menus: { settings: false, browseEvents: false, voucher: false } });
    expect(menuEnabled('voucher')).toBe(false);
    expect(console.warn).toHaveBeenCalledTimes(2);
  });
});

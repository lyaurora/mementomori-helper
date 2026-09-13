// Logged-in WebUI smoke test: navigation, character selection, tabs and draw details only.
// Does not purchase, summon, spend items, run jobs or delete logs.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');

(async () => {
    const base = process.argv[2] || 'http://127.0.0.1:5290';
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    const errors = [], failed = [], result = {};
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, colorScheme: 'dark' });
        page.on('pageerror', error => errors.push(error.message));
        page.on('response', response => {
            if (response.url().startsWith(base) && response.status() >= 400)
                failed.push({ path: new URL(response.url()).pathname, status: response.status() });
        });
        await page.goto(`${base}/`);
        await page.waitForFunction(() => !document.querySelector('[aria-label="外观模式"]')?.disabled);
        assert(await page.locator('.app-content .mud-card').count(), 'Home data is missing');
        const go = async (path, selector) => {
            const start = Date.now();
            const previous = await page.locator('.app-content > :first-child').elementHandle();
            await page.locator(`.app-nav a[href$="${path}" i]`).click();
            await page.waitForFunction(element => !element.isConnected, previous);
            await previous.dispose();
            await page.waitForSelector(selector);
            assert.equal(await page.locator('.app-content .mud-alert-error').count(), 0, `${path} failed to load`);
            result[path] = { readyMs: Date.now() - start };
        };
        await go('Character', '.app-content .character-icon');
        const icons = page.locator('.app-content .character-icon');
        result.Character.characters = await icons.count();
        for (let index = 0; index < Math.min(3, await icons.count()); index++) {
            await icons.nth(index).click();
            await page.waitForTimeout(60);
            assert(await page.locator('.app-content .mud-field').count(), 'Character details disappeared');
        }

        for (const path of ['Items', 'Shop']) {
            await go(path, '.app-content .mud-tab');
            const tabs = page.locator('.app-content .mud-tab');
            result[path].tabs = await tabs.count();
            assert(result[path].tabs > 0, `${path} tabs are missing`);
            for (let index = 0; index < result[path].tabs; index++) {
                await tabs.nth(index).click();
                await page.waitForFunction(i => document.querySelectorAll('.app-content .mud-tab')[i]?.classList.contains('mud-tab-active'), index);
            }
        }

        await go('Gacha', '.app-content .mud-card');
        result.Gacha.cases = await page.locator('.app-content .mud-card').count();
        await page.locator('.app-content .mud-card-header button').first().click();
        await page.waitForSelector('.mud-dialog');
        assert(await page.locator('.mud-dialog tbody tr').count(), 'Draw details are empty');
        await page.locator('.mud-dialog .mud-button-close').click();
        await page.waitForSelector('.mud-dialog', { state: 'detached' });

        await go('Battlelog', '#log_viewer');
        await go('Chat', '.chat-channels');
        await go('Settings', '.app-content .mud-card');
        result.Settings.cards = await page.locator('.app-content .mud-card').count();
        await page.setViewportSize({ width: 390, height: 844 });
        await page.waitForFunction(() => document.documentElement.scrollWidth <= innerWidth);
        assert.deepEqual(errors, []);
        assert.deepEqual(failed, []);
        console.log(JSON.stringify({ pages: result, errors, failed }, null, 2));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

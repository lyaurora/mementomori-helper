// Uses the project's existing Playwright installation; never invokes game actions.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');

(async () => {
    const url = process.argv[2] || 'http://127.0.0.1:5290';
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    const errors = [];
    const checkScheme = (page, expected) => page.waitForFunction(value =>
        getComputedStyle(document.documentElement).getPropertyValue('--mud-native-html-color-scheme').trim() === value, expected);
    const choose = async (page, name) => {
        await page.getByRole('button', { name: '外观模式', exact: true }).click();
        await page.locator('.mud-popover-open').getByText(name, { exact: true }).click();
    };
    try {
        const page = await browser.newPage({ colorScheme: 'dark', viewport: { width: 1440, height: 1000 } });
        page.on('pageerror', e => errors.push(e.message));
        await page.goto(`${url}/Settings`);
        await checkScheme(page, 'dark');
        await page.getByTitle('外观：跟随系统', { exact: true }).waitFor();
        const contrast = await page.evaluate(() => {
            const style = getComputedStyle(document.documentElement);
            const luminance = key => {
                const rgb = style.getPropertyValue(`--mud-palette-${key}`).match(/[\d.]+/g).slice(0, 3).map(Number)
                    .map(n => n / 255).map(n => n <= .04045 ? n / 12.92 : ((n + .055) / 1.055) ** 2.4);
                return rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722;
            };
            const ratio = (a, b) => (Math.max(luminance(a), luminance(b)) + .05) / (Math.min(luminance(a), luminance(b)) + .05);
            return { text: ratio('text-primary', 'surface'), secondary: ratio('text-secondary', 'surface'), button: ratio('primary', 'primary-text') };
        });
        assert(Object.values(contrast).every(value => value >= 4.5), 'Dark theme text has insufficient contrast');
        await page.emulateMedia({ colorScheme: 'light' });
        await checkScheme(page, 'light');
        await page.emulateMedia({ colorScheme: 'dark' });
        await checkScheme(page, 'dark');

        await choose(page, '浅色');
        await checkScheme(page, 'light');
        await page.emulateMedia({ colorScheme: 'light' });
        await page.emulateMedia({ colorScheme: 'dark' });
        await page.waitForTimeout(200);
        await checkScheme(page, 'light');
        await page.reload();
        await page.getByTitle('外观：浅色', { exact: true }).waitFor();
        await checkScheme(page, 'light');

        await choose(page, '深色');
        await checkScheme(page, 'dark');
        await page.emulateMedia({ colorScheme: 'light' });
        await page.waitForTimeout(200);
        await checkScheme(page, 'dark');
        await page.reload();
        await page.getByTitle('外观：深色', { exact: true }).waitFor();
        await checkScheme(page, 'dark');

        await choose(page, '跟随系统');
        await checkScheme(page, 'light');
        await page.emulateMedia({ colorScheme: 'dark' });
        await checkScheme(page, 'dark');
        await page.reload();
        await page.getByTitle('外观：跟随系统', { exact: true }).waitFor();
        await checkScheme(page, 'dark');
        await page.emulateMedia({ colorScheme: 'light' });
        await checkScheme(page, 'light');
        await page.setViewportSize({ width: 390, height: 844 });
        await page.waitForTimeout(200);
        const header = await page.locator('.app-header').boundingBox();
        assert(header.x >= 0 && header.x + header.width <= 391, 'Theme controls overflow on mobile');
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Settings overflow on mobile');
        await choose(page, '深色');
        await checkScheme(page, 'dark');

        const restricted = await browser.newPage({ colorScheme: 'dark' });
        restricted.on('pageerror', e => errors.push(e.message));
        await restricted.addInitScript(() => {
            Storage.prototype.getItem = Storage.prototype.setItem = () => { throw new DOMException('Storage disabled', 'SecurityError'); };
        });
        await restricted.goto(`${url}/Settings`);
        await checkScheme(restricted, 'dark');
        await choose(restricted, '浅色');
        await checkScheme(restricted, 'light');
        await restricted.getByText('主题已切换，但浏览器未能保存设置。', { exact: true }).waitFor();
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({ systemTheme: true, manualOverride: true, preferencesPersist: true, restrictedStorage: true, contrast, errors }, null, 2));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

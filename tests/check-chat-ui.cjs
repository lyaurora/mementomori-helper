// Run with Playwright available in NODE_PATH: node tests/check-chat-ui.cjs http://127.0.0.1:5290
// Exercises read/compose controls. It never sends a chat message or submits a vote.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');

(async () => {
    const url = process.argv[2] || 'http://127.0.0.1:5290';
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = [], failed = [];
    page.on('pageerror', e => errors.push(e.message));
    page.on('response', r => { if (r.url().startsWith(url) && r.status() >= 400) failed.push([new URL(r.url()).pathname, r.status()]); });
    const channel = name => page.getByRole('navigation', { name: '聊天频道' }).getByRole('button', { name, exact: true });
    const waitIdle = () => page.waitForFunction(() => !document.querySelector('[data-testid="chat-send"]')?.textContent.includes('处理中'));
    try {
        await page.goto(`${url}/Chat`);
        await page.waitForFunction(() => document.querySelector('[data-testid="chat-status"]')?.textContent.includes('实时连接已建立'));
        await page.waitForSelector('.chat-channels');
        const result = { worldMessages: await page.locator('.chat-message').count() };
        await page.getByRole('button', { name: '显示设置', exact: true }).click();
        await page.waitForSelector('#chat-text-size');
        assert.equal(await page.locator('#chat-text-size').getAttribute('step'), '1');
        assert.equal(await page.locator('#chat-sticker-size').getAttribute('step'), '1');
        await page.locator('#chat-text-size').evaluate(e => { e.value = '17'; e.dispatchEvent(new Event('input', { bubbles: true })); });
        await page.waitForFunction(() => JSON.parse(localStorage.getItem('mementomori.chat.appearance') || '{}').textSize === 17);
        await page.locator('#chat-sticker-size').evaluate(e => { e.value = '53'; e.dispatchEvent(new Event('input', { bubbles: true })); });
        await page.waitForFunction(() => JSON.parse(localStorage.getItem('mementomori.chat.appearance') || '{}').stickerSize === 53);
        await page.reload();
        await page.waitForFunction(() => document.querySelector('[data-testid="chat-status"]')?.textContent.includes('实时连接已建立'));
        await page.getByRole('button', { name: '显示设置', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('#chat-text-size')?.value === '17' && document.querySelector('#chat-sticker-size')?.value === '53');
        result.appearancePersists = true;
        await page.getByRole('button', { name: '显示设置', exact: true }).click();
        await page.waitForSelector('#chat-text-size', { state: 'detached' });
        assert(await page.getByTestId('chat-send').isDisabled(), 'Empty message can be sent');
        const sendBeforePicker = await page.getByTestId('chat-send').boundingBox();
        const stickerButton = await page.getByTestId('chat-emoticons-toggle').boundingBox();
        assert(stickerButton.x + stickerButton.width <= sendBeforePicker.x && Math.abs(stickerButton.y - sendBeforePicker.y) < 3,
            'Sticker picker is not beside the send button');
        await page.getByRole('button', { name: '表情包', exact: true }).click();
        await page.waitForSelector('.chat-emoticon-grid button');
        assert(Math.abs((await page.getByTestId('chat-send').boundingBox()).y - sendBeforePicker.y) < 3, 'Opening stickers moves the send button');
        const desktopPicker = await page.locator('#chat-emoticon-picker').boundingBox();
        const input = await page.locator('#chat-message-input').boundingBox();
        const messageHeight = (await page.getByTestId('chat-messages').boundingBox()).height;
        assert(desktopPicker.x >= input.x + input.width, 'Desktop sticker panel does not sit beside the conversation');
        assert(await page.getByTestId('chat-messages').evaluate(e => e.scrollHeight - e.scrollTop - e.clientHeight < 64), 'Opening stickers loses the latest messages');
        result.freeStickers = await page.locator('.chat-emoticon-grid button:not(:disabled)').count();
        assert(result.freeStickers >= 27, 'Default stickers missing');
        const categories = page.getByRole('group', { name: '表情分类' });
        await categories.getByRole('button', { name: '角色', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('#chat-emoticon-picker button[aria-label="插入贴图 1001"]'));
        assert(Math.abs((await page.locator('#chat-emoticon-picker').boundingBox()).height - desktopPicker.height) < 1, 'Sticker category changes the panel height');
        assert(Math.abs((await page.getByTestId('chat-messages').boundingBox()).height - messageHeight) < 1, 'Sticker category resizes the conversation');
        await categories.getByRole('button', { name: '猫猫', exact: true }).click();
        await page.waitForSelector('#chat-emoticon-picker button[aria-label="插入贴图 1"]');
        assert.equal(await page.getByTestId('chat-emoticons-toggle').getAttribute('aria-expanded'), 'true', 'Switching sticker categories closes the picker');
        await page.getByRole('button', { name: '插入贴图 1', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('#chat-message-input')?.value === '#1#');
        assert.equal(await page.locator('.chat-draft-preview .chat-sticker').count(), 1, 'Sticker preview is missing');
        assert.equal(await page.locator('.chat-draft-preview .chat-sticker').evaluate(e => e.getBoundingClientRect().width), 53, 'Sticker sizing is not applied');
        assert(!(await page.getByTestId('chat-send').isDisabled()), 'Composed sticker cannot be sent');
        const style = await page.locator('.chat-draft-preview .chat-sticker').getAttribute('style');
        const atlas = style.match(/url\('([^']+)'\)/)[1];
        const asset = await page.request.get(new URL(atlas, `${url}/`).toString());
        assert.equal(asset.status(), 200, 'Sticker atlas is missing');
        assert(asset.headers()['content-type'].startsWith('image/webp'), 'Sticker atlas is not an image');
        await page.getByRole('button', { name: '插入贴图 2', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('#chat-message-input')?.value === '#1##2#');
        assert.equal(await page.locator('.chat-draft-preview .chat-sticker').count(), 2, 'Multiple stickers cannot be composed');
        await page.locator('#chat-message-input').fill('hello #1##2#');
        assert.equal(await page.locator('#chat-emoticon-picker').count(), 1, 'Typing closes the sticker panel');
        await page.getByRole('button', { name: '收起表情包', exact: true }).click();
        await page.waitForSelector('.chat-emoticon-grid', { state: 'detached' });
        assert.equal(await page.locator('.chat-emoticon-grid').count(), 0);
        await page.locator('#chat-message-input').fill('');
        await page.waitForFunction(() => document.querySelector('[data-testid="chat-send"]')?.disabled);

        await channel('公会').click();
        await page.waitForSelector('.chat-message');
        await page.waitForFunction(() => Object.keys(localStorage).some(key => key.startsWith('mementomori.chat.channel.') && localStorage.getItem(key) === 'Guild'));
        await page.evaluate(() => {
            const original = Storage.prototype.getItem;
            window.chatPreferenceReads = 0;
            Storage.prototype.getItem = function (key) {
                if (key.startsWith('mementomori.chat.')) window.chatPreferenceReads++;
                return original.call(this, key);
            };
        });
        await page.locator('.app-nav a[href$="Settings" i]').click();
        await page.waitForSelector('.chat-page', { state: 'detached' });
        await page.locator('.app-nav a[href$="Chat" i]').click();
        await page.waitForFunction(() => document.querySelector('.chat-conversation-heading strong')?.textContent === '公会');
        assert.equal(await page.locator('.chat-loading').count(), 0, 'Returning to chat waits for stored preferences again');
        assert.equal(await page.evaluate(() => window.chatPreferenceReads), 0, 'Returning to chat rereads browser storage');
        const messageMenus = await page.getByRole('button', { name: '消息操作', exact: true }).count();
        await page.waitForFunction(limit => document.querySelectorAll('.mud-popover').length <= limit, messageMenus + 3);
        const chatModule = /\/chat(?:\.[^/]+)?\.js(?:\?.*)?$/;
        await page.route(chatModule, async route => { await new Promise(resolve => setTimeout(resolve, 500)); await route.continue(); });
        await page.reload({ waitUntil: 'domcontentloaded' });
        await page.waitForSelector('.chat-loading');
        assert.equal(await page.locator('.chat-channels').count(), 0, 'World channel flashes before the saved channel is restored');
        await page.waitForFunction(() => document.querySelector('.chat-conversation-heading strong')?.textContent === '公会'
            && document.querySelector('[data-testid="chat-status"]')?.textContent.includes('实时连接已建立'));
        await page.unroute(chatModule);
        await page.waitForSelector('.chat-message');
        result.channelPersists = true;
        result.wrapping = await page.evaluate(() => {
            const clone = document.querySelector('.chat-message').cloneNode(true);
            clone.classList.remove('own');
            clone.querySelector('.chat-message-meta')?.remove();
            clone.querySelector('.chat-message-actions')?.remove();
            const bubble = clone.querySelector('.chat-bubble');
            bubble.classList.remove('sticker-only');
            const paragraph = bubble.querySelector('p');
            bubble.replaceChildren(paragraph);
            document.querySelector('.chat-messages').append(clone);
            try {
                paragraph.textContent = '他們的修隊伍都還沒出現呢';
                const range = document.createRange();
                range.selectNodeContents(paragraph);
                const shortLines = range.getClientRects().length;
                paragraph.textContent = '第一行\n第二行';
                const preservesNewline = paragraph.getBoundingClientRect().height >= parseFloat(getComputedStyle(paragraph).lineHeight) * 1.9;
                clone.style.maxWidth = '280px';
                paragraph.textContent = 'https://example.com/' + 'x'.repeat(80);
                return { shortLines, preservesNewline, longLinkFits: bubble.scrollWidth <= bubble.clientWidth + 1 };
            } finally { clone.remove(); }
        });
        assert.equal(result.wrapping.shortLines, 1, 'Short messages wrap before using available space');
        assert(result.wrapping.preservesNewline && result.wrapping.longLinkFits, 'Explicit newlines or long-link wrapping is broken');
        await page.getByRole('button', { name: '公告与投票', exact: true }).click();
        await page.waitForSelector('.chat-guild-info');
        await waitIdle();
        assert.equal(await page.locator('.chat-guild-info').count(), 1, 'Guild panel did not open');
        result.announcements = await page.locator('.chat-announcement').count();
        result.surveys = await page.locator('.chat-survey').count();
        if (result.announcements) {
            assert((await page.locator('.chat-announcement .chat-muted').first().innerText()).includes('公告发布：'));
            assert.equal(await page.locator('.chat-announcement .chat-reactions').count(), result.announcements, 'Announcements lack reaction controls');
            result.announcementReactions = await page.locator('.chat-announcement .chat-reaction-icon').count();
        }
        await page.locator('.chat-guild-info .mud-tab').filter({ hasText: '投票 ·' }).click();
        if (result.surveys) assert((await page.locator('.chat-vote-footer').first().innerText()).includes('人参与'));
        await page.getByRole('button', { name: '收起公告与投票', exact: true }).click();
        await page.waitForSelector('.chat-guild-info', { state: 'detached' });
        assert.equal(await page.locator('.chat-guild-info').count(), 0, 'Guild panel did not close');
        result.unresolvedCastleNames = await page.locator('.chat-bubble, .chat-system-message').filter({ hasText: /\[(Global|Local)GvgCastleName\d+\]/ }).count();
        assert.equal(result.unresolvedCastleNames, 0, 'System message parameters remain untranslated');
        if (await page.locator('.chat-bubble').count()) {
            result.fontSize = await page.locator('.chat-bubble').first().evaluate(e => parseFloat(getComputedStyle(e).fontSize));
            assert.equal(result.fontSize, 17, 'Text sizing is not applied');
        }
        const reaction = page.getByRole('button', { name: '添加回应', exact: true }).first();
        if (await reaction.count()) {
            assert(!(await reaction.isDisabled()), 'Legacy CanReact field disables the reaction picker');
            await reaction.click();
            await page.waitForSelector('.chat-reaction-choice');
            assert.equal(await page.locator('.chat-reaction-choice:visible').count(), 4);
            assert(await page.locator('.chat-reaction-choice:visible').evaluateAll(images => images.every(i => i.complete && i.naturalWidth > 0)), 'Game reaction images failed to load');
            await page.locator('.chat-reaction-options button').first().focus();
            await page.keyboard.press('Escape');
            await page.waitForSelector('.mud-overlay', { state: 'detached' });
            assert(await reaction.evaluate(e => e === document.activeElement), 'Closing reactions loses keyboard focus');
        }
        const otherMessage = page.locator('.chat-message:not(.own)').first();
        const more = otherMessage.getByRole('button', { name: '消息操作', exact: true });
        if (await more.count()) {
            await more.click();
            await page.waitForTimeout(200);
            assert.equal(await page.getByText('设为公告', { exact: true }).filter({ visible: true }).count(), 0, 'Other players messages can be registered as announcements');
            await page.mouse.click(1430, 990);
            await page.waitForSelector('.mud-overlay', { state: 'detached' });
        }
        const avatar = page.locator('.chat-message-avatar img').last();
        if (await avatar.count()) {
            await avatar.scrollIntoViewIfNeeded();
            await avatar.waitFor({ state: 'visible' });
            await page.waitForFunction(() => Array.from(document.querySelectorAll('.chat-message-avatar img')).some(image => image.complete && image.naturalWidth > 0));
            result.avatarsLoaded = true;
        }

        await channel('私聊').click();
        await page.getByRole('button', { name: '选择玩家', exact: true }).click();
        await page.waitForSelector('#chat-player-id');
        await waitIdle();
        assert.equal(await page.locator('#chat-player-id').count(), 1);
        await page.getByRole('button', { name: '收起玩家选择', exact: true }).click();
        await page.waitForSelector('#chat-player-id', { state: 'detached' });
        assert.equal(await page.locator('#chat-player-id').count(), 0, 'Player panel did not close');
        result.privateContacts = await page.locator('.chat-contact').count();
        if (result.privateContacts) {
            assert(!(await page.locator('.chat-contact').first().isDisabled()), 'Private contacts are permanently disabled');
            await page.locator('.chat-contact').first().click();
            await page.waitForSelector('.chat-contact.selected');
            await waitIdle();
            assert.equal(await page.locator('.chat-contact.selected').count(), 1, 'Private conversation cannot be selected');
            result.privateMessages = await page.locator('.chat-message').count();
        }
        await page.setViewportSize({ width: 390, height: 844 });
        await page.waitForTimeout(300);
        assert(!(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)), 'Chat overflows a mobile viewport');
        const mobileSend = await page.getByTestId('chat-send').boundingBox();
        const mobileSticker = await page.getByTestId('chat-emoticons-toggle').boundingBox();
        assert(mobileSticker.x + mobileSticker.width <= mobileSend.x && Math.abs(mobileSticker.y - mobileSend.y) < 3, 'Sticker/send controls split on mobile');
        await page.getByTestId('chat-emoticons-toggle').click();
        await page.waitForSelector('#chat-emoticon-picker');
        const picker = await page.locator('#chat-emoticon-picker').boundingBox();
        assert(picker.x >= 0 && picker.x + picker.width <= 391, 'Sticker picker overflows on mobile');
        const mobileInput = await page.locator('#chat-message-input').boundingBox();
        assert(mobileInput.y + mobileInput.height <= picker.y, 'Mobile stickers cover the composer instead of sitting below it');
        await page.getByRole('group', { name: '表情分类' }).getByRole('button', { name: '角色', exact: true }).click();
        await page.waitForSelector('#chat-emoticon-picker button[aria-label="插入贴图 1001"]');
        assert(Math.abs((await page.locator('#chat-emoticon-picker').boundingBox()).height - picker.height) < 1, 'Mobile sticker categories change panel size');
        assert.deepEqual(errors, []);
        assert.deepEqual(failed, []);
        console.log(JSON.stringify({ ...result, errors, failed }, null, 2));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

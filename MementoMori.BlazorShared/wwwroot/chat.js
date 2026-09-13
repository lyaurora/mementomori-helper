export function followMessages(element, mode) {
    if (!element) return;
    const previousHeight = Number(element.dataset.chatHeight || 0);
    const previousViewport = Number(element.dataset.chatViewport || element.clientHeight);
    if (mode === 'older') element.scrollTop += element.scrollHeight - previousHeight;
    else if (mode === 'end' || previousHeight - element.scrollTop - previousViewport < 64)
        element.scrollTop = element.scrollHeight;
    element.dataset.chatHeight = element.scrollHeight;
    element.dataset.chatViewport = element.clientHeight;
}

export function getAppearance() {
    let value;
    try { value = JSON.parse(localStorage.getItem('mementomori.chat.appearance')); } catch { }
    const number = (n, min, max, fallback) => Number.isFinite(n) ? Math.max(min, Math.min(max, Math.round(n))) : fallback;
    return { textSize: number(value?.textSize, 12, 24, 16), stickerSize: number(value?.stickerSize, 24, 96, 48) };
}

export function saveAppearance(value) {
    try { localStorage.setItem('mementomori.chat.appearance', JSON.stringify(value)); return true; }
    catch { return false; }
}

export function getChannel(account) {
    try { return localStorage.getItem(`mementomori.chat.channel.${account}`); }
    catch { return null; }
}

export function saveChannel(account, channel) {
    try { localStorage.setItem(`mementomori.chat.channel.${account}`, channel); return true; }
    catch { return false; }
}

export function revealEmoticons(element) {
    element?.scrollIntoView({ block: 'nearest' });
}

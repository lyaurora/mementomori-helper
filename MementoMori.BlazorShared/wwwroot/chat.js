export function followMessages(element, mode) {
    if (!element) return;
    const previousHeight = Number(element.dataset.chatHeight || 0);
    if (mode === 'older') element.scrollTop += element.scrollHeight - previousHeight;
    else if (mode === 'end' || previousHeight - element.scrollTop - element.clientHeight < 64)
        element.scrollTop = element.scrollHeight;
    element.dataset.chatHeight = element.scrollHeight;
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

// Themed replacement for native title tooltips: title moves to data-tip while hovered/focused, one shared div shows it.
(() => {
    if (window.mementoTooltip) return;
    window.mementoTooltip = true;
    let tip, cur, timer;
    function hide() {
        clearTimeout(timer);
        if (tip) tip.hidden = true;
        if (cur && cur.dataset.tip != null) {
            if (!cur.hasAttribute('title')) cur.setAttribute('title', cur.dataset.tip);
            delete cur.dataset.tip;
        }
        cur = null;
    }
    function show() {
        if (!cur) return;
        if (!tip) { tip = document.createElement('div'); tip.className = 'app-tooltip'; tip.setAttribute('role', 'tooltip'); document.body.append(tip); }
        tip.textContent = cur.dataset.tip;
        tip.hidden = false;
        const r = cur.getBoundingClientRect(), t = tip.getBoundingClientRect(), m = 8;
        let top = r.bottom + 6;
        if (top + t.height > innerHeight - m) top = r.top - t.height - 6;
        const left = Math.min(Math.max(m, r.left + r.width / 2 - t.width / 2), innerWidth - t.width - m);
        tip.style.top = Math.max(m, top) + 'px';
        tip.style.left = left + 'px';
    }
    function enter(e) {
        if (e.pointerType === 'touch') return;
        // Focus only counts when it comes from the keyboard: a dialog handing focus back to its button must not pop the tip again.
        if (e.type === 'focusin' && !e.target.matches?.(':focus-visible')) return;
        const el = e.target.closest?.('[title]');
        // An iframe's title is its accessible name, not a tip (the battle-log viewer would get one over its content).
        if (!el || el === cur || el.tagName === 'IFRAME') return;
        const text = el.getAttribute('title');
        hide();
        if (!text?.trim()) return;
        cur = el;
        el.dataset.tip = text;
        if (!el.hasAttribute('aria-label') && !el.hasAttribute('aria-labelledby') && !el.textContent.trim()) el.setAttribute('aria-label', text);
        el.removeAttribute('title');
        timer = setTimeout(show, 400);
    }
    function leave(e) {
        if (cur && !(e.relatedTarget && cur.contains(e.relatedTarget))) hide();
    }
    document.addEventListener('pointerover', enter);
    document.addEventListener('focusin', enter);
    document.addEventListener('pointerout', leave);
    document.addEventListener('focusout', leave);
    document.addEventListener('keydown', e => { if (e.key === 'Escape') hide(); });
    // A press opens whatever the control does: drop the pending/shown bubble but keep title off until leave, so no native tip either.
    document.addEventListener('pointerdown', () => { clearTimeout(timer); if (tip) tip.hidden = true; }, true);
    addEventListener('scroll', hide, true);
})();

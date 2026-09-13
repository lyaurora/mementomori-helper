(() => {
    if (window.mementoTheme) return;
    const system = matchMedia('(prefers-color-scheme: dark)');
    let mode = 'system';
    try {
        const saved = localStorage.getItem('mementomori.theme');
        if (saved === 'light' || saved === 'dark') mode = saved;
    } catch { }
    function apply() {
        const theme = mode === 'system' ? system.matches ? 'dark' : 'light' : mode;
        document.documentElement.dataset.theme = theme;
        document.documentElement.style.colorScheme = theme;
    }
    window.mementoTheme = {
        getMode: () => mode,
        setMode: value => {
            mode = value === 'light' || value === 'dark' ? value : 'system';
            apply();
            try { localStorage.setItem('mementomori.theme', mode); return true; }
            catch { return false; }
        }
    };
    system.addEventListener('change', () => { if (mode === 'system') apply(); });
    apply();
})();

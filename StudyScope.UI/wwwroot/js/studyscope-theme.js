// Farbschema (Einstellungen › Darstellung). Wird im <head> vor dem ersten Zeichnen geladen: setzt data-theme
// (gewählt: system/light/dark) und data-theme-effective (light/dark) auf <html>, damit nichts hell aufblitzt.
// Der gespeicherte Wert steht in config.json (UiSettings.Theme); localStorage ist nur ein Spiegel für den Start,
// App.razor gleicht ihn nach dem Laden an. Bei „system“ folgt die Darstellung prefers-color-scheme – auch zur Laufzeit.
window.studyScopeTheme = (function () {
    var key = 'studyscope-theme';
    var root = document.documentElement;
    var mq = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
    var mode = 'system';
    var listener = null;

    function normalize(m) { return m === 'light' || m === 'dark' ? m : 'system'; }
    function systemDark() { return !!(mq && mq.matches); }
    function effective() { return mode === 'dark' || (mode === 'system' && systemDark()) ? 'dark' : 'light'; }

    function render() {
        var eff = effective();
        root.setAttribute('data-theme', mode);
        root.setAttribute('data-theme-effective', eff);
        if (listener) { try { listener.invokeMethodAsync('OnThemeChanged', eff, systemDark()); } catch (_) { } }
    }

    try { mode = normalize(localStorage.getItem(key)); } catch (_) { }
    render();

    if (mq) {
        var onChange = function () { if (mode === 'system') render(); else if (listener) render(); };
        if (mq.addEventListener) mq.addEventListener('change', onChange);
        else if (mq.addListener) mq.addListener(onChange);
    }

    return {
        // Anwenden (Vorschau oder gespeicherter Wert); persist=true schreibt den Spiegel für den nächsten Start
        apply: function (m, persist) {
            mode = normalize(m);
            if (persist) { try { localStorage.setItem(key, mode); } catch (_) { } }
            render();
            return effective();
        },
        // .NET erfährt Wechsel des Systems (für den Hinweis „zurzeit: Dunkel“)
        listen: function (dotnetRef) { listener = dotnetRef; render(); },
        unlisten: function () { listener = null; },
        state: function () { return { mode: mode, effective: effective(), systemDark: systemDark() }; }
    };
})();

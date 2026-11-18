// Seitenleiste (App.razor): meldet Strg+B und die Fensterbreite an .NET. Ob umgeschaltet wird, entscheidet
// LayoutViewModel (nicht beim Schreiben in Eingabefeldern, nicht im Vortrags-/Vollbildmodus). Hier wird nur die
// Standardaktion des Browsers für Strg+B unterdrückt, wenn kein Textfeld den Fokus hat.
window.studyScopeLayout = (function () {
    var keyHandler = null;
    var resizeHandler = null;
    var resizeTimer = 0;

    function focusInfo() {
        var el = document.activeElement;
        if (!el || el === document.body) return { tag: '', type: '', editable: false };
        return {
            tag: el.tagName || '',
            type: el.tagName === 'INPUT' ? (el.getAttribute('type') || 'text') : '',
            editable: !!el.isContentEditable
        };
    }

    function isTextEntry(f) {
        if (f.editable || f.tag === 'TEXTAREA' || f.tag === 'SELECT') return true;
        if (f.tag !== 'INPUT') return false;
        return ['checkbox', 'radio', 'button', 'submit', 'reset', 'range', 'color', 'file', 'image'].indexOf(f.type.toLowerCase()) < 0;
    }

    return {
        attach: function (dotnetRef) {
            this.detach();
            keyHandler = function (e) {
                if (!(e.ctrlKey && (e.key === 'b' || e.key === 'B'))) return;
                var f = focusInfo();
                if (!e.altKey && !e.shiftKey && !e.metaKey && !isTextEntry(f)) e.preventDefault();
                dotnetRef.invokeMethodAsync('OnLayoutShortcut', e.key, e.ctrlKey, e.altKey, e.shiftKey, e.metaKey, e.repeat,
                    f.tag, f.type, f.editable);
            };
            resizeHandler = function () {
                clearTimeout(resizeTimer);
                resizeTimer = setTimeout(function () { dotnetRef.invokeMethodAsync('OnWindowWidth', window.innerWidth); }, 120);
            };
            document.addEventListener('keydown', keyHandler);
            window.addEventListener('resize', resizeHandler);
            return window.innerWidth;
        },
        detach: function () {
            if (keyHandler) document.removeEventListener('keydown', keyHandler);
            if (resizeHandler) window.removeEventListener('resize', resizeHandler);
            clearTimeout(resizeTimer);
            keyHandler = null;
            resizeHandler = null;
        }
    };
})();

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
        },

        // Ziehbare Trennlinie der Seitenleiste (SidebarPanel.razor). Während des Ziehens setzt das Skript nur die
        // CSS-Variable --ss-sidebar-width (höchstens einmal je Bild, ohne Blazor); erst beim Loslassen entscheidet
        // LayoutViewModel (EndSidebarResize: begrenzen, speichern oder einklappen) und liefert die angezeigte Breite.
        // o = { min, minContent, maxFraction, collapseBelow } – dieselben Werte wie in LayoutViewModel.
        attachResizer: function (separator, dotnetRef, o) {
            if (!separator || separator.__ssResizer) return;
            separator.__ssResizer = true;
            var sidebar = separator.closest('.ss-sidebar');
            var drag = null;

            function maxWidth() {
                var w = window.innerWidth;
                return Math.max(o.min, Math.floor(Math.min(w * o.maxFraction, w - o.minContent)));
            }
            function currentWidth() {
                var v = parseFloat(sidebar.style.getPropertyValue('--ss-sidebar-width'));
                return isNaN(v) ? sidebar.getBoundingClientRect().width : v;
            }
            function apply() {
                drag.frame = 0;
                var live = Math.round(Math.min(Math.max(drag.raw, o.min), maxWidth()));
                sidebar.style.setProperty('--ss-sidebar-width', live + 'px');
                separator.setAttribute('aria-valuenow', String(live));
                sidebar.classList.toggle('ss-collapse-hint', drag.raw < o.collapseBelow);
            }
            function finish(e) {
                if (!drag) return;
                var d = drag;
                drag = null;
                if (d.frame) cancelAnimationFrame(d.frame);
                try { separator.releasePointerCapture(e.pointerId); } catch (_) { }
                if (d.overlay) d.overlay.remove();
                document.body.classList.remove('ss-resizing');
                sidebar.classList.remove('ss-resizing', 'ss-collapse-hint');
                if (!d.moved || e.type === 'pointercancel') {
                    sidebar.style.setProperty('--ss-sidebar-width', d.start + 'px');
                    return;
                }
                dotnetRef.invokeMethodAsync('EndSidebarResize', d.raw).then(function (shown) {
                    // eingeklappt (null): Ausgangsbreite zurück, sie gilt beim Wiederaufklappen
                    sidebar.style.setProperty('--ss-sidebar-width', (shown == null ? d.start : shown) + 'px');
                });
            }

            separator.addEventListener('pointerdown', function (e) {
                if (e.button !== 0 || sidebar.classList.contains('collapsed')) return;
                e.preventDefault();
                separator.setPointerCapture(e.pointerId);
                var overlay = document.createElement('div');   // fängt Ereignisse über dem iframe des Berichts ab
                overlay.className = 'ss-resize-overlay';
                document.body.appendChild(overlay);
                var start = currentWidth();
                drag = { x: e.clientX, start: start, raw: start, moved: false, frame: 0, overlay: overlay };
                document.body.classList.add('ss-resizing');
                sidebar.classList.add('ss-resizing');
            });
            separator.addEventListener('pointermove', function (e) {
                if (!drag) return;
                var dx = e.clientX - drag.x;
                if (dx !== 0) drag.moved = true;
                drag.raw = drag.start + dx;
                if (!drag.frame) drag.frame = requestAnimationFrame(apply);
            });
            separator.addEventListener('pointerup', finish);
            separator.addEventListener('pointercancel', finish);
            separator.addEventListener('lostpointercapture', function (e) { if (drag) finish(e); });
            separator.addEventListener('keydown', function (e) {
                if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].indexOf(e.key) < 0 || e.ctrlKey || e.altKey || e.metaKey) return;
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnSeparatorKey', e.key);
            });
        }
    };
})();

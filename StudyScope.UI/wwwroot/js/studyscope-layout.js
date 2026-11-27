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

    // Schreibrichtung der Oberfläche (dir="rtl" auf <html> bei Sprachen wie Arabisch oder Hebräisch)
    function isRtl(el) { return getComputedStyle(el || document.documentElement).direction === 'rtl'; }

    return {
        // Sprache und Schreibrichtung der Oberfläche auf <html> setzen (App.razor beim Start)
        setLanguage: function (lang, rtl) {
            document.documentElement.setAttribute('lang', lang);
            document.documentElement.setAttribute('dir', rtl ? 'rtl' : 'ltr');
        },

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

        // Ausklappmenüs (DropdownMenu.razor: ☰ und „» Mehr“). Die Logik liegt in MenuNavigator (.NET); hier nur:
        // Tasten abfangen (bei offenem Menü ohne Standardaktion und ohne Weitergabe – Strg+B u. a. laufen weiter),
        // Klick außerhalb melden und den Fokus setzen.
        attachMenu: function (wrapper, dotnetRef) {
            if (!wrapper || wrapper.__ssMenu) return;
            var menuKeys = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'Enter', ' ', 'Escape'];
            var onKey = function (e) {
                if (e.ctrlKey || e.altKey || e.metaKey) return;
                var open = wrapper.getAttribute('data-open') === 'true';
                var trigger = wrapper.querySelector('[data-menu-trigger]');
                if (!open) {
                    if (e.target === trigger && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
                        e.preventDefault();
                        dotnetRef.invokeMethodAsync('OnMenuKey', e.key);
                    }
                    return;
                }
                if (e.key === 'Tab') { dotnetRef.invokeMethodAsync('OnMenuKey', 'Tab'); return; }
                if (menuKeys.indexOf(e.key) < 0) return;
                e.preventDefault();
                e.stopPropagation();
                dotnetRef.invokeMethodAsync('OnMenuKey', e.key);
            };
            var onPointer = function (e) {
                if (wrapper.getAttribute('data-open') !== 'true' || wrapper.contains(e.target)) return;
                dotnetRef.invokeMethodAsync('OnMenuOutside');
            };
            var onBlur = function () {
                if (wrapper.getAttribute('data-open') === 'true') dotnetRef.invokeMethodAsync('OnMenuOutside');
            };
            wrapper.addEventListener('keydown', onKey);
            document.addEventListener('pointerdown', onPointer, true);
            window.addEventListener('blur', onBlur);
            wrapper.__ssMenu = function () {
                wrapper.removeEventListener('keydown', onKey);
                document.removeEventListener('pointerdown', onPointer, true);
                window.removeEventListener('blur', onBlur);
            };
        },
        detachMenu: function (wrapper) {
            if (wrapper && wrapper.__ssMenu) { wrapper.__ssMenu(); wrapper.__ssMenu = null; }
        },
        focusElement: function (id) {
            var el = document.getElementById(id);
            if (el) el.focus({ preventScroll: true });
        },
        // Für Dialoge: erst nach dem Zeichnen fokussieren (das Menü gibt den Fokus beim Schließen an ☰ zurück)
        focusElementLater: function (id) {
            requestAnimationFrame(function () { requestAnimationFrame(function () {
                var el = document.getElementById(id);
                if (el) { el.focus({ preventScroll: true }); if (el.select) el.select(); }
            }); });
        },

        // Reiterleiste (TabBar.razor): Breiten aller Reiter (auch der ausgeblendeten), verfügbare Breite und Breite
        // von „» Mehr“ an .NET melden – beim Start und bei jeder Größenänderung. Die Aufteilung rechnet TabOverflow.
        attachTabs: function (container, dotnetRef) {
            if (!container || container.__ssTabs) return;
            var frame = 0;
            var measure = function () {
                frame = 0;
                var gap = parseFloat(getComputedStyle(container).columnGap) || 0;
                var tabs = container.querySelectorAll('[data-tab]');
                var widths = [];
                for (var i = 0; i < tabs.length; i++) widths.push(Math.ceil(tabs[i].getBoundingClientRect().width) + gap);
                var more = container.querySelector('[data-tab-more]');
                var moreWidth = more ? Math.ceil(more.getBoundingClientRect().width) + gap : 0;
                dotnetRef.invokeMethodAsync('OnTabsMeasured', widths, Math.floor(container.clientWidth), moreWidth);
            };
            var schedule = function () { if (!frame) frame = requestAnimationFrame(measure); };
            var observer = new ResizeObserver(schedule);
            observer.observe(container);
            if (document.fonts && document.fonts.ready) document.fonts.ready.then(schedule);
            container.__ssTabs = { observer: observer, schedule: schedule };
            schedule();
        },
        remeasureTabs: function (container) {
            if (container && container.__ssTabs) container.__ssTabs.schedule();
        },
        detachTabs: function (container) {
            if (container && container.__ssTabs) { container.__ssTabs.observer.disconnect(); container.__ssTabs = null; }
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
                // Bei rechts-nach-links liegt die Leiste rechts: Ziehen nach links macht sie breiter
                drag = { x: e.clientX, dir: isRtl(sidebar) ? -1 : 1, start: start, raw: start, moved: false, frame: 0, overlay: overlay };
                document.body.classList.add('ss-resizing');
                sidebar.classList.add('ss-resizing');
            });
            separator.addEventListener('pointermove', function (e) {
                if (!drag) return;
                var dx = (e.clientX - drag.x) * drag.dir;
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
                // .NET rechnet mit „rechts = breiter“; bei rechts-nach-links sind die Pfeile vertauscht
                var key = !isRtl(sidebar) ? e.key : e.key === 'ArrowLeft' ? 'ArrowRight' : e.key === 'ArrowRight' ? 'ArrowLeft' : e.key;
                dotnetRef.invokeMethodAsync('OnSeparatorKey', key);
            });
        }
    };
})();

// Dialoge: Esc schließt den obersten offenen Dialog – für alle Dialoge gleich, ohne eigene Tastenbehandlung je Dialog.
// Ein Dialog trägt data-ss-dialog am äußersten Element, sein „Schließen“ bzw. „Abbrechen“ data-ss-dialog-close; Esc
// löst genau diesen Knopf aus (gleiche Wirkung wie ein Klick, z. B. Vorschau verwerfen). Danach steht der Fokus wieder
// auf dem Element, das vor dem Dialog den Fokus hatte, sonst auf ☰. Offene Menüs fangen Esc vorher selbst ab
// (attachMenu); Vortrag und Vollbild-Video sind keine Dialoge und behalten ihr eigenes Esc.
(function () {
    var lastOutside = null;

    function openDialogs() { return document.querySelectorAll('[data-ss-dialog]'); }

    document.addEventListener('focusin', function (e) {
        if (e.target && e.target.closest && !e.target.closest('[data-ss-dialog]')) lastOutside = e.target;
    }, true);

    function restoreFocus() {
        var active = document.activeElement;
        if (openDialogs().length > 0 && active && active !== document.body && active.isConnected) return;
        var target = lastOutside && lastOutside.isConnected ? lastOutside : document.getElementById('ss-appmenu-trigger');
        if (target) target.focus({ preventScroll: true });
    }

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape' || e.defaultPrevented || e.isComposing || e.ctrlKey || e.altKey || e.metaKey || e.shiftKey) return;
        var dialogs = openDialogs();
        if (dialogs.length === 0) return;
        var dialog = dialogs[dialogs.length - 1];
        var close = dialog.querySelector('[data-ss-dialog-close]');
        if (!close) return;
        e.preventDefault();
        e.stopPropagation();
        close.click();
        // Blazor entfernt den Dialog erst nach dem nächsten Zeichnen
        var tries = 0;
        (function wait() {
            if (dialog.isConnected && tries++ < 30) { requestAnimationFrame(wait); return; }
            restoreFocus();
        })();
    });
})();

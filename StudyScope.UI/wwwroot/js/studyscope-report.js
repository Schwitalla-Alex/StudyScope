// Berichtsansicht (ReportView.razor): Der Bericht läuft in einem <iframe srcdoc>. Der Knopf „▶ Video abspielen“ im
// Video-Abschnitt meldet sich per postMessage; hier wird die Meldung (nur aus diesem Rahmen) an .NET weitergegeben.
window.studyScopeReport = (function () {
    var handler = null;
    return {
        listen: function (dotnetRef) {
            if (handler) window.removeEventListener('message', handler);
            handler = function (e) {
                var frame = document.getElementById('rp-frame');
                if (!frame || e.source !== frame.contentWindow) return;
                var d = e.data;
                if (d && d.type === 'studyscope-play-video' && typeof d.id === 'string')
                    dotnetRef.invokeMethodAsync('PlayVideoFromReport', d.id);
            };
            window.addEventListener('message', handler);
        },
        stop: function () {
            if (handler) window.removeEventListener('message', handler);
            handler = null;
        }
    };
})();

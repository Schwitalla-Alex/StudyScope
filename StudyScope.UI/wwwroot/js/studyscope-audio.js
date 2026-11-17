// Wiedergabe erzeugter Audiodateien (WAV) für Audio-Beiträge. Die Datei kommt als Stream aus .NET und wird als
// Blob-URL in einem globalen Audio-Element abgespielt, damit die Wiedergabe beim Reiterwechsel weiterläuft.
// Position und Ende meldet das Skript an BrowserAudioPlayer.OnPosition; die Ablaufsteuerung liegt im AudioViewModel.
window.studyScopeAudio = (function () {
    "use strict";

    var audio = null;
    var url = null;
    var dotnet = null;

    function report(ended) {
        if (!dotnet || !audio) return;
        dotnet.invokeMethodAsync("OnPosition", audio.currentTime || 0, !!ended).catch(function () { });
    }

    function ensure() {
        if (audio) return audio;
        audio = new Audio();
        audio.preload = "auto";
        audio.addEventListener("timeupdate", function () { report(false); });
        audio.addEventListener("seeked", function () { report(false); });
        audio.addEventListener("ended", function () { report(true); });
        return audio;
    }

    async function load(streamRef, mimeType, reference) {
        ensure();
        dotnet = reference;
        var buffer = await streamRef.arrayBuffer();
        var blob = new Blob([buffer], { type: mimeType || "audio/wav" });
        audio.pause();
        if (url) URL.revokeObjectURL(url);
        url = URL.createObjectURL(blob);
        audio.src = url;
        audio.load();
    }

    function waitForMetadata() {
        return new Promise(function (resolve) {
            if (audio.readyState >= 1) { resolve(); return; }
            var done = false;
            function finish() {
                if (done) return;
                done = true;
                audio.removeEventListener("loadedmetadata", finish);
                resolve();
            }
            audio.addEventListener("loadedmetadata", finish);
            setTimeout(finish, 3000);
        });
    }

    async function play(startSeconds, rate) {
        ensure();
        await waitForMetadata();
        try { audio.currentTime = Math.max(0, startSeconds || 0); } catch (e) { /* Position später */ }
        audio.playbackRate = rate || 1;
        await audio.play();
    }

    function pause() { if (audio) audio.pause(); }
    async function resume() { if (audio) await audio.play(); }
    function stop() { if (audio) audio.pause(); }
    function setRate(rate) { if (audio) audio.playbackRate = rate || 1; }

    // Beim Schließen bzw. Neuladen der Seite nicht weiterspielen
    window.addEventListener("pagehide", stop);
    window.addEventListener("beforeunload", stop);

    return { load: load, play: play, pause: pause, resume: resume, stop: stop, setRate: setRate };
})();

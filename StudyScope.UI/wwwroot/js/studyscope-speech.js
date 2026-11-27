// Systemstimme für Audio-Beiträge: dünne Hülle um die Web Speech API (speechSynthesis) der WebView.
// Die Ablaufsteuerung (Segmente, Vor/Zurück, Hervorhebung) liegt im AudioViewModel; hier wird nur gesprochen.
window.studyScopeSpeech = (function () {
    "use strict";

    function isSupported() {
        return typeof window.speechSynthesis !== "undefined" && typeof window.SpeechSynthesisUtterance !== "undefined";
    }

    // Stimmen werden in Chromium/WebView2 oft erst nach "voiceschanged" geliefert
    function loadVoices(timeoutMs) {
        return new Promise(function (resolve) {
            if (!isSupported()) { resolve([]); return; }
            var voices = window.speechSynthesis.getVoices();
            if (voices && voices.length) { resolve(voices); return; }

            var done = false;
            function finish() {
                if (done) return;
                done = true;
                window.speechSynthesis.removeEventListener("voiceschanged", finish);
                resolve(window.speechSynthesis.getVoices() || []);
            }
            window.speechSynthesis.addEventListener("voiceschanged", finish);
            setTimeout(finish, timeoutMs || 2000);
        });
    }

    async function check() {
        if (!isSupported()) return { supported: false, voices: [] };
        var voices = await loadVoices(2000);
        return {
            supported: true,
            voices: voices.map(function (v) {
                return { id: v.voiceURI || v.name, name: v.name, lang: v.lang || "", local: !!v.localService, isDefault: !!v["default"] };
            })
        };
    }

    // Sprachteil einer Angabe: "en-GB" / "en_US" -> "en"
    function languagePart(tag) {
        return String(tag || "").split(/[-_]/)[0].toLowerCase();
    }

    // Gewählte Stimme, sonst eine Stimme der Sprache (offline zuerst); lang = "de-DE", "en-US" ...
    function findVoice(voiceId, lang) {
        var voices = window.speechSynthesis.getVoices() || [];
        if (voiceId) {
            var chosen = voices.find(function (v) { return (v.voiceURI || v.name) === voiceId; });
            if (chosen) return chosen;
        }
        var wanted = languagePart(lang || "de-DE");
        function matches(v) { return languagePart(v.lang) === wanted; }
        return voices.find(function (v) { return matches(v) && v.localService; })
            || voices.find(matches)
            || null;
    }

    // Spricht einen Text in der Sprache lang ("de-DE", "en-US"); meldet das Ende über
    // dotnetRef.OnSpeechEnded(id, vollständig, fehler)
    function speak(dotnetRef, id, text, voiceId, rate, lang) {
        if (!isSupported()) {
            dotnetRef.invokeMethodAsync("OnSpeechEnded", id, false, "not-supported");
            return;
        }

        window.speechSynthesis.cancel();
        var utterance = new SpeechSynthesisUtterance(text);
        var voice = findVoice(voiceId, lang);
        if (voice) {
            utterance.voice = voice;
            utterance.lang = voice.lang;
        } else {
            utterance.lang = lang || "de-DE";
        }
        utterance.rate = rate || 1;

        var reported = false;
        function report(completed, error) {
            if (reported) return;
            reported = true;
            dotnetRef.invokeMethodAsync("OnSpeechEnded", id, completed, error);
        }

        utterance.onend = function () { report(true, null); };
        utterance.onerror = function (e) {
            var code = e && e.error ? e.error : "unknown";
            // Abbruch durch cancel() (Stopp, Vor/Zurück) ist kein Fehler
            report(false, code === "canceled" || code === "interrupted" ? null : code);
        };

        // Kleine Verzögerung nach cancel(): sonst verschluckt Chromium gelegentlich die neue Äußerung
        setTimeout(function () { window.speechSynthesis.speak(utterance); }, 30);
    }

    function pause() { if (isSupported()) window.speechSynthesis.pause(); }
    function resume() { if (isSupported()) window.speechSynthesis.resume(); }
    function stop() { if (isSupported()) window.speechSynthesis.cancel(); }

    // Beim Schließen bzw. Neuladen der Seite nicht weitersprechen
    window.addEventListener("pagehide", stop);
    window.addEventListener("beforeunload", stop);

    return { check: check, speak: speak, pause: pause, resume: resume, stop: stop };
})();

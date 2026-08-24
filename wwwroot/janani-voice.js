// wwwroot/janani-voice.js
// Web Speech API wrapper for the VoiceInput Blazor component.
// Called via JS interop from VoiceInput.razor.

window.bloomVoice = (() => {
    let recognition = null;

    return {
        isSupported: () =>
            typeof window !== 'undefined' &&
            ('SpeechRecognition' in window || 'webkitSpeechRecognition' in window),

        start: (dotNetRef, lang) => {
            const SpeechRecognition =
                window.SpeechRecognition || window.webkitSpeechRecognition;
            if (!SpeechRecognition) return;

            recognition = new SpeechRecognition();
            recognition.lang = lang || 'en-US';
            recognition.continuous = false;
            recognition.interimResults = false;
            recognition.maxAlternatives = 1;

            recognition.onresult = (event) => {
                const transcript = event.results[0][0].transcript;
                dotNetRef.invokeMethodAsync('ReceiveTranscript', transcript);
            };

            recognition.onend = () => {
                dotNetRef.invokeMethodAsync('OnListeningEnd');
            };

            recognition.onerror = () => {
                dotNetRef.invokeMethodAsync('OnListeningEnd');
            };

            recognition.start();
        },

        stop: () => {
            if (recognition) {
                recognition.stop();
                recognition = null;
            }
        }
    };
})();

// Read-aloud for Elder Mode — a large chunk of the target audience benefits
// from hearing an answer as well as (or instead of) reading it. Plain Web
// Speech API synthesis, no server round-trip.
window.bloomSpeak = {
    isSupported: () => typeof window !== 'undefined' && 'speechSynthesis' in window,

    speak: (text, lang) => {
        if (!('speechSynthesis' in window)) return;
        window.speechSynthesis.cancel();
        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = lang || 'en-US';
        utterance.rate = 0.9;
        window.speechSynthesis.speak(utterance);
    },

    stop: () => {
        if ('speechSynthesis' in window) window.speechSynthesis.cancel();
    }
};

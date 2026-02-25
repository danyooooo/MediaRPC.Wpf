// Content script injected into all pages to monitor Media Session API via inject.js

// Inject inject.js into the main world
const script = document.createElement('script');
script.src = chrome.runtime.getURL('inject.js');
script.onload = function () {
    this.remove();
};
(document.head || document.documentElement).appendChild(script);

// Listen for messages from the injected script and forward to background.js
window.addEventListener('message', (event) => {
    if (event.source !== window || !event.data || event.data.source !== 'mediarpc-inject') return;

    if (event.data.type === 'MEDIA_STATE') {
        const data = event.data.payload;

        const currentState = {
            title: data.title || document.title, // improved fallback
            artist: data.artist || new URL(data.url).hostname,
            album: data.album || "",
            artwork: data.artwork,
            isPlaying: data.playbackState === "playing",
            url: data.url,
            position: data.positionState ? data.positionState.position : 0,
            duration: data.positionState ? data.positionState.duration : 0,
            supportedActions: data.supportedActions || []
        };

        chrome.runtime.sendMessage({
            type: "MEDIA_SESSION_UPDATE",
            payload: currentState
        });
    }
});

// Listen for commands from background.js and forward to injected script
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.type === "ACTION") {
        window.postMessage({
            source: 'mediarpc-content',
            type: 'ACTION',
            action: message.data ? message.data.action : null
        }, '*');
    }
});

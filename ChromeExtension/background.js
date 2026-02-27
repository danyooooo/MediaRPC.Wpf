// Background script connecting to WebSocket server

const WS_URL = "ws://127.0.0.1:8765/";
let ws = null;
let reconnectTimer = null;
let messageQueue = [];

let sessionMode = 'normal';
(async () => {
    if (typeof chrome !== 'undefined' && chrome.extension?.inIncognitoContext) {
        sessionMode = 'incognito';
    } else if (typeof browser !== 'undefined' && browser.extension && typeof browser.extension.isAllowedIncognitoAccess === 'function') {
        const isPrivate = await browser.extension.isAllowedIncognitoAccess();
        sessionMode = isPrivate ? 'private' : 'normal';
    }
})();

function connectWebSocket() {
    if (ws && (ws.readyState === WebSocket.CONNECTING || ws.readyState === WebSocket.OPEN)) return;

    console.log("Connecting to WebSocket server:", WS_URL);
    ws = new WebSocket(WS_URL);

    ws.onopen = () => {
        console.log("Connected to WebSocket server.");
        if (reconnectTimer) clearTimeout(reconnectTimer);

        while (messageQueue.length > 0) {
            const msg = messageQueue.shift();
            try { ws.send(msg); } catch (e) { }
        }
    };

    ws.onmessage = (event) => {
        console.log("Received from server:", event.data);
        // Handle control commands (Play, Pause, etc) from MediaRPC back to the active tab
        try {
            const msg = JSON.parse(event.data);
            if (msg.type === "ACTION") {
                // Forward the action to the active tab
                chrome.tabs.query({ active: true }, (tabs) => {
                    if (tabs.length > 0) {
                        chrome.tabs.sendMessage(tabs[0].id, msg);
                    }
                });
            }
        } catch (err) {
            console.error("Error parsing message from server:", err);
        }
    };

    ws.onclose = () => {
        console.error("Disconnected from WebSocket server.");
        ws = null;
        // Attempt to reconnect after a delay
        reconnectTimer = setTimeout(connectWebSocket, 5000);
    };

    ws.onerror = (err) => {
        console.error("WebSocket error:", err);
    };
}

// Connect immediately
connectWebSocket();

// Listen for messages from content scripts
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.type === "MEDIA_SESSION_UPDATE") {
        const payload = {
            tabId: sender.tab ? sender.tab.id : 0,
            active: sender.tab ? sender.tab.active : false,
            sessionMode: sessionMode,
            mediaInfo: message.payload
        };

        const jsonStr = JSON.stringify({ type: "SESSION_UPDATE", data: payload });

        if (ws && ws.readyState === WebSocket.OPEN) {
            try {
                ws.send(jsonStr);
            } catch (err) {
                console.error("Error posting to WebSocket:", err);
            }
        } else {
            // Queue message if sleeping/connecting
            messageQueue.push(jsonStr);
            if (!ws || ws.readyState === WebSocket.CLOSED) {
                connectWebSocket();
            }
        }
    }
    return true; // async
});

// Notify server when active tab changes, so it knows which session is currently active visually
chrome.tabs.onActivated.addListener((activeInfo) => {
    const jsonStr = JSON.stringify({ type: "ACTIVE_TAB_CHANGED", data: { tabId: activeInfo.tabId } });
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(jsonStr);
    } else {
        messageQueue.push(jsonStr);
        if (!ws || ws.readyState === WebSocket.CLOSED) connectWebSocket();
    }
});

// Notify server when a tab is closed, to remove the session
chrome.tabs.onRemoved.addListener((tabId, removeInfo) => {
    const jsonStr = JSON.stringify({ type: "TAB_CLOSED", data: { tabId: tabId } });
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(jsonStr);
    } else {
        messageQueue.push(jsonStr);
        if (!ws || ws.readyState === WebSocket.CLOSED) connectWebSocket();
    }
});

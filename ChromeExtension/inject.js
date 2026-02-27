(function () {
    if (!('mediaSession' in navigator)) return;

    let currentMetadata = null;
    let currentPlaybackState = 'none';
    let currentPositionState = { duration: 0, playbackRate: 1, position: 0 };
    const actionHandlers = new Map();

    function broadcastState() {
        const payload = {
            title: currentMetadata ? currentMetadata.title : '',
            artist: currentMetadata ? currentMetadata.artist : '',
            album: currentMetadata ? currentMetadata.album : '',
            artwork: currentMetadata && currentMetadata.artwork && currentMetadata.artwork.length > 0
                ? currentMetadata.artwork[currentMetadata.artwork.length - 1].src
                : null,
            playbackState: currentPlaybackState,
            positionState: currentPositionState,
            url: window.location.href,
            supportedActions: Array.from(actionHandlers.keys())
        };

        window.postMessage({
            source: 'mediarpc-inject',
            type: 'MEDIA_STATE',
            payload: payload
        }, '*');
    }

    // Proxy metadata 
    const mediaSessionProto = Object.getPrototypeOf(navigator.mediaSession);

    const originalMetadataSet = Object.getOwnPropertyDescriptor(mediaSessionProto, 'metadata')?.set;
    const originalPlaybackStateSet = Object.getOwnPropertyDescriptor(mediaSessionProto, 'playbackState')?.set;

    if (originalMetadataSet) {
        Object.defineProperty(mediaSessionProto, 'metadata', {
            set: function (value) {
                currentMetadata = value;
                originalMetadataSet.call(this, value);
                broadcastState();
            },
            get: Object.getOwnPropertyDescriptor(mediaSessionProto, 'metadata').get
        });
    }

    if (originalPlaybackStateSet) {
        Object.defineProperty(mediaSessionProto, 'playbackState', {
            set: function (value) {
                currentPlaybackState = value;
                originalPlaybackStateSet.call(this, value);
                broadcastState();
            },
            get: Object.getOwnPropertyDescriptor(mediaSessionProto, 'playbackState').get
        });
    }

    const originalSetPositionState = navigator.mediaSession.setPositionState;
    if (originalSetPositionState) {
        navigator.mediaSession.setPositionState = function (state) {
            if (state) {
                currentPositionState = {
                    duration: state.duration || 0,
                    playbackRate: state.playbackRate || 1,
                    position: state.position || 0
                };
            }
            broadcastState();
            return originalSetPositionState.call(this, state);
        };
    }

    const originalSetActionHandler = navigator.mediaSession.setActionHandler;
    if (originalSetActionHandler) {
        navigator.mediaSession.setActionHandler = function (action, handler) {
            if (handler) {
                actionHandlers.set(action, handler);
            } else {
                actionHandlers.delete(action);
            }
            broadcastState();
            return originalSetActionHandler.call(this, action, handler);
        };
    }

    window.addEventListener('message', (event) => {
        if (event.source !== window || !event.data || event.data.source !== 'mediarpc-content') return;

        if (event.data.type === 'ACTION' && event.data.action) {
            const handler = actionHandlers.get(event.data.action);
            if (handler) {
                handler({ action: event.data.action });
            }
        }
    });

    // Seed initial state just in case it was already playing before we injected
    if (navigator.mediaSession.metadata) {
        currentMetadata = navigator.mediaSession.metadata;
    }
    if (navigator.mediaSession.playbackState) {
        currentPlaybackState = navigator.mediaSession.playbackState;
    }

    setInterval(() => {
        // Attempt to guess playback state if the page didn't explicitly set it to 'playing'
        // (Some pages construct a media session but fail to update playbackState)
        const media = document.querySelector('video, audio');
        if (media && !isNaN(media.duration) && !isNaN(media.currentTime)) {
            if (!media.paused && currentPlaybackState !== 'playing') {
                currentPlaybackState = 'playing';
            } else if (media.paused && currentPlaybackState === 'playing') {
                currentPlaybackState = 'paused';
            }
        }

        if (currentPlaybackState === 'playing') {
            // Try updating from standard API first
            try {
                if (navigator.mediaSession.getPositionState) {
                    const pos = navigator.mediaSession.getPositionState();
                    if (pos) {
                        currentPositionState = {
                            duration: pos.duration || 0,
                            playbackRate: pos.playbackRate || 1,
                            position: pos.position || 0
                        };
                    }
                }
            } catch (e) { }

            // Accurate live fallback via media element
            if (media && !isNaN(media.duration) && !isNaN(media.currentTime)) {
                currentPositionState = {
                    duration: media.duration || currentPositionState.duration || 0,
                    playbackRate: media.playbackRate || currentPositionState.playbackRate || 1,
                    position: media.currentTime || currentPositionState.position || 0
                };
            } else if (currentPositionState.duration > 0) {
                // Manual ticking if no media element found
                currentPositionState.position += currentPositionState.playbackRate;
                if (currentPositionState.position > currentPositionState.duration) {
                    currentPositionState.position = currentPositionState.duration;
                }
            }

            broadcastState();
        }
    }, 1000);

    console.log("[MediaRPC] Media Session API fully intercepted.");
})();

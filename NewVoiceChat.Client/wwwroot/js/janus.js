let janus = null;
let audioHandle = null;
let dotNetRef = null;
let roomId = null;
let myId = null;

export function initialize(serverUrl, dotNetReference) {
    dotNetRef = dotNetReference;
    
    return new Promise((resolve, reject) => {
        if (!Janus.isWebrtcSupported()) {
            reject('WebRTC is not supported');
            return;
        }

        Janus.init({
            debug: true,
            callback: function() {
                janus = new Janus({
                    server: serverUrl,
                    success: function() {
                        attachAudioBridgePlugin();
                        resolve();
                    },
                    error: function(error) {
                        console.error('Janus error:', error);
                        dotNetRef.invokeMethodAsync('OnJanusError', error);
                        reject(error);
                    },
                    destroyed: function() {
                        console.log('Janus destroyed');
                    }
                });
            }
        });
    });
}

function attachAudioBridgePlugin() {
    janus.attach({
        plugin: 'janus.plugin.audiobridge',
        opaqueId: 'audiobridge-' + Janus.randomString(12),
        success: function(pluginHandle) {
            audioHandle = pluginHandle;
            console.log('Plugin attached:', pluginHandle);
            dotNetRef.invokeMethodAsync('OnHandleCreated', pluginHandle.id);
        },
        error: function(error) {
            console.error('Error attaching plugin:', error);
            dotNetRef.invokeMethodAsync('OnJanusError', 'Error attaching to audiobridge plugin: ' + error);
        },
        onmessage: function(msg, jsep) {
            handleMessage(msg, jsep);
        },
        onremotestream: function(stream) {
            // Create or update audio element for remote stream
            let audioElement = document.getElementById('remoteAudio');
            if (!audioElement) {
                audioElement = document.createElement('audio');
                audioElement.id = 'remoteAudio';
                audioElement.autoplay = true;
                document.body.appendChild(audioElement);
            }
            Janus.attachMediaStream(audioElement, stream);
        },
        oncleanup: function() {
            console.log('Got a cleanup notification');
        }
    });
}

function handleMessage(msg, jsep) {
    console.log('Got a message:', msg);

    if (msg.audiobridge === 'joined') {
        myId = msg.id;
        if (jsep) {
            audioHandle.handleRemoteJsep({ jsep: jsep });
        }
        // Start publishing our audio
        publishOwnFeed();
    } else if (msg.audiobridge === 'event') {
        if (msg.participants) {
            // Handle participants list
            msg.participants.forEach(participant => {
                dotNetRef.invokeMethodAsync('OnParticipantJoined', JSON.stringify({
                    id: participant.id,
                    display: participant.display || 'Anonymous'
                }));
            });
        } else if (msg.leaving) {
            // Handle participant leaving
            dotNetRef.invokeMethodAsync('OnParticipantLeft', JSON.stringify({
                id: msg.leaving,
                display: 'Anonymous'
            }));
        }
    }

    if (jsep) {
        audioHandle.handleRemoteJsep({ jsep: jsep });
    }
}

function publishOwnFeed() {
    navigator.mediaDevices.getUserMedia({ audio: true, video: false })
        .then(function(stream) {
            Janus.debug('Got user media');
            audioHandle.createOffer({
                media: { audio: true, video: false },
                success: function(jsep) {
                    Janus.debug('Got SDP:', jsep);
                    var publish = { request: 'configure', muted: false };
                    audioHandle.send({ message: publish, jsep: jsep });
                },
                error: function(error) {
                    Janus.error('WebRTC error:', error);
                    dotNetRef.invokeMethodAsync('OnJanusError', 'WebRTC error: ' + error);
                }
            });
        })
        .catch(function(error) {
            Janus.error('getUserMedia error:', error);
            dotNetRef.invokeMethodAsync('OnJanusError', 'Media error: ' + error);
        });
}

export function joinRoom(newRoomId) {
    if (!audioHandle) {
        throw new Error('Janus not initialized');
    }

    roomId = newRoomId;
    const register = {
        request: 'join',
        room: parseInt(roomId),
        display: 'User-' + Janus.randomString(6)
    };

    audioHandle.send({ message: register });
}

export function leaveRoom() {
    if (!audioHandle || !roomId) {
        return;
    }

    const leave = {
        request: 'leave',
        room: parseInt(roomId)
    };

    audioHandle.send({ message: leave });
    roomId = null;
} 
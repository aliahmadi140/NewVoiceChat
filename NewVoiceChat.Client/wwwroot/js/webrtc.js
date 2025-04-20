let peerConnections = new Map(); // Track peer connections by user ID
let dotNetObject;
let queuedIceCandidates = new Map(); // Track queued ICE candidates by user ID
let audioElements = new Map(); // Track audio elements by user ID

export function initialize(dotNetObj) {
    dotNetObject = dotNetObj;
}

export async function createPeerConnection(roomId, userId) {
    try {
        // Close any existing connection for this user
        if (peerConnections.has(userId)) {
            await closePeerConnection(userId);
        }

        const configuration = {
            iceServers: [
                { urls: 'stun:stun.l.google.com:19302' }
            ]
        };

        const peerConnection = new RTCPeerConnection(configuration);
        peerConnections.set(userId, peerConnection);
        queuedIceCandidates.set(userId, []);

        // Set up event handlers
        peerConnection.onicecandidate = async (event) => {
            if (event.candidate) {
                await dotNetObject.invokeMethodAsync('OnIceCandidate', 
                    event.candidate.candidate,
                    event.candidate.sdpMid,
                    event.candidate.sdpMLineIndex,
                    userId
                );
            }
        };

        peerConnection.ontrack = (event) => {
            if (event.track.kind === 'audio') {
                const streamUserId = event.streams[0].id;
                let audioElement = audioElements.get(streamUserId);
                
                if (!audioElement) {
                    audioElement = document.createElement('audio');
                    audioElement.id = `audio-${streamUserId}`;
                    audioElement.autoplay = true;
                    audioElement.controls = true;
                    document.body.appendChild(audioElement);
                    audioElements.set(streamUserId, audioElement);
                }

                audioElement.srcObject = event.streams[0];
                console.log('Audio track added for user:', streamUserId);
            }
        };

        // Add local audio track
        const stream = await navigator.mediaDevices.getUserMedia({ 
            audio: {
                echoCancellation: true,
                noiseSuppression: true,
                autoGainControl: true
            }, 
            video: false 
        });

        stream.getAudioTracks().forEach(track => {
            console.log('Local audio track:', track.label, track.enabled, track.muted);
            peerConnection.addTrack(track, stream);
        });

        // Create and set local description
        const offer = await peerConnection.createOffer();
        await peerConnection.setLocalDescription(offer);

        // Send offer to server
        await dotNetObject.invokeMethodAsync('SendOffer', {
            type: offer.type,
            sdp: offer.sdp,
            roomId: roomId,
            userId: userId
        });
    } catch (error) {
        console.error('Error creating peer connection:', error);
        await dotNetObject.invokeMethodAsync('HandleWebRtcError', 'Failed to initialize audio connection');
    }
}

export async function handleOffer(offer, userId) {
    try {
        let peerConnection = peerConnections.get(userId);
        
        if (!peerConnection) {
            const configuration = {
                iceServers: [
                    { urls: 'stun:stun.l.google.com:19302' }
                ]
            };
            peerConnection = new RTCPeerConnection(configuration);
            peerConnections.set(userId, peerConnection);
            queuedIceCandidates.set(userId, []);

            // Set up event handlers
            peerConnection.onicecandidate = async (event) => {
                if (event.candidate) {
                    await dotNetObject.invokeMethodAsync('OnIceCandidate', 
                        event.candidate.candidate,
                        event.candidate.sdpMid,
                        event.candidate.sdpMLineIndex,
                        userId
                    );
                }
            };

            peerConnection.ontrack = (event) => {
                if (event.track.kind === 'audio') {
                    const streamUserId = event.streams[0].id;
                    let audioElement = audioElements.get(streamUserId);
                    
                    if (!audioElement) {
                        audioElement = document.createElement('audio');
                        audioElement.id = `audio-${streamUserId}`;
                        audioElement.autoplay = true;
                        audioElement.controls = true;
                        document.body.appendChild(audioElement);
                        audioElements.set(streamUserId, audioElement);
                    }

                    audioElement.srcObject = event.streams[0];
                    console.log('Audio track added for user:', streamUserId);
                }
            };

            // Add local audio track
            const stream = await navigator.mediaDevices.getUserMedia({ 
                audio: {
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                }, 
                video: false 
            });

            stream.getAudioTracks().forEach(track => {
                console.log('Local audio track:', track.label, track.enabled, track.muted);
                peerConnection.addTrack(track, stream);
            });
        }

        // Only set remote description if we're in a stable state
        if (peerConnection.signalingState === 'stable') {
            await peerConnection.setRemoteDescription(new RTCSessionDescription(offer));
            const answer = await peerConnection.createAnswer();
            await peerConnection.setLocalDescription(answer);

            // Process any queued ICE candidates
            const queuedCandidates = queuedIceCandidates.get(userId) || [];
            for (const candidate of queuedCandidates) {
                try {
                    await peerConnection.addIceCandidate(new RTCIceCandidate(candidate));
                } catch (error) {
                    console.warn('Error adding queued ICE candidate:', error);
                }
            }
            queuedIceCandidates.set(userId, []);

            await dotNetObject.invokeMethodAsync('SendAnswer', {
                type: answer.type,
                sdp: answer.sdp,
                roomId: offer.roomId,
                userId: userId
            });
        } else {
            console.warn('Cannot handle offer in current state:', peerConnection.signalingState);
        }
    } catch (error) {
        console.error('Error handling offer:', error);
        await dotNetObject.invokeMethodAsync('HandleWebRtcError', 'Error handling incoming call');
    }
}

export async function handleAnswer(answer, userId) {
    try {
        const peerConnection = peerConnections.get(userId);
        if (!peerConnection) {
            console.warn('No peer connection found for user:', userId);
            return;
        }

        // Only set remote description if we're in the correct state
        if (peerConnection.signalingState === 'have-local-offer') {
            await peerConnection.setRemoteDescription(new RTCSessionDescription(answer));

            // Process any queued ICE candidates
            const queuedCandidates = queuedIceCandidates.get(userId) || [];
            for (const candidate of queuedCandidates) {
                try {
                    await peerConnection.addIceCandidate(new RTCIceCandidate(candidate));
                } catch (error) {
                    console.warn('Error adding queued ICE candidate:', error);
                }
            }
            queuedIceCandidates.set(userId, []);
        } else {
            console.warn('Cannot handle answer in current state:', peerConnection.signalingState);
        }
    } catch (error) {
        console.error('Error setting remote description:', error);
        await dotNetObject.invokeMethodAsync('HandleWebRtcError', 'Error setting remote description');
    }
}

export async function addIceCandidate(candidate, userId) {
    try {
        const peerConnection = peerConnections.get(userId);
        if (!peerConnection) {
            console.warn('No peer connection found for user:', userId);
            return;
        }

        if (peerConnection.remoteDescription) {
            await peerConnection.addIceCandidate(new RTCIceCandidate(candidate));
        } else {
            // Queue the ICE candidate if remote description is not set yet
            const queuedCandidates = queuedIceCandidates.get(userId) || [];
            queuedCandidates.push(candidate);
            queuedIceCandidates.set(userId, queuedCandidates);
        }
    } catch (error) {
        console.error('Error adding ICE candidate:', error);
        await dotNetObject.invokeMethodAsync('HandleWebRtcError', 'Error adding ICE candidate');
    }
}

export function closePeerConnection(userId) {
    const peerConnection = peerConnections.get(userId);
    if (peerConnection) {
        // Close all audio tracks for this user
        const audioElement = audioElements.get(userId);
        if (audioElement) {
            if (audioElement.srcObject) {
                audioElement.srcObject.getTracks().forEach(track => track.stop());
            }
            audioElement.remove();
            audioElements.delete(userId);
        }

        peerConnection.close();
        peerConnections.delete(userId);
        queuedIceCandidates.delete(userId);
    }
} 
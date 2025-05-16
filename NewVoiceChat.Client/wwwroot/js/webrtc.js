let localStream = null;
let peerConnection = null;
let dotNetObjectReference = null;
let currentRoomId = null;

const iceServers = {
    iceServers: [
        { urls: 'stun:stun.l.google.com:19302' }
    ]
};

window.initializeWebRTC = function(dotNetRef) {
    console.log('[WebRTC] Initializing WebRTC module');
    dotNetObjectReference = dotNetRef;
    return true;
};

window.createPeerConnection = async function(roomId) {
    console.log(`[WebRTC] Creating peer connection for room: ${roomId}`);
    currentRoomId = roomId;

    if (peerConnection) {
        console.log('[WebRTC] Closing existing peer connection before creating a new one.');
        await closeWebRTCPeerConnection();
    }
    
    peerConnection = new RTCPeerConnection(iceServers);

    peerConnection.onicecandidate = (event) => {
        if (event.candidate && dotNetObjectReference) {
            console.log('[WebRTC] Sending ICE candidate to C#:', event.candidate);
            dotNetObjectReference.invokeMethodAsync('OnIceCandidate', event.candidate.candidate, event.candidate.sdpMid, event.candidate.sdpMLineIndex);
        }
    };

    peerConnection.ontrack = (event) => {
        console.log('[WebRTC] Received remote track:', event.streams[0]);
        // If you have an <audio> element to play remote audio, attach the stream here
        // e.g., const remoteAudio = document.getElementById('remoteAudio');
        // if (remoteAudio) remoteAudio.srcObject = event.streams[0];
    };

    try {
        localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
        console.log('[WebRTC] Got user media (microphone access granted)');
        localStream.getTracks().forEach(track => {
            peerConnection.addTrack(track, localStream);
        });
        console.log('[WebRTC] Added local stream tracks to peer connection');

        const offer = await peerConnection.createOffer();
        await peerConnection.setLocalDescription(offer);
        console.log('[WebRTC] Offer created and local description set:', offer);
        if (dotNetObjectReference) {
            await dotNetObjectReference.invokeMethodAsync('SendOffer', { 
                Sdp: offer.sdp, 
                Type: offer.type 
            });
        }
        
    } catch (error) {
        console.error('[WebRTC] Error in createPeerConnection (getUserMedia or offer creation):', error);
        if (dotNetObjectReference) {
            dotNetObjectReference.invokeMethodAsync('HandleWebRtcError', `Error accessing media devices or creating offer: ${error.message}`);
        }
    }
};

window.handleOffer = async function(offerSdp) {
    console.log('[WebRTC] Received offer, handling it:', offerSdp);
    if (!peerConnection) {
        console.error('[WebRTC] PeerConnection not initialized when trying to handle offer.');
        if (dotNetObjectReference) {
             dotNetObjectReference.invokeMethodAsync('HandleWebRtcError', 'PeerConnection not ready for offer.');
        }
        return;
    }

    try {
        await peerConnection.setRemoteDescription(new RTCSessionDescription({ type: offerSdp.type, sdp: offerSdp.sdp }));
        console.log('[WebRTC] Remote description (offer) set.');

        if (!localStream) {
            console.log('[WebRTC] Local stream not available, attempting to get user media before creating answer.');
            localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
            localStream.getTracks().forEach(track => {
                peerConnection.addTrack(track, localStream);
            });
            console.log('[WebRTC] Got user media and added tracks while handling offer.');
        }
        
        const answer = await peerConnection.createAnswer();
        await peerConnection.setLocalDescription(answer);
        console.log('[WebRTC] Answer created and local description set:', answer);
        if (dotNetObjectReference) {
            await dotNetObjectReference.invokeMethodAsync('SendAnswer', { 
                Sdp: answer.sdp, 
                Type: answer.type
            });
        }
    } catch (error) {
        console.error('[WebRTC] Error handling offer or creating answer:', error);
        if (dotNetObjectReference) {
            dotNetObjectReference.invokeMethodAsync('HandleWebRtcError', `Error handling offer: ${error.message}`);
        }
    }
};

window.handleAnswer = async function(answerSdp) {
    console.log('[WebRTC] Received answer, handling it:', answerSdp);
    if (!peerConnection) {
        console.error('[WebRTC] PeerConnection not initialized when trying to handle answer.');
         if (dotNetObjectReference) {
             dotNetObjectReference.invokeMethodAsync('HandleWebRtcError', 'PeerConnection not ready for answer.');
        }
        return;
    }
    try {
        await peerConnection.setRemoteDescription(new RTCSessionDescription({ type: answerSdp.type, sdp: answerSdp.sdp }));
        console.log('[WebRTC] Remote description (answer) set.');
    } catch (error) {
        console.error('[WebRTC] Error handling answer:', error);
        if (dotNetObjectReference) {
            dotNetObjectReference.invokeMethodAsync('HandleWebRtcError', `Error handling answer: ${error.message}`);
        }
    }
};

window.addIceCandidate = async function(candidateInfo) {
    console.log('[WebRTC] Adding received ICE candidate:', candidateInfo);
    if (!peerConnection) {
        console.error('[WebRTC] PeerConnection not initialized when trying to add ICE candidate.');
        return;
    }
    try {
        const rtcIceCandidate = new RTCIceCandidate({
            candidate: candidateInfo.candidate,
            sdpMid: candidateInfo.sdpMid,
            sdpMLineIndex: candidateInfo.sdpMLineIndex
        });
        await peerConnection.addIceCandidate(rtcIceCandidate);
        console.log('[WebRTC] ICE candidate added.');
    } catch (error) {
        console.error('[WebRTC] Error adding ICE candidate:', error);
        if (dotNetObjectReference) {
            dotNetObjectReference.invokeMethodAsync('HandleWebRtcError', `Error adding ICE candidate: ${error.message}`);
        }
    }
};

window.closeWebRTCPeerConnection = async function() {
    console.log('[WebRTC] Closing peer connection.');
    if (localStream) {
        localStream.getTracks().forEach(track => track.stop());
        localStream = null;
        console.log('[WebRTC] Local stream tracks stopped.');
    }
    if (peerConnection) {
        peerConnection.close();
        peerConnection = null;
        console.log('[WebRTC] Peer connection closed.');
    }
}; 
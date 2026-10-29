// A two-way WebRTC call to a go2rtc stream: the intercom's video and sound in, this PC's
// microphone out over the unit's RTSP talk-back channel. Signalling is go2rtc's WebSocket
// (/api/ws), the same exchange its own video-rtc player uses.
//
// Why not go2rtc's player page in an iframe, as the camera views do: the page inside the
// frame is http://<server>:1984, which is not a secure origin, so the browser will not give
// it a microphone. Here the peer connection lives in the app's own page instead.

export interface GoRtcCall {
  hangup: () => void;
  setMicMuted: (muted: boolean) => void;
  // Swap the microphone mid-call without renegotiating. Resolves false if it couldn't be opened.
  setMicDevice: (deviceId: string) => Promise<boolean>;
  // 0..1 loudness of what the microphone is sending right now; null when there is no microphone.
  // Packets flow even when the mic hears nothing, so this — not bytes sent — shows it is live.
  micLevel: () => Promise<number | null>;
}

export interface GoRtcCallOptions {
  videoBaseUrl: string;
  stream: string;
  video: HTMLVideoElement;
  micDeviceId?: string | null;
  // Called once with whether the microphone could be opened (false = listen only).
  onMic?: (ok: boolean, reason?: string) => void;
  onState?: (state: "connecting" | "connected" | "failed" | "closed") => void;
}

const micConstraints = (deviceId?: string | null): MediaStreamConstraints => ({
  audio: {
    ...(deviceId ? { deviceId: { exact: deviceId } } : {}),
    echoCancellation: true,
    noiseSuppression: true,
    autoGainControl: true,
  },
});

/** The PC's microphones. Labels are blank until the page has been allowed a microphone once. */
export async function listMicrophones(): Promise<MediaDeviceInfo[]> {
  if (!navigator.mediaDevices?.enumerateDevices) return [];
  return (await navigator.mediaDevices.enumerateDevices()).filter((d) => d.kind === "audioinput");
}

async function openMic(deviceId?: string | null): Promise<MediaStream> {
  try {
    return await navigator.mediaDevices.getUserMedia(micConstraints(deviceId));
  } catch (e) {
    // A remembered device that has since been unplugged: fall back to the default.
    if (deviceId) return navigator.mediaDevices.getUserMedia(micConstraints(null));
    throw e;
  }
}

export async function startGoRtcCall(opts: GoRtcCallOptions): Promise<GoRtcCall> {
  const { videoBaseUrl, stream, video, micDeviceId, onMic, onState } = opts;
  onState?.("connecting");

  let mic: MediaStream | null = null;
  if (typeof navigator.mediaDevices?.getUserMedia === "function") {
    try {
      mic = await openMic(micDeviceId);
      onMic?.(true);
    } catch (e) {
      onMic?.(false, e instanceof Error ? e.message : String(e));
    }
  } else {
    // No mediaDevices at all = the page is not a secure origin (see tauri.conf.json's
    // additionalBrowserArgs, which marks the server-hosted UI as one).
    onMic?.(false, "This page is not allowed to use the microphone");
  }

  const pc = new RTCPeerConnection({ iceServers: [{ urls: "stun:stun.l.google.com:19302" }] });
  // Microphone first, then video and audio — the order go2rtc's own player offers them in.
  let micTrack = mic?.getAudioTracks()[0] ?? null;
  const micSender = micTrack ? pc.addTransceiver(micTrack, { direction: "sendonly" }).sender : null;
  let muted = false;
  pc.addTransceiver("video", { direction: "recvonly" });
  pc.addTransceiver("audio", { direction: "recvonly" });

  const remote = new MediaStream();
  pc.ontrack = (ev) => {
    remote.addTrack(ev.track);
    if (video.srcObject !== remote) video.srcObject = remote;
    video.play().catch(() => {});
  };
  pc.onconnectionstatechange = () => {
    if (pc.connectionState === "connected") onState?.("connected");
    else if (pc.connectionState === "failed") onState?.("failed");
  };

  const wsUrl = `${videoBaseUrl.replace(/^http/, "ws").replace(/\/+$/, "")}/api/ws?src=${encodeURIComponent(stream)}`;
  const ws = new WebSocket(wsUrl);
  let closed = false;

  pc.onicecandidate = (ev) => {
    if (ws.readyState !== WebSocket.OPEN) return;
    ws.send(JSON.stringify({ type: "webrtc/candidate", value: ev.candidate ? ev.candidate.candidate : "" }));
  };

  ws.onopen = async () => {
    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    ws.send(JSON.stringify({ type: "webrtc/offer", value: pc.localDescription!.sdp }));
  };
  ws.onmessage = async (ev) => {
    const msg = JSON.parse(ev.data as string) as { type: string; value: string };
    if (msg.type === "webrtc/answer") {
      await pc.setRemoteDescription({ type: "answer", sdp: msg.value });
    } else if (msg.type === "webrtc/candidate" && msg.value) {
      await pc.addIceCandidate({ candidate: msg.value, sdpMid: "0" }).catch(() => {});
    } else if (msg.type === "error") {
      onState?.("failed");
    }
  };
  ws.onerror = () => {
    if (!closed && pc.connectionState !== "connected") onState?.("failed");
  };

  return {
    hangup() {
      if (closed) return;
      closed = true;
      ws.close();
      pc.close();
      micTrack?.stop();
      video.srcObject = null;
      onState?.("closed");
    },
    setMicMuted(m) {
      muted = m;
      if (micTrack) micTrack.enabled = !m;
    },
    async setMicDevice(deviceId) {
      if (!micSender) return false;
      try {
        const next = (await navigator.mediaDevices.getUserMedia(micConstraints(deviceId))).getAudioTracks()[0];
        next.enabled = !muted;
        await micSender.replaceTrack(next);
        micTrack?.stop();
        micTrack = next;
        return true;
      } catch {
        return false;
      }
    },
    async micLevel() {
      if (!micSender) return null;
      let level = 0;
      (await micSender.getStats()).forEach((r) => {
        if (r.type === "media-source" && r.kind === "audio" && typeof r.audioLevel === "number") level = r.audioLevel;
      });
      return level;
    },
  };
}

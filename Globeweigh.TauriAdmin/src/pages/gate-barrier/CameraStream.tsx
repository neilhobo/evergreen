interface Props {
  videoBaseUrl: string;
  stream: string;
}

// Embed go2rtc's own player page (stream.html) in an iframe. go2rtc negotiates WebRTC
// (sub-second) with an MSE fallback that survives Tailscale/VPN. Using the iframe (rather
// than loading go2rtc's web component cross-origin) sidesteps CORS and keeps this trivial —
// everything inside the frame is same-origin to the go2rtc server.
export default function CameraStream({ videoBaseUrl, stream }: Props) {
  const base = videoBaseUrl.replace(/\/+$/, "");
  const src = `${base}/stream.html?src=${encodeURIComponent(stream)}`;
  return (
    <iframe
      title="camera"
      src={src}
      style={{ width: "100%", height: "100%", border: "none", background: "#000" }}
      allow="autoplay; fullscreen"
    />
  );
}

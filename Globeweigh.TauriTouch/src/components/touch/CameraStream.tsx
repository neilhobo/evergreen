interface Props {
  videoBaseUrl: string;
  stream: string;
}

// Embed go2rtc's own player page (stream.html) in an iframe — the same approach as the
// TauriAdmin Gate/Barrier view. go2rtc negotiates WebRTC (sub-second) with an MSE fallback
// that survives Tailscale/VPN; the iframe keeps everything inside same-origin to the go2rtc
// server, sidestepping CORS.
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

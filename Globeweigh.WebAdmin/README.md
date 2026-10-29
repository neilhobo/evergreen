# Globeweigh.WebAdmin

Experimental web-tech UI for the **Gate/Barrier (Web)** tab in `Globeweigh.Admin`.
React + Vite + TypeScript, reusing the styling from `SpiceOLife\Globeweigh.TauriAdmin`
(`App.css`). No Tauri — it is hosted inside a WPF **WebView2** control. All data and relay
calls go through **Globeweigh.Api** (`..\Globeweigh.Api`); live weight is read directly from
`GlobeweighService` on `:8765`.

## Run (development)

1. **API** — from `..\Globeweigh.Api`:
   ```
   dotnet run --urls http://localhost:5099
   ```
   Swagger: http://localhost:5099/swagger
2. **Web UI** — from here:
   ```
   npm install
   npm run dev
   ```
   Dev server: http://localhost:1420 (the URL the WPF tab loads by default).
3. **WPF** — build & run `Globeweigh.Admin` in Visual Studio, click **Gate/Barrier (Web)**.

The WebView2 URL is configurable in `Globeweigh.Admin\App.config` →
`appSettings/GateBarrierWebUrl` (defaults to `http://localhost:1420`).

## Build & deploy (served by the API)

```
npm run build                         # -> dist/
# copy dist/* into ..\Globeweigh.Api\wwwroot\gate-web\
```
The API serves it at `http://<host>:5099/gate-web/`. Point `GateBarrierWebUrl` there for a
deployment that doesn't need the Vite dev server.

## Endpoints used

- `GET  /api/gate-barrier/config` — cameras + relay config (from the `Setting` table)
- `GET  /api/gate-barrier/state`  — `{ connected, gateOpen, barrierOpen }`
- `POST /api/gate-barrier/gate`    `{ open: bool }`
- `POST /api/gate-barrier/barrier` `{ open: bool }`
- `http://<host>:8765/weighbridge/weight` — live weight (GlobeweighService, direct)

## Status

Phase 1: stack + relay controls + live weight + camera selection. **Live video is a
placeholder** — RTSP can't play natively in a browser; an RTSP→browser streaming gateway
(WebRTC/HLS) is a later phase. The original LibVLC `Gate/Barrier` tab is untouched and runs
alongside this one.

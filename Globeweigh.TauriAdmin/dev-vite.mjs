// tauri.conf.json's beforeDevCommand (via `npm run dev:vite`).
// `npm run dev:full` starts Vite itself, in parallel with the API and the Rust side, and
// runs dev.ps1 with -ExternalVite, which sets GW_EXTERNAL_VITE. In that case, stay alive
// without doing anything (tauri treats the before-dev command exiting as the dev server
// going away) and let tauri's own wait on devUrl pick up the Vite that is already starting.
// Otherwise run Vite as before.
if (process.env.GW_EXTERNAL_VITE) {
  setInterval(() => {}, 1 << 30);
} else {
  await import("./node_modules/vite/bin/vite.js");
}

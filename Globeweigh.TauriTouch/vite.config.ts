import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// @ts-expect-error process is a nodejs global
const host = process.env.TAURI_DEV_HOST;

// Stamped into the bundle (__BUILD_ID__) and emitted as version.json alongside it.
// A running app polls version.json; a mismatch means a newer UI has been deployed
// to the server and the refresh pill is shown.
const now = new Date();
const buildId = now.toISOString();
// Human-readable, monotonic, no state file: local date+time of the build,
// e.g. "2026.06.14.1530". Shown in the sidebar and stored in version.json so a
// running build is identifiable down the phone / from the server.
const pad = (n: number) => String(n).padStart(2, "0");
const buildVersion =
  `${now.getFullYear()}.${pad(now.getMonth() + 1)}.${pad(now.getDate())}` +
  `.${pad(now.getHours())}${pad(now.getMinutes())}`;

// https://vite.dev/config/
export default defineConfig(async () => ({
  plugins: [
    react(),
    {
      name: "emit-version-json",
      apply: "build" as const,
      generateBundle() {
        this.emitFile({
          type: "asset" as const,
          fileName: "version.json",
          source: JSON.stringify({ buildId, buildVersion }),
        });
      },
    },
  ],

  define: {
    __BUILD_ID__: JSON.stringify(buildId),
    __BUILD_VERSION__: JSON.stringify(buildVersion),
  },

  // Relative asset paths so the built UI can be served from any sub-path
  // (it lives at /SpiceOLifeApi/admin/ on the server). Dev treats "./" as "/".
  base: "./",

  // Vite options tailored for Tauri development and only applied in `tauri dev` or `tauri build`
  //
  // 1. prevent Vite from obscuring rust errors
  clearScreen: false,
  // 2. tauri expects a fixed port, fail if that port is not available
  server: {
    // 1430 (not 1420) so TauriTouch can run alongside the TauriAdmin dev server.
    port: 1430,
    strictPort: true,
    host: host || false,
    hmr: host
      ? {
          protocol: "ws",
          host,
          port: 1431,
        }
      : undefined,
    watch: {
      // 3. tell Vite to ignore watching `src-tauri`
      ignored: ["**/src-tauri/**"],
    },
  },
}));

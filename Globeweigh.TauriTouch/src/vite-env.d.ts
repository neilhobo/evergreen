/// <reference types="vite/client" />

// Injected by vite.config.ts at build time; compared against version.json on the
// server to detect that a newer UI has been published (UiRefreshPill).
declare const __BUILD_ID__: string;

// Human-readable build version (e.g. "2026.06.14.1530"), shown in the sidebar.
declare const __BUILD_VERSION__: string;

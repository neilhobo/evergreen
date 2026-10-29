/// <reference types="vite/client" />

// Build-time API target selector, set by the `npm run dev` script (see config.ts).
interface ImportMetaEnv {
  readonly VITE_API_TARGET?: "production" | "development";
  // DEV ONLY auto sign-in (.env.development.local, gitignored via *.local). See LoginPage.
  readonly VITE_DEV_LOGIN_EMAIL?: string;
  readonly VITE_DEV_LOGIN_PASSWORD?: string;
}
interface ImportMeta {
  readonly env: ImportMetaEnv;
}

// Injected by vite.config.ts at build time; compared against version.json on the
// server to detect that a newer UI has been published (UiRefreshPill).
declare const __BUILD_ID__: string;

// Human-readable build version (e.g. "2026.06.14.1530"), shown in the sidebar.
declare const __BUILD_VERSION__: string;

// noVNC ships no TypeScript types; we only use a small slice of the RFB API.
// Its package "exports" maps the bare specifier to core/rfb.js (no subpaths).
declare module "@novnc/novnc" {
  interface RFBCredentials {
    username?: string;
    password?: string;
    target?: string;
  }
  export default class RFB extends EventTarget {
    constructor(
      target: HTMLElement,
      url: string,
      options?: {
        credentials?: RFBCredentials;
        shared?: boolean;
        repeaterID?: string;
        wsProtocols?: string[];
      },
    );
    viewOnly: boolean;
    scaleViewport: boolean;
    resizeSession: boolean;
    focusOnClick: boolean;
    background: string;
    clipViewport: boolean;
    sendCredentials(credentials: RFBCredentials): void;
    sendCtrlAltDel(): void;
    focus(): void;
    disconnect(): void;
  }
}

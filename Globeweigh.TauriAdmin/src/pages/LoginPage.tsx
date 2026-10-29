import { FormEvent, useEffect, useRef, useState } from "react";
import { adminLogin } from "../api/auth";
import { ApiError, probeApi } from "../api/client";
import { getApiBaseUrl, getDbTarget, setDbTarget, DB_TARGET_URLS, DbTarget } from "../config";
import { clearSavedDetails, loadSavedDetails, saveDetails } from "../auth/savedDetails";
import { AdminUserDto } from "../types";

interface Props {
  onLoggedIn: (user: AdminUserDto) => void;
  notice?: string | null;
}

// Module-level so the dev auto sign-in below runs once per app load, not once per mount
// of this page (a failed attempt lands back here and would otherwise try again forever).
let devAutoSignInTried = false;

// How long a sign-in keeps retrying while the API isn't answering, and how often.
const WAIT_FOR_SERVER_MS = 90_000;
const WAIT_RETRY_MS = 1500;

export default function LoginPage({ onLoggedIn, notice }: Props) {
  // Read synchronously so the first render already has the saved details — focusing a
  // pre-filled box makes the browser pop its autofill suggestion over it.
  const [saved] = useState(loadSavedDetails);
  const [email, setEmail] = useState(saved?.email ?? "");
  const [password, setPassword] = useState(saved?.password ?? "");
  const [saveDetailsChecked, setSaveDetailsChecked] = useState(!!saved);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // null = probe in flight
  const [apiReachable, setApiReachable] = useState<boolean | null>(null);
  // A sign-in is being held until the API answers.
  const [waiting, setWaiting] = useState(false);
  const mountedRef = useRef(true);

  const apiUrl = getApiBaseUrl();

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  // Preflight the API target so a wrong/dead server is visible before the first sign-in
  // attempt, not after a mysterious failure. While it's down, keep re-probing so the status
  // line flips to "online" by itself once the API has started.
  useEffect(() => {
    if (apiReachable === true) return;
    let cancelled = false;
    const timer = setTimeout(
      () => {
        probeApi(apiUrl).then((ok) => {
          if (!cancelled) setApiReachable(ok);
        });
      },
      apiReachable === null ? 0 : WAIT_RETRY_MS,
    );
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [apiUrl, apiReachable]);

  // Dev-only rescue: when the target is unreachable, offer the other one right here —
  // the sidebar DbSwitcher only exists after login, which is no help when login is broken.
  function switchTarget(target: DbTarget) {
    setDbTarget(target);
    window.location.reload();
  }

  // remember: true = store the details, false = clear any stored ones, null = leave the
  // "Save details" store alone (the dev auto sign-in must not overwrite a real choice).
  async function signIn(withEmail: string, withPassword: string, remember: boolean | null) {
    setError(null);
    setBusy(true);
    // A failure that isn't a wrong password usually means the API is still starting (dev:full
    // brings the UI up before dotnet watch has finished) — so rather than an error, show
    // "Waiting for the server…" and keep retrying; only give up and show it after a while.
    const waitUntil = Date.now() + WAIT_FOR_SERVER_MS;
    try {
      for (;;) {
        try {
          const user = await adminLogin(withEmail, withPassword);
          if (user.disabled) {
            setError("Account is disabled");
            return;
          }
          if (remember === true) saveDetails({ email: withEmail, password: withPassword });
          else if (remember === false) clearSavedDetails();
          onLoggedIn(user);
          return;
        } catch (err) {
          if (err instanceof ApiError && err.status === 401) {
            setError("Invalid details entered");
            return;
          }
          if (!mountedRef.current) return;
          if (Date.now() >= waitUntil) {
            const detail = (err as Error)?.message || String(err ?? "no response");
            setError(`Could not reach the server at ${apiUrl} — ${detail}`);
            probeApi(apiUrl).then(setApiReachable);
            return;
          }
          setWaiting(true);
          await new Promise((r) => setTimeout(r, WAIT_RETRY_MS));
          if (!mountedRef.current) return;
          setApiReachable(await probeApi(apiUrl));
        }
      }
    } finally {
      if (mountedRef.current) {
        setWaiting(false);
        setBusy(false);
      }
    }
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    void signIn(email, password, saveDetailsChecked);
  }

  // DEV ONLY: sign straight in with the credentials from .env.development.local, so the dev
  // loop doesn't stop at this form on every reload. import.meta.env.DEV is a compile-time
  // false in a release build, so Vite strips the whole branch — the credentials are never
  // read into, or bundled with, anything that ships.
  useEffect(() => {
    if (!import.meta.env.DEV || devAutoSignInTried) return;
    const devEmail = import.meta.env.VITE_DEV_LOGIN_EMAIL;
    const devPassword = import.meta.env.VITE_DEV_LOGIN_PASSWORD;
    if (!devEmail || !devPassword) return;
    devAutoSignInTried = true; // one attempt only — a bad password must not loop
    setEmail(devEmail);
    setPassword(devPassword);
    void signIn(devEmail, devPassword, null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="login-page">
      <div className="login-glow1" />
      <div className="login-glow2" />
      <div className="login-grid-overlay" />

      <div className="login-content">
        <div className="login-brand">
          <img className="login-brand-icon" src="images/Globe-96.png" alt="" />
          <span className="login-wordmark">
            <span className="word-globe">Globe</span>
            <span className="word-weigh">weigh</span>
          </span>
        </div>

        <form className="login-card" onSubmit={onSubmit}>
          <h1 className="login-title">Sign in</h1>
          <p className="login-subtitle">Warehouse management — Evergreen</p>

          {/* Down but no error yet = still starting up: grey "waiting…", not red. */}
          <div className={`login-api-status ${apiReachable === false && error ? "down" : ""}`}>
            <span
              className={`db-switch-dot ${
                apiReachable ? getDbTarget() : apiReachable === false && error ? "down" : "probing"
              }`}
            />
            <span className="login-api-url">{apiUrl}</span>
            <span className="login-api-state">
              {apiReachable === null
                ? "checking…"
                : apiReachable
                  ? "online"
                  : error
                    ? "UNREACHABLE"
                    : "waiting…"}
            </span>
          </div>

          {apiReachable === false && import.meta.env.DEV && (
            <div className="login-api-switch">
              {(["local", "prod"] as DbTarget[])
                .filter((t) => t !== getDbTarget())
                .map((t) => (
                  <button key={t} type="button" className="login-api-switch-btn" onClick={() => switchTarget(t)}>
                    Switch to {t === "local" ? "Local" : "PROD"} ({DB_TARGET_URLS[t]})
                  </button>
                ))}
            </div>
          )}

          {waiting && <div className="login-wait-banner">Waiting for the server…</div>}
          {!error && !waiting && notice && <div className="login-error-banner">{notice}</div>}
          {error && <div className="login-error-banner">{error}</div>}

          <label className="form-label" htmlFor="email">
            Email address
          </label>
          <input
            id="email"
            className="form-input"
            type="text"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoFocus={!saved}
            autoComplete="username"
          />

          <label className="form-label" htmlFor="password">
            Password
          </label>
          <input
            id="password"
            className="form-input"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
          />

          <label className="login-check">
            <input
              type="checkbox"
              checked={saveDetailsChecked}
              onChange={(e) => setSaveDetailsChecked(e.target.checked)}
            />
            Save details
          </label>

          <button className="btn-primary login-submit" type="submit" disabled={busy}>
            {waiting ? "Waiting…" : busy ? "Signing in…" : "Sign in"}
          </button>
        </form>
      </div>
    </div>
  );
}

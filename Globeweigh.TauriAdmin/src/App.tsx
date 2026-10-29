import { useEffect, useState } from "react";
import LoginPage from "./pages/LoginPage";
import MainWindow from "./pages/MainWindow";
import TitleBar from "./components/TitleBar";
import ServiceStatusOverlay from "./components/ServiceStatusOverlay";
import { AdminUserDto } from "./types";
import { clearSession, restoreSession } from "./api/auth";
import { setUnauthorizedHandler } from "./api/client";
import { takeDbSwitchNotice } from "./config";
import "./App.css";

export default function App() {
  // A page refresh (UI update pill) restores the session instead of re-prompting.
  const [user, setUser] = useState<AdminUserDto | null>(() => restoreSession());
  // Why the login screen is showing, when it is not simply "you are not signed in".
  const [notice, setNotice] = useState<string | null>(null);

  // A DB switch signs you out (DbSwitcher) and reloads, so the reason cannot be held in state —
  // it is left in sessionStorage and picked up here. Read in an effect rather than a useState
  // initialiser: StrictMode runs those twice in dev, and the second read would find it gone.
  useEffect(() => {
    const n = takeDbSwitchNotice();
    if (n) setNotice(n);
  }, []);

  // Expired JWT on any API call → straight back to the login screen.
  useEffect(() => {
    setUnauthorizedHandler(() => {
      clearSession();
      setUser(null);
      setNotice("Your session has expired. Please sign in again.");
    });
    return () => setUnauthorizedHandler(null);
  }, []);

  function handleSignOut() {
    clearSession();
    setUser(null);
    setNotice(null);
  }

  return (
    <div className="app-shell">
      <TitleBar />
      <div className="app-body">
        {user ? (
          <MainWindow
            // Keyed by who is signed in (and on whose behalf), so a Switch user starts the
            // window afresh — its nav, open page and page state belong to the other session.
            key={`${user.id}:${user.impersonatedByUserId ?? ""}`}
            user={user}
            onSignOut={handleSignOut}
            onUserUpdated={setUser}
            onSwitchUser={setUser}
          />
        ) : (
          <LoginPage
            notice={notice}
            onLoggedIn={(u) => {
              setNotice(null);
              setUser(u);
            }}
          />
        )}
      </div>
      <ServiceStatusOverlay signedIn={!!user} />
    </div>
  );
}

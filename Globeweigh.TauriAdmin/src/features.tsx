import { ReactNode, createContext, useCallback, useContext, useEffect, useState } from "react";
import { FeatureFlags, getFeatures, setFeature } from "./api/features";

// Which staged features are switched on, for the whole app. Read once after sign-in and kept in
// context, so a screen asking "is this on?" is answering from memory rather than from a call it
// would have to make every time it renders.
//
// OFF is the answer to everything until the flags arrive, and OFF is the answer if they never do.
// A staged feature is one nobody has asked for yet; showing it because the server was unreachable
// would be exactly the wrong way round.

interface Features {
  flags: FeatureFlags;
  isOn: (name: string) => boolean;
  /** Switch one and keep every screen in step, without a reload. Super-admin only server-side. */
  set: (name: string, enabled: boolean) => Promise<void>;
  /** False until the first read comes back (or fails) — the Staging page waits for it. */
  loaded: boolean;
}

const FeaturesContext = createContext<Features | null>(null);

export function FeaturesProvider({ children }: { children: ReactNode }) {
  const [flags, setFlags] = useState<FeatureFlags>({});
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    let cancelled = false;
    getFeatures()
      .then((f) => {
        if (!cancelled) setFlags(f);
      })
      .catch(() => {
        // Leave everything off. An API that cannot be asked is not a reason to go live.
      })
      .finally(() => {
        if (!cancelled) setLoaded(true);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const isOn = useCallback((name: string) => flags[name] === true, [flags]);
  const set = useCallback(async (name: string, enabled: boolean) => {
    setFlags(await setFeature(name, enabled));
  }, []);

  return (
    <FeaturesContext.Provider value={{ flags, isOn, set, loaded }}>
      {children}
    </FeaturesContext.Provider>
  );
}

export function useFeatures(): Features {
  const f = useContext(FeaturesContext);
  if (!f) throw new Error("useFeatures outside FeaturesProvider");
  return f;
}

/** Whether one staged feature is switched on. */
export function useFeature(name: string): boolean {
  return useFeatures().isOn(name);
}

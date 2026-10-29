import { keypad } from "./Keypad";

// Pass code for the weigh-in Open Barrier button — the barrier sits in front of drivers, so
// it is gated exactly as the WPF WeighbridgeInViewModel gated it. Its own code (2404), NOT
// the Super Admin one; override per-machine via localStorage("barrierPasscode").
const DEFAULT_PASSCODE = "2404";

// Resolves true when the code was right. A wrong code resolves false silently, matching
// requireSuperAdminPin. Unlike that one this is NOT skipped in dev builds: the API proxies the
// relay to the live GlobeweighService, so a dev machine's button opens the real barrier.
export async function requireBarrierPin(): Promise<boolean> {
  const expected = localStorage.getItem("barrierPasscode") || DEFAULT_PASSCODE;
  const entered = await keypad({ title: "Enter Pass Code", password: true });
  return entered !== null && entered === expected;
}

import { keypad } from "./Keypad";

// Shared Super Admin passcode gate (settings gear, weighscale setup). Defaults to the
// WPF value (1225); override per-machine via localStorage("superAdminPasscode").
const DEFAULT_PASSCODE = "1225";

// Resolves true when access is granted. A wrong passcode resolves false silently
// (discrete, like the WPF behaviour). Dev builds skip the prompt entirely.
export async function requireSuperAdminPin(): Promise<boolean> {
  if (import.meta.env.DEV) return true;
  const expected = localStorage.getItem("superAdminPasscode") || DEFAULT_PASSCODE;
  const entered = await keypad({ title: "Enter Pass Code", password: true });
  return entered !== null && entered === expected;
}

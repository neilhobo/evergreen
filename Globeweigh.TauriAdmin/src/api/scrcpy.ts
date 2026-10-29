// Wireless-ADB + scrcpy launcher for the Devices detail panel. Runs adb / scrcpy on the
// admin's PC via the Tauri shell plugin (the binaries are scoped in capabilities). These
// only work inside the Tauri desktop shell — in a plain browser there is no shell access,
// so the callers guard on `canRemote` and show a hint instead.
import { Command } from "@tauri-apps/plugin-shell";

export const canRemote =
  typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;

export interface AdbResult {
  ok: boolean;
  code: number | null;
  output: string;
}

const NOT_DESKTOP = "Remote control runs adb/scrcpy locally — open the desktop app to use it.";

// The adb device serial for a wireless connection is just "host:port".
export const serialOf = (host: string, port: number) => `${host}:${port}`;

async function runAdb(args: string[]): Promise<AdbResult> {
  if (!canRemote) throw new Error(NOT_DESKTOP);
  const out = await Command.create("adb", args).execute();
  const output = [out.stdout, out.stderr].filter(Boolean).join("\n").trim();
  return { ok: out.code === 0, code: out.code, output: output || `exit ${out.code}` };
}

// One-time pairing (Android 11+ wireless debugging "pair with code"). Newer adb takes the
// 6-digit code as a third arg, so no interactive prompt is needed.
export const adbPair = (host: string, pairPort: number, code: string) =>
  runAdb(["pair", serialOf(host, pairPort), code]);

export const adbConnect = (host: string, connectPort: number) =>
  runAdb(["connect", serialOf(host, connectPort)]);

// Switch a device to fixed-port TCP mode so future connects use 5555 (until reboot).
// This is the bootstrap for Android < 11 (no Wireless debugging): plug the device into
// this PC via USB once, run this, unplug, then adbConnect(ip, 5555). With exactly one
// device attached no serial is needed; pass one to disambiguate.
export const adbTcpip = (serial?: string) =>
  runAdb(serial ? ["-s", serial, "tcpip", "5555"] : ["tcpip", "5555"]);

// List attached/known devices (USB + wireless) so the admin can confirm a USB device is
// connected and authorised before enabling TCP/IP mode.
export const adbDevices = () => runAdb(["devices", "-l"]);

// Mirror + control. Latency-capped for connections over Tailscale; tweak as needed.
export async function scrcpyView(serial: string, deviceName?: string): Promise<void> {
  if (!canRemote) throw new Error(NOT_DESKTOP);
  const args = [
    "-s", serial,
    "--max-size", "1024",
    "--video-bit-rate", "2M",
    "--max-fps", "30",
  ];
  if (deviceName) args.push("--window-title", deviceName);
  await Command.create("scrcpy", args).spawn();
}

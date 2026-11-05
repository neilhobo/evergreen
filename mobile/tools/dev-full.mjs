// `npm run dev:full` — the local API (tools/dev-api.mjs) + the app on the open Android emulator,
// side by side in one terminal. Saving any .dart file hot reloads the app; r / R + Enter
// reload / restart it by hand.
//
// Why a script and not a bare `concurrently` line: on Ctrl+C, `flutter run` can leave its
// `adb shell -x logcat` stream behind with no parent. `adb shell` forwards the terminal's
// keystrokes to the device, so after the prompt comes back the terminal seems hung and typing
// crawls. Once everything has stopped, this sweeps up any logcat stream like that.
import concurrently from "concurrently";
import { execFileSync } from "node:child_process";

// Ctrl+C reaches every process on the console; this one stays to clean up after them.
process.on("SIGINT", () => {});

const { result } = concurrently(
  [
    { command: "node ./tools/dev-api.mjs", name: "api", prefixColor: "blue" },
    {
      // Through flutter_dev.dart (via dev-app.ps1), not a bare `flutter run`: it hot reloads by
      // itself whenever a .dart file under lib/ is saved, and takes /reload at :8991. The API
      // is reached as the emulator's own localhost:5099 (adb reverse).
      command: "powershell -NoProfile -ExecutionPolicy Bypass -File ./tools/dev-app.ps1",
      name: "app",
      prefixColor: "green",
    },
  ],
  { killOthersOn: ["success", "failure"], handleInput: true, defaultInputTarget: "app" },
);

let code = 0;
try {
  await result;
} catch {
  code = 1; // one side failed or was killed; the other was stopped with it
}

try {
  // Only logcat streams whose parent (flutter's dart process) no longer exists.
  const ps = `
$all = Get-CimInstance Win32_Process
$all | Where-Object { $_.Name -eq 'adb.exe' -and $_.CommandLine -match 'shell .*logcat' -and -not ($all | Where-Object ProcessId -eq $_.ParentProcessId) } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue; "[dev-full] Stopped a leftover adb logcat (PID $($_.ProcessId))." }`;
  const out = execFileSync("powershell", ["-NoProfile", "-Command", ps], { encoding: "utf8" }).trim();
  if (out) console.log(out);
} catch {
  /* nothing to clean, or PowerShell unavailable — not worth failing the exit over */
}
process.exit(code);

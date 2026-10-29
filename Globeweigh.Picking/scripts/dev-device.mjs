// Runs the Flutter app with hot reload AND auto-triggers a hot reload whenever a
// lib/**.dart file is saved (no keypress needed) — so a UI tweak applies in ~1s
// without rebuilding/reinstalling/restarting.
//
// Two targets:
//   • Physical ET40 (default) — connects over wireless adb. The ET40's wireless-
//     debugging port changes between sessions, so we connect to the known endpoint,
//     then resolve whatever serial actually came up. Override with ET40=192.168.1.201:PORT.
//     If it isn't reachable at all we fall back to a running emulator (or the only
//     attached USB device) so the session still starts.
//   • Local emulator — set EMULATOR=1 to boot/attach the "ET401" AVD (1920×1200 / 8" @ 280dpi),
//     or EMULATOR=<AvdName> for another. This is what `npm run dev:full` uses, so the
//     emulator boots on its own; `npm run dev:full:et40` is the physical-tablet variant.
//     Create the AVD with `npm run avd:create`. Faithful enough for UI/flow testing without
//     the physical Zebra; simulate scans with the same broadcast you use on-device:
//       adb shell am broadcast -a com.globeweigh.picking.SCANNER \
//         --es com.symbol.datawedge.data_string "ProdCd:<code>Weight:<w>kgUniqueCode:<id>"
import { spawn, spawnSync } from "node:child_process";
import { existsSync, watch } from "node:fs";
import path from "node:path";
import os from "node:os";

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function sdkRoot() {
  return (
    process.env.ANDROID_HOME ||
    process.env.ANDROID_SDK_ROOT ||
    path.join(os.homedir(), "AppData", "Local", "Android", "Sdk")
  );
}

function findAdb() {
  const candidates = [
    process.env.ADB,
    path.join(sdkRoot(), "platform-tools", "adb.exe"),
    "adb",
  ].filter(Boolean);
  for (const c of candidates) {
    if (c === "adb" || existsSync(c)) return c;
  }
  return "adb";
}

const adb = findAdb();
const adbRun = (args) => spawnSync(adb, args, { encoding: "utf8" });
console.log(`[device] adb: ${adb}`);

// ── target resolution ───────────────────────────────────────────────────────
const emuEnv = process.env.EMULATOR;
const serial = emuEnv
  ? await resolveEmulator(emuEnv === "1" || emuEnv === "true" ? "ET401" : emuEnv)
  : resolveEt40();

function resolveEt40() {
  const ET40 = process.env.ET40 || "192.168.1.201:33223";
  const ip = ET40.split(":")[0];
  console.log(`[device] connecting to ET40 at ${ET40} …`);
  adbRun(["connect", ET40]);

  // The wireless port can change between sessions — resolve the live serial by IP.
  const online = (adbRun(["devices"]).stdout || "")
    .split(/\r?\n/)
    .slice(1)
    .filter((l) => l.trim().endsWith("device"))
    .map((l) => l.split(/\s+/)[0]);

  const et40 = online.find((s) => s === ip || s.startsWith(`${ip}:`));
  if (et40) return et40;

  // ET40 unreachable — asleep, on a new DHCP address, or wireless debugging switched
  // off (its port only exists while that screen's toggle is on). Rather than dying,
  // fall back to whatever is attached so the dev session still starts: a running
  // emulator first, else the only USB device.
  const fallback =
    online.find((s) => s.startsWith("emulator-")) ??
    (online.length === 1 ? online[0] : null);

  console.warn(`[device] ${ip} not listed as connected.`);
  console.warn(`[device] For the real ET40: Settings → Developer options → Wireless`);
  console.warn(`[device] debugging, then run with  ET40=<ip>:<port> npm run dev:app`);
  if (fallback) {
    console.warn(`[device] falling back to ${fallback}`);
    return fallback;
  }
  console.warn(`[device] nothing else attached — trying ${ET40} anyway.`);
  return ET40;
}

function runningEmulatorSerial() {
  const out = adbRun(["devices"]).stdout || "";
  const l = out
    .split(/\r?\n/)
    .find((x) => x.startsWith("emulator-") && x.trim().endsWith("device"));
  return l ? l.split(/\s+/)[0] : null;
}

function emulatorBin(sdk) {
  const exe = path.join(sdk, "emulator", "emulator.exe");
  return existsSync(exe) ? exe : "emulator";
}

function listAvds(sdk) {
  const out = spawnSync(emulatorBin(sdk), ["-list-avds"], { encoding: "utf8" }).stdout || "";
  return out.split(/\r?\n/).map((s) => s.trim()).filter(Boolean);
}

async function resolveEmulator(avd) {
  let ser = runningEmulatorSerial();
  if (ser) {
    console.log(`[device] attaching to running emulator ${ser}`);
  } else {
    const sdk = sdkRoot();
    const emu = emulatorBin(sdk);

    // Check the AVD exists BEFORE booting. Without this, a machine that has never had
    // the AVD created (a fresh SDK install, or a rebuilt PC) sits for two minutes
    // waiting for an emulator that was never going to appear, then prints a guess.
    const avds = listAvds(sdk);
    if (!avds.includes(avd)) {
      console.error(`[device] no "${avd}" AVD on this machine.`);
      console.error(`[device] create it:  npm run avd:create`);
      console.error(`[device] AVDs found: ${avds.length ? avds.join(", ") : "(none)"}`);
      process.exit(1);
    }

    console.log(`[device] booting AVD "${avd}" …`);
    const proc = spawn(emu, ["-avd", avd, "-netdelay", "none", "-netspeed", "full"], {
      stdio: "ignore",
      detached: true,
    });
    proc.unref();

    const start = Date.now();
    while (!ser && Date.now() - start < 120_000) {
      await sleep(2000);
      ser = runningEmulatorSerial();
    }
    if (!ser) {
      // The AVD exists (checked above), so this is a boot failure rather than a missing
      // AVD — usually another emulator holding the lock, or HAXV/WHPX not available.
      console.error(`[device] AVD "${avd}" exists but never came online within 120s.`);
      console.error(`[device] try booting it by hand: "${emu}" -avd ${avd}`);
      process.exit(1);
    }
  }

  console.log(`[device] ${ser} online — waiting for boot to complete …`);
  const bootStart = Date.now();
  while (Date.now() - bootStart < 180_000) {
    const b = adbRun(["-s", ser, "shell", "getprop", "sys.boot_completed"]).stdout || "";
    if (b.trim() === "1") break;
    await sleep(2000);
  }
  // Clear any stale density override so the AVD uses its physical 280 dpi (config.ini),
  // matching the ET401's ~283 ppi panel: 1200px / (280/160) = sw686dp. A leftover
  // override (e.g. 213/240 dpi) silently shifts every dp breakpoint the layouts use.
  adbRun(["-s", ser, "shell", "wm", "density", "reset"]);
  console.log(`[device] ${ser} booted`);
  return ser;
}

// ── flutter run + auto hot-reload ────────────────────────────────────────────
console.log(`[device] flutter run on ${serial} — auto hot-reload on save (lib/**.dart)`);
console.log(`[device] manual keys still work: r=reload  R=restart  q=quit`);

// Optional build-time API target (GW_TARGET=prod|local) → forwarded to the app as a
// dart-define so it defaults to that DB without auto-probing (see AppConfig.resolve).
const runArgs = ["run", "-d", serial];
const gwTarget = process.env.GW_TARGET;
if (gwTarget) {
  runArgs.push(`--dart-define=GW_TARGET=${gwTarget}`);
  console.log(`[device] default API target: ${gwTarget.toUpperCase()} (GW_TARGET)`);
}

// stdin piped so we can inject a hot-reload ("r") on file save; stdout/stderr inherited
// so flutter's own logs stream through. shell:true so Windows resolves flutter.bat.
const child = spawn("flutter", runArgs, {
  stdio: ["pipe", "inherit", "inherit"],
  shell: true,
});

// Forward the user's keystrokes (r / R / q …) when attached to a real terminal.
// Raw mode disables the terminal's Ctrl+C → SIGINT, so the 0x03 byte must be
// handled here: first press asks flutter to quit cleanly ("q"), a second press
// (or a 5s timeout) force-kills the whole process tree.
if (process.stdin.isTTY) {
  try {
    process.stdin.setRawMode(true);
  } catch {
    /* not a raw-capable TTY — ignore */
  }
  process.stdin.resume();
  let quitting = false;
  process.stdin.on("data", (d) => {
    if (d.includes(0x03)) {
      if (quitting) forceKill();
      quitting = true;
      console.log("\n[device] Ctrl+C — quitting flutter (press again to force-kill)");
      child.stdin.write("q");
      setTimeout(forceKill, 5000).unref();
      return;
    }
    child.stdin.write(d);
  });
}

// shell:true means child.pid is the cmd wrapper — taskkill /T takes out the whole
// tree (cmd → flutter.bat → dart) where child.kill() would strand flutter.
function forceKill() {
  try {
    spawnSync("taskkill", ["/pid", String(child.pid), "/T", "/F"], { stdio: "ignore" });
  } catch {
    /* already gone */
  }
  process.exit(130);
}

// Auto hot-reload: watch lib/ for .dart edits, debounce, then tell flutter to reload.
let timer = null;
const libDir = path.join(process.cwd(), "lib");
try {
  watch(libDir, { recursive: true }, (_event, file) => {
    if (!file || !file.toString().endsWith(".dart")) return;
    clearTimeout(timer);
    timer = setTimeout(() => {
      console.log(`[device] ${file} saved → hot reload`);
      child.stdin.write("r");
    }, 300);
  });
} catch (e) {
  console.warn(`[device] file watch unavailable (${e}); press r to hot reload manually.`);
}

child.on("exit", (code) => process.exit(code ?? 0));

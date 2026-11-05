// `npm run dev:api` (part of dev:full) — the local Evergreen API on port 5099, which the
// emulator reaches as http://10.0.2.2:5099. Development environment = appsettings.json's
// connection string = the local (local)\SQLEXPRESS GbEvergreen DB.
//
// TauriAdmin's dev:full runs the same API on the same port. Two dotnet watchers on one
// project wedge each other, so if this repo's API is already listening we just use it and
// start nothing. Anything else on the port is named and we exit non-zero.
import { spawn, execFileSync } from "node:child_process";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

const port = 5099;
const here = path.dirname(fileURLToPath(import.meta.url));
const apiDir = path.resolve(here, "../../Globeweigh.Api");

function portFree(host) {
  return new Promise((resolve) => {
    const srv = net.createServer();
    srv.once("error", (e) => resolve(e.code !== "EADDRINUSE"));
    srv.once("listening", () => srv.close(() => resolve(true)));
    srv.listen(port, host);
  });
}

// PIDs listening on the port (IPv4 and IPv6 rows), via netstat.
function holders() {
  const out = execFileSync("netstat", ["-ano", "-p", "tcp"], { encoding: "utf8" }) +
    execFileSync("netstat", ["-ano", "-p", "tcpv6"], { encoding: "utf8" });
  const pids = new Set();
  for (const line of out.split(/\r?\n/)) {
    const m = line.trim().split(/\s+/);
    if (m.length >= 5 && m[3] === "LISTENING" && m[1].endsWith(`:${port}`)) pids.add(Number(m[4]));
  }
  return [...pids];
}

function describe(pid) {
  try {
    const ps = `$p = Get-CimInstance Win32_Process -Filter "ProcessId=${pid}"; "$($p.ExecutablePath)|$($p.CommandLine)"`;
    const [exe, cmd] = execFileSync("powershell", ["-NoProfile", "-Command", ps], { encoding: "utf8" })
      .trim()
      .split("|");
    return { exe: exe ?? "", cmd: cmd ?? "" };
  } catch {
    return { exe: "", cmd: "" };
  }
}

if (!((await portFree("127.0.0.1")) && (await portFree("::1")))) {
  const pids = holders();
  const ours = pids.filter((pid) => describe(pid).exe.toLowerCase().startsWith(apiDir.toLowerCase() + path.sep));
  if (ours.length > 0 && ours.length === pids.length) {
    console.log(`[dev-api] The Evergreen API is already running on port ${port} (PID ${ours.join(", ")}) — using it.`);
    // Stay up rather than exit: dev:full runs with --kill-others, so an early clean exit here
    // would take the app down with it. Ctrl+C (or concurrently's kill) ends this.
    setInterval(() => {}, 1 << 30);
    process.on("SIGINT", () => process.exit(0));
    await new Promise(() => {});
  }
  console.error(
    `\n[dev-api] Port ${port} is taken by something that is NOT this repo's Evergreen API:\n` +
      pids.map((pid) => `  PID ${pid}: ${describe(pid).cmd || "(unknown process)"}`).join("\n") +
      `\n\nStop it, then rerun dev:full.\n`,
  );
  process.exit(1);
}

const child = spawn(
  "dotnet",
  ["watch", "--non-interactive", "--project", path.join(apiDir, "Globeweigh.Api.csproj"), "run", "--launch-profile", "http"],
  { stdio: "inherit", shell: true },
);
child.on("exit", (code) => process.exit(code ?? 0));
// Ctrl+C reaches dotnet watch too (same console), and it shuts the API down itself. Wait for
// that rather than exiting first: leaving early orphans dotnet watch, which then keeps reading
// the terminal's keystrokes after the prompt is back, so the terminal seems hung.
process.on("SIGINT", () => {});

// `npm run dev:api` — starts the local Evergreen API under dotnet watch on the dev port from
// dev-api-port.json (also read by src/config.ts, so the two can never drift apart).
//
// Guards the port first. Other projects' dev APIs have squatted on ours twice (5199, then
// 5299): Kestrel then fails to bind, dotnet watch sits waiting, and the Touch app's calls go
// to the other API and come back as baffling 404s. So before starting:
//   - an orphaned Evergreen API (this repo's own Globeweigh.Api.exe, left behind by an
//     earlier dev:full) is stopped automatically;
//   - anything else holding the port is NAMED and we exit non-zero, which makes
//     `concurrently --kill-others` stop dev:full loudly instead of half-working.
import { spawn, execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const { port } = JSON.parse(readFileSync(path.join(here, "dev-api-port.json"), "utf8"));
const apiDir = path.resolve(here, "../Globeweigh.Api");

function portFree(host) {
  return new Promise((resolve) => {
    const srv = net.createServer();
    srv.once("error", (e) => resolve(e.code !== "EADDRINUSE"));
    srv.once("listening", () => srv.close(() => resolve(true)));
    srv.listen(port, host);
  });
}

// PIDs listening on the port (both IPv4 and IPv6 rows), via netstat.
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

const isFree = async () => (await portFree("127.0.0.1")) && (await portFree("::1"));

if (!(await isFree())) {
  const foreign = [];
  for (const pid of holders()) {
    const { exe, cmd } = describe(pid);
    if (exe.toLowerCase().startsWith(apiDir.toLowerCase() + path.sep)) {
      console.log(`[dev-api] Stopping orphaned Evergreen API (PID ${pid}) left on port ${port}.`);
      try { process.kill(pid); } catch { /* already gone */ }
    } else {
      foreign.push(`  PID ${pid}: ${cmd || exe || "(unknown process)"}`);
    }
  }
  if (foreign.length > 0) {
    console.error(
      `\n[dev-api] Port ${port} is taken by a process that is NOT this repo's Evergreen API:\n` +
        foreign.join("\n") +
        `\n\nStop it (or move that project off port ${port}), then rerun dev:full.\n`,
    );
    process.exit(1);
  }
  await new Promise((r) => setTimeout(r, 1000));
  if (!(await isFree())) {
    console.error(`\n[dev-api] Port ${port} is still in use — see netstat -ano | findstr :${port}\n`);
    process.exit(1);
  }
}

const child = spawn(
  "dotnet",
  [
    "watch", "--non-interactive",
    "--project", path.join(apiDir, "Globeweigh.Api.csproj"),
    "run", "--launch-profile", "http",
    "--", "--urls", `http://localhost:${port}`,
  ],
  { stdio: "inherit", shell: false },
);
child.on("exit", (code) => process.exit(code ?? 0));
for (const sig of ["SIGINT", "SIGTERM"]) process.on(sig, () => child.kill(sig));

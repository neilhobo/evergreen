# Globeweigh.TauriTouch

Tauri 2 + React 19 + Vite rewrite of the WPF `Globeweigh.Touch` shop-floor touch-screen app.
Same thin-shell + server-hosted-UI model as `Globeweigh.TauriAdmin`: the installed exe bundles
only `loader/index.html`, which navigates the webview to `<api>/touch/`. A UI release is just a
file copy to the server (`deploy-touch-ui.ps1`). All data goes through `Globeweigh.Api` — the app
never touches the database.

## What the screen does is per-device

Each physical screen has a `dbo.Device` row (keyed by the OS hostname, reported by the Tauri
`os` plugin). On boot the app fetches that row and routes exactly like the old
`MainWindowViewModel`:

- `IsWeighIn`  → **Weigh-In** screen (Delivery / Collection)
- `IsWeighOut` → **Weigh-Out** screen (pending list → confirm reg → weigh out)
- `Dispatch` / `Production` → Home (screens are a later phase)
- otherwise → **Home** menu

A discrete **gear** in the bottom-right corner opens a passcode prompt (default `1225`, override
via `localStorage("superAdminPasscode")`) → the **Super Admin** popup to set this screen's mode.

Login is the touch passcode flow: pick a factory user, enter the numeric passcode.

## Unattended startup (power-cut recovery)

`setup-touch-autostart.ps1` is the whole thing — one file, copy it to each touch PC and run it.

**On a touch PC: press `Win`+`X` then `A` (Windows PowerShell (Admin)) and paste one line.**
Nothing to copy to the PC first:

```powershell
irm http://evergreen:5099/touch-setup.txt | iex
```

`touch-setup.txt` in the API's `wwwroot` is this script — republish it after editing:

```powershell
Copy-Item .\setup-touch-autostart.ps1 '\\evergreen\Globeweigh\EvergreenApi\wwwroot\touch-setup.txt' -Force
```

Why that shape, and not a `.ps1` you double-click:

- A Group Policy execution policy (`MachinePolicy` / `UserPolicy` in `Get-ExecutionPolicy -List`)
  outranks `-ExecutionPolicy Bypass` and refuses to load script *files* at all. Double-click or
  "Run with PowerShell" dies with "cannot be loaded because running scripts is disabled", in red,
  **before a single line of the script runs** — and the window shuts with it. Piping *text* to
  `iex` isn't gated that way.
- `.txt`, not `.ps1`: the API's static-file middleware has no `ServeUnknownFileTypes`, so a
  `.ps1` would 404. `.txt` is served as `text/plain`.
- Your own admin window means no UAC hop and no second window that can vanish. Run it without
  admin and it says so, and changes nothing.

Everything is also written to `C:\Globeweigh\Touch\setup-<PCNAME>.log`.

Run it **while logged on as the account the screen actually runs as**. The NSIS bundle is
`INSTALLMODE "currentUser"`, so the exe lives in that user's `%LOCALAPPDATA%\Globeweigh Touch`,
and autostart + auto-logon must both target them. It prompts for that account's Windows
password (to set auto-logon) and refuses to store one that doesn't actually authenticate.

```powershell
.\setup-touch-autostart.ps1                            # the full job, prompts for what it needs
.\setup-touch-autostart.ps1 -Status                    # report only, changes nothing
.\setup-touch-autostart.ps1 -AutoLogonPassword 'p@ss'  # don't prompt for the password
.\setup-touch-autostart.ps1 -Remove                    # undo the autostart task
```

Every run is safe to repeat, and ends with a verified five-line checklist of what the PC will
actually do after a power cut.

It registers scheduled task `Globeweigh-Touch-Autostart`, which runs a watchdog
(`C:\Globeweigh\Touch\touch-watchdog.ps1`) in the user's session: it starts the app at logon
and restarts it within ~30s if it ever dies. The watchdog resolves the exe itself at runtime
(HKCU uninstall key → `%LOCALAPPDATA%` → Program Files), so reinstalls and updater bumps keep
working, and it holds off while an installer is running so it can't race the Tauri updater.
It logs to `%LOCALAPPDATA%\Globeweigh Touch\autostart.log`.

It also disables sleep/screensaver/lock-on-idle, turns off hibernate and Fast Startup, sets
`bootstatuspolicy ignoreallfailures` + `recoveryenabled No` (so an unclean shutdown can't park
the PC on the recovery menu), and stops Windows Update rebooting under someone.

**The one step it cannot do:** the BIOS "After AC Power Loss" setting must be `Power On`, set
by hand in firmware setup on each PC. Without it the PC simply stays off when the mains
returns. (`-TryBiosAcRecovery` attempts it via Lenovo WMI / Dell cctk where those exist.)

Nothing is needed for the server being slow to return: `loader/index.html` retries
`<api>/touch/` every 5 seconds, forever.

## Dev / build / deploy

- `.\dev.ps1 dev` — run the Tauri shell (its own Vite on :1420). Do **not** also `npm run dev`.
- `.\deploy-touch-ui.ps1 -Local` — build + deploy to the source-tree API wwwroot (`…\Globeweigh.Api\wwwroot\touch`).
- `.\deploy-touch-ui.ps1` — build + deploy to the IIS-published API (`C:\Globeweigh\EvergreenApi\wwwroot\touch`).
- Served at `<api>/touch/`. Needs the matching `Globeweigh.Api` build (Device/Touch controllers + touch-login).

## API surface (in Globeweigh.Api)

- `GET /api/auth/touch-users`, `POST /api/auth/touch-login` (userId + passcode → JWT)
- `GET /api/device/by-name/{host}` (create-on-miss), `PUT /api/device/{id}/config`, `GET /api/device`
- `GET /api/touch/hauliers|products|products/{id}/suppliers|suppliers/{id}/bogs`
- `POST /api/touch/weigh-in` (delivery), `GET /api/touch/collection/{ref}` + `POST /api/touch/collection/{id}/weigh`
- `GET /api/touch/pending`, `POST /api/touch/pending/{id}/weigh-out`
- live weight + barrier reuse `GET /api/gate-barrier/weight` / `POST /api/gate-barrier/barrier`

One-time DB migration: `Globeweigh.Api/Data/Migrations/AddDeviceModeColumns.sql` (adds
`Dispatch` / `Production` bit columns to `dbo.Device`).

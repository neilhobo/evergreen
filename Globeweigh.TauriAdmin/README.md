# Globeweigh Evergreen — TauriAdmin

Web-tech rewrite of the WPF **Globeweigh.Admin** desktop app (Evergreen WMS), built as a
Tauri 2 + React 19 + Vite + TypeScript shell. Same design language as the SpiceO'Life
`Globeweigh.TauriAdmin`. All data comes from **Globeweigh.Api** — the app never touches the
database directly.

## Architecture — thin shell + server-hosted UI

The installed `.exe` is a **thin Tauri shell** that bundles only `loader/index.html`. On
launch the loader navigates the webview to the server-hosted UI at `<api>/admin/`. A UI
release is therefore just a **file copy to the server** (`deploy-admin-ui.ps1`) — clients
never reinstall, they just relaunch or refresh.

```
Globeweigh Evergreen.exe  (loader/index.html only)
   └─ on launch → http://evergreen:5099/admin/   (React build in API wwwroot/admin)
          └─ all calls → http://evergreen:5099/api/...  (Globeweigh.Api, JWT)
```

- `src/config.ts` — API base URL. Dev `http://localhost:5099`, prod `http://evergreen:5099`,
  or derived from the page URL when served at `<api>/admin/`. Override per-machine with
  `localStorage("apiBaseUrl")`.
- `src/api/client.ts` — fetch wrapper. Inside Tauri uses the HTTP plugin (no CORS); in a
  plain browser uses native `fetch`. Bearer JWT; a 401 bounces back to login.
- Session is kept in `sessionStorage` so a UI-refresh (see below) keeps you signed in.

## Auto-update — refresh, not reinstall

`vite.config.ts` stamps each build with `__BUILD_ID__` and emits `version.json` beside the
bundle. `src/components/UiRefreshPill.tsx` polls `version.json` every 30s; when the server
has a newer build it:

1. shows an "Update available" card at the bottom of the sidebar (click to refresh), and
2. once the mouse has been idle for 60s, shows a popup that **auto-refreshes after a 20s
   countdown** (committed once shown — mouse activity no longer dismisses it).

`src/components/UpdatePill.tsx` is the separate Tauri **shell** updater (rare; endpoint
`<api>/api/admin-app/latest.json`, signed artifacts). The day-to-day update path is the UI
refresh above.

## Develop

The API must be running on `:5099` (`dotnet run` in `../Globeweigh.Api`, or IIS).

```powershell
npm install
.\dev.ps1 dev        # tauri dev (starts its own Vite on :1420 + the desktop window)
# or, browser-only:  npm run dev   → http://localhost:1420
```

`dev.ps1` runs inside the VS2022 Build-Tools x64 env and sets the updater signing key.
Note: don't run `npm run dev` separately *and* `dev.ps1 dev` — the latter starts its own
Vite on :1420 and will fail if the port is taken.

## Deploy a UI release

```powershell
.\deploy-admin-ui.ps1            # build + copy dist\ to C:\Globeweigh\EvergreenApi\wwwroot\admin
.\deploy-admin-ui.ps1 -Local     # build + copy to the source-tree API wwwroot (dotnet run / IIS Express)
.\deploy-admin-ui.ps1 -Target X  # explicit wwwroot\admin path
.\deploy-admin-ui.ps1 -SkipBuild # redeploy existing dist\
```

The API serves it via `app.MapFallbackToFile("admin/{*path:nonfile}", "admin/index.html")`.

## Modules (MainWindow nav, mirrors the WPF app)

| Menu | Page | API |
|------|------|-----|
| Weighbridge | weighbridge/WeighbridgeRecordsPage | `GET /api/weighbridge/complete`, `/pending` |
| Orders / Dispatch | dispatch/DispatchPage | `GET /api/dispatch/orders`, `/orders/{id}/items` |
| Costing | costing/CostingPage | `GET /api/costing/products`, `/products/{id}/zones` |
| Production | production/ProductionPage | `GET /api/production/pallets` |
| Completed Orders | completed-orders/CompletedOrdersPage | `GET /api/dispatch/completed` |
| Customers/Suppliers | customers-suppliers/CustomersSuppliersPage | `GET /api/customers-suppliers`, `/{id}/addresses` |
| Products | products/ProductsPage | `GET /api/products` |
| Hauliers | hauliers/HauliersPage | `GET /api/hauliers` |
| Admin | reference-data/ReferenceDataPage | `GET /api/reference-data/types`, `?refTypeId=` |
| Gate/Barrier | gate-barrier/GateBarrierPage | `GET /api/gate-barrier/*` (relay/weight/cameras) |
| Entities *(super admin)* | entities/EntitiesPage | `GET /api/entities`, `/{id}/fields` |
| Devices *(super admin)* | devices/DevicesPage | — (placeholder, as in WPF) |

The legacy LibVLC **Gate/Barrier (native)** module is intentionally **not** ported — the
Gate/Barrier menu item maps to the web module (relay + live weight + go2rtc camera).

### Current scope

All modules above are present and navigable; the data grids are **read-only** so far
(list + master/detail), matching the WPF default views. Add/Edit/Delete dialogs and
printing/reports are the next layer. The API exposes the read endpoints as keyless EF Core
read-models (`Globeweigh.Api/Data/ReadModels/*`, mapped to the `vwXxx` views / tables);
each new module is additive (no DbContext edits — `ApplyConfigurationsFromAssembly`).

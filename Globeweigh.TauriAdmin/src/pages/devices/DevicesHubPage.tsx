import { useState } from "react";
import { AdminUserDto } from "../../types";
import PageTabs, { PageTab } from "../../widgets/PageTabs";
import DevicesPage from "./DevicesPage";
import SystemDevicesPage from "../system-devices/SystemDevicesPage";

type DevicesTab = "overview" | "manager";

// The overview is still being built out, so it stays dev-only; Device Manager goes wherever
// the Devices nav item does (the super-admin, or any local dev build).
const TABS: PageTab<DevicesTab>[] = [
  ...(import.meta.env.DEV ? [{ key: "overview" as const, label: "Devices" }] : []),
  { key: "manager", label: "Device Manager" },
];

// Globeweigh Admin > Devices: the client-facing device overview and the mobile-app Device
// Manager, one tab each. Each screen keeps its own card and draws the strip under its header.
export default function DevicesHubPage({ user }: { user: AdminUserDto }) {
  const [tab, setTab] = useState<DevicesTab>(TABS[0].key);
  const strip = <PageTabs tabs={TABS} active={tab} onChange={setTab} />;
  return tab === "overview" ? (
    <SystemDevicesPage user={user} tabs={strip} />
  ) : (
    <DevicesPage user={user} tabs={strip} />
  );
}

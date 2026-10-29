import { useState } from "react";
import { AdminUserDto } from "../../types";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import EmailSetupPanel from "./EmailSetupPanel";
import StagingPanel from "./StagingPanel";
import ScaleRelayStylesPanel from "./ScaleRelayStylesPanel";
import OilTanksAccessPanel from "./OilTanksAccessPanel";

// Globeweigh-internal setup, super-admin only (the "Globeweigh Admin" nav section).
// Same master/detail shape as the Admin page: a list of views on the left, the selected
// one's panel on the right. Add a view by adding an ITEMS row and a case below.
type View = "email" | "staging" | "styles" | "oil-tanks";
const ITEMS: { key: View; label: string }[] = [
  { key: "email", label: "Email Setup" },
  // Features that are built and deployed but not switched on yet — see src/features.tsx.
  { key: "staging", label: "Staging" },
  // How the Live Weight column on Deliveries/Incoming is drawn — see LiveWeightPanel.tsx.
  { key: "styles", label: "Scale/Relay Styles" },
  // Which users see the Oil Tanks page — see OilTanksAccessPanel.tsx.
  { key: "oil-tanks", label: "Oil Tanks" },
];

export default function SuperAdminPage(_props: { user: AdminUserDto }) {
  const [view, setView] = useState<View>(ITEMS[0].key);

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Super Admin" meta={ITEMS.find((i) => i.key === view)?.label} />
        <div className={cc.body}>
          <aside className={`${cc.listCol} ${cc.listColTinted}`} style={{ flexBasis: 170 }}>
            <div className="side-list-title rail">Super Admin</div>
            <ul className="side-list">
              {ITEMS.map((item) => (
                <li
                  key={item.key}
                  className={item.key === view ? "active" : undefined}
                  onClick={() => setView(item.key)}
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") setView(item.key);
                  }}
                >
                  {item.label}
                </li>
              ))}
            </ul>
          </aside>

          {view === "email" && <EmailSetupPanel />}
          {view === "staging" && <StagingPanel />}
          {view === "styles" && <ScaleRelayStylesPanel />}
          {view === "oil-tanks" && <OilTanksAccessPanel />}
        </div>
      </div>
    </div>
  );
}

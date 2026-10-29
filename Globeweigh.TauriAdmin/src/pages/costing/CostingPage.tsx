import { useState } from "react";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import PageTabs, { PageTab } from "../../widgets/PageTabs";
import CostingPricingPanel from "./CostingPricingPanel";
import CostingFullPanel from "./CostingFullPanel";
import CostingZonesPanel from "../reference-data/CostingZonesPanel";
import { AdminUserDto } from "../../types";

type CostingTab = "costing" | "full" | "setup";

// The master/detail "Costing" tab is hidden for now — the matrix view has taken its name.
// Add { key: "costing", label: "Costing (detail)" } here to bring it back.
const TABS: PageTab<CostingTab>[] = [
  { key: "full", label: "Costing" },
  { key: "setup", label: "Costing Setup" },
];

// Costing — "Costing" tab mirrors the WPF CostingPriceListView (product list →
// zone pricing → pricing history); "Costing Setup" is the costing-zones
// add/edit/delete list (same panel as Admin ▸ Data Setup ▸ Costing Zones).
export default function CostingPage(_props: { user: AdminUserDto }) {
  const [tab, setTab] = useState<CostingTab>("full");

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader
          title="Costing"
        />
        <PageTabs tabs={TABS} active={tab} onChange={setTab} />
        <div className={cc.body}>
          {tab === "costing" ? (
            <CostingPricingPanel />
          ) : tab === "full" ? (
            <CostingFullPanel />
          ) : (
            <div className="po-open-layout" style={{ flex: 1, minHeight: 0 }}>
              {/* A fixed 500px column for the zones grid (the panel is shared with Data
                  Setup, so the width is constrained here), parted from the empty rest of
                  the card by a hairline. */}
              <div
                style={{ width: 500, flex: "none", display: "flex", minHeight: 0, borderRight: "1px solid #e5e7eb" }}
              >
                <CostingZonesPanel />
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

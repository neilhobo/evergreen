import { useState } from "react";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import PageTabs, { PageTab } from "../../widgets/PageTabs";
import CurrentStockPanel from "./CurrentStockPanel";
import StockTakesPanel from "./StockTakesPanel";
import { AdminUserDto } from "../../types";

type StockTab = "current" | "takes";

const TABS: PageTab<StockTab>[] = [
  { key: "current", label: "Current Stock" },
  { key: "takes", label: "Stock Takes/Transfer" },
];

// Stock — a per-product summary of the CodicoPallet master list (the stock list is
// always a summary, never a list of pallets), plus count-based Stock Takes.
export default function StockPage(_props: { user: AdminUserDto }) {
  const [tab, setTab] = useState<StockTab>("current");

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader
          title="Stock"
        />
        <PageTabs tabs={TABS} active={tab} onChange={setTab} />
        <div className={cc.body}>{tab === "current" ? <CurrentStockPanel /> : <StockTakesPanel />}</div>
      </div>
    </div>
  );
}

import { useState } from "react";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import PageTabs, { PageTab } from "../../widgets/PageTabs";
import RequestsPanel from "./RequestsPanel";
import CalendarPanel from "./CalendarPanel";
import StaffPanel from "./StaffPanel";
import HolidaysPanel from "./HolidaysPanel";
import CategoriesPanel from "./CategoriesPanel";
import "./AnnualLeave.css";

// Annual Leave — the office side of the client's staff holiday entitlement (their "Annual Leave
// Request Log" spreadsheet, made live). Every employee has 28 days a year: set company days
// for their category plus floating days they book. Requests are approved or declined here, with
// the policy checks beside them; the calendar shows who is off; the company days and categories
// are the rules behind every balance. The sums are the API's (Services/LeaveCalculator.cs).
//
// Staged: the super-admin only until Annual Leave is switched on in Super Admin > Staging.
// The employee's own side (book and view their leave, on a phone) comes later.

type Tab = "requests" | "calendar" | "staff" | "holidays" | "categories";

const TABS: PageTab<Tab>[] = [
  { key: "requests", label: "Requests" },
  { key: "calendar", label: "Calendar" },
  { key: "staff", label: "Staff" },
  { key: "holidays", label: "Company Holidays" },
  { key: "categories", label: "Categories" },
];

export default function AnnualLeavePage() {
  const [tab, setTab] = useState<Tab>("requests");
  const [year, setYear] = useState(new Date().getFullYear());
  // What the open tab puts in the header ("4 waiting", "22 staff").
  const [meta, setMeta] = useState("");
  // Bumped whenever one tab changes something another shows (a request approved moves the
  // calendar and the balances), so every tab reloads when it is next shown.
  const [version, setVersion] = useState(0);
  const changed = () => setVersion((v) => v + 1);

  const yearPicker = (
    <span className="al-year">
      <button type="button" aria-label="Previous year" onClick={() => setYear((y) => y - 1)}>
        ‹
      </button>
      Leave year {year}
      <button type="button" aria-label="Next year" onClick={() => setYear((y) => y + 1)}>
        ›
      </button>
    </span>
  );

  const props = { year, version, onChanged: changed, onMeta: setMeta };

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Annual Leave" meta={meta} center={yearPicker} />
        <PageTabs
          tabs={TABS}
          active={tab}
          onChange={(t) => {
            if (t === tab) return;
            setMeta("");
            setTab(t);
          }}
        />
        <div className={cc.body}>
          {tab === "requests" && <RequestsPanel {...props} />}
          {tab === "calendar" && <CalendarPanel {...props} />}
          {tab === "staff" && <StaffPanel {...props} />}
          {tab === "holidays" && <HolidaysPanel {...props} />}
          {tab === "categories" && <CategoriesPanel {...props} />}
        </div>
      </div>
    </div>
  );
}

/** What every tab is given by the page. */
export interface PanelProps {
  year: number;
  /** Changes when another tab has changed the data — reload. */
  version: number;
  /** Tell the other tabs something changed. */
  onChanged: () => void;
  /** The header's meta text for this tab. */
  onMeta: (text: string) => void;
}

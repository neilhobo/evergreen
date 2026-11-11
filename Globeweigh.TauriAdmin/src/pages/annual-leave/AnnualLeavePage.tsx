import { useEffect, useState } from "react";
import { FEATURE } from "../../api/features";
import { useFeatures } from "../../features";
import { AdminUserDto, isSuperAdmin } from "../../types";
import cc from "../../styles/ContentCard.module.css";
import PageHeader from "../../widgets/PageHeader";
import PageTabs, { PageTab } from "../../widgets/PageTabs";
import RequestsPanel from "./RequestsPanel";
import CalendarPanel from "./CalendarPanel";
import StaffPanel from "./StaffPanel";
import OutstandingPanel from "./OutstandingPanel";
import HolidaysPanel from "./HolidaysPanel";
import CategoriesPanel from "./CategoriesPanel";
import "./AnnualLeave.css";

// Annual Leave — the office side of the client's staff holiday entitlement (their "Annual Leave
// Request Log" spreadsheet, made live). Every employee has 28 days a year: set company days
// for their category plus floating days they book. Requests are approved or declined here, with
// the policy checks beside them; the calendar shows who is off; the company days and categories
// are the rules behind every balance. The sums are the API's (Services/LeaveCalculator.cs).
//
// Staged: the super-admin only until Annual Leave is switched on in Super Admin > Staging, and
// then each tab only once its own switch there is on too (the super-admin sees every tab, the
// switched-off ones in lavender). The employee's own side (on a phone) comes later.

type Tab = "requests" | "calendar" | "staff" | "outstanding" | "holidays" | "categories";

const TABS: (PageTab<Tab> & { flag: string })[] = [
  { key: "requests", label: "Requests", flag: FEATURE.annualLeaveRequests },
  { key: "calendar", label: "Calendar", flag: FEATURE.annualLeaveCalendar },
  { key: "staff", label: "Staff", flag: FEATURE.annualLeaveStaff },
  { key: "outstanding", label: "Outstanding", flag: FEATURE.annualLeaveOutstanding },
  { key: "holidays", label: "Company Holidays", flag: FEATURE.annualLeaveHolidays },
  { key: "categories", label: "Categories", flag: FEATURE.annualLeaveCategories },
];

export default function AnnualLeavePage({ user }: { user: AdminUserDto }) {
  const { isOn } = useFeatures();
  const superAdmin = isSuperAdmin(user);
  const tabs: PageTab<Tab>[] = TABS.filter((t) => superAdmin || isOn(t.flag)).map((t) => ({
    key: t.key,
    label: t.label,
    restricted: !isOn(t.flag),
  }));
  const [tab, setTab] = useState<Tab>(tabs[0]?.key ?? "requests");
  const [year, setYear] = useState(new Date().getFullYear());
  // What the open tab puts in the header ("4 waiting", "22 staff").
  const [meta, setMeta] = useState("");
  // The open tab switched off (or the flags arriving after the first render): move to one
  // that shows.
  const firstKey = tabs[0]?.key;
  const tabShown = tabs.some((t) => t.key === tab);
  useEffect(() => {
    if (!tabShown && firstKey) {
      setMeta("");
      setTab(firstKey);
    }
  }, [tabShown, firstKey]);
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

  const props = { year, version, onChanged: changed, onMeta: setMeta, onYear: setYear };

  return (
    <div className={cc.page}>
      <div className={cc.card}>
        <PageHeader title="Annual Leave" meta={meta} center={yearPicker} />
        <PageTabs
          tabs={tabs}
          active={tab}
          onChange={(t) => {
            if (t === tab) return;
            setMeta("");
            setTab(t);
          }}
        />
        <div className={cc.body}>
          {tabs.length === 0 && (
            <p className="al-empty">No Annual Leave tabs are switched on yet.</p>
          )}
          {tabShown && (
            <>
              {tab === "requests" && <RequestsPanel {...props} />}
              {tab === "calendar" && <CalendarPanel {...props} />}
              {tab === "staff" && <StaffPanel {...props} />}
              {tab === "outstanding" && <OutstandingPanel {...props} />}
              {tab === "holidays" && <HolidaysPanel {...props} />}
              {tab === "categories" && <CategoriesPanel {...props} />}
            </>
          )}
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
  /** Move the whole page to another leave year (the calendar's month arrows run past December). */
  onYear: (year: number) => void;
}

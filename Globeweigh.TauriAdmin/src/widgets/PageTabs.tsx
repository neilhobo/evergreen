import s from "./PageTabs.module.css";

export interface PageTab<K extends string> {
  key: K;
  label: string;
}

interface Props<K extends string> {
  tabs: readonly PageTab<K>[];
  active: K;
  onChange: (key: K) => void;
}

// Page tabs: a compact navy strip of its own under the card's PageHeader (the specgauge
// Collection Planner's tab strip), the selected tab styled as the sidebar's selected item.
// Render it straight after <PageHeader />.
export default function PageTabs<K extends string>({ tabs, active, onChange }: Props<K>) {
  return (
    <div className={s.strip} role="tablist">
      {tabs.map((t) => (
        <button
          key={t.key}
          type="button"
          role="tab"
          aria-selected={t.key === active}
          className={t.key === active ? `${s.tab} ${s.active}` : s.tab}
          onClick={() => onChange(t.key)}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}

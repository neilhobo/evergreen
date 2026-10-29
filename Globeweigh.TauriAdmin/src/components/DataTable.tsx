import { Fragment, KeyboardEvent, MouseEvent, ReactNode, useEffect, useRef, useState } from "react";

export interface Column<T> {
  key: string;
  header: ReactNode; // pass a fragment with <br /> for a two-line header
  width?: number; // fixed px; omit for flexible
  // Best-fit: column shrinks to its content. When any column is best-fit, the flexible
  // (width-less) columns clamp with ellipsis so the table can never scroll horizontally.
  fit?: boolean;
  align?: "left" | "right" | "center";
  render?: (row: T) => ReactNode;
  // WPF-style auto-filter row: when any column has a filter, a free-typing input row is
  // rendered under the headers (the caller owns the value and does the row filtering).
  filter?: { value: string; onChange: (value: string) => void; placeholder?: string };
  // Hovering this column's header highlights the whole column (Costing Full zone columns).
  hoverHighlight?: boolean;
  // Globeweigh-internal column, only ever built when the signed-in user is the super
  // admin (types.ts isSuperAdmin). Shaded lavender — the same cue as the super-admin
  // nav section — so it's obvious the client never sees it.
  superAdmin?: boolean;
  // Extra class on every cell of the column — header, body, filter and footer — e.g.
  // "col-tint-orange" to shade a column (see App.css).
  className?: string;
  // Totals row pinned under the grid: when any column defines this, a footer row is
  // rendered (given every row, so a column can total, count, or just label the row).
  footer?: (rows: T[]) => ReactNode;
  // Click-to-sort (grids with sortable set): what this column sorts on. Without it the
  // column falls back to row[key], so a plain data column needs nothing; give it for a
  // column whose render() builds its text out of something else.
  sortValue?: (row: T) => string | number | Date | null | undefined;
  // Never sortable, even on a sortable grid (action/button columns).
  noSort?: boolean;
}

interface Props<T> {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => number | string;
  selectedKey?: number | string | null;
  // The click event is passed through so callers can do shift/ctrl multi-select, with the
  // rows in the order they are on screen (after any header sort) for a shift-click range.
  onSelect?: (row: T, e?: MouseEvent, view?: T[]) => void;
  onDoubleClick?: (row: T) => void;
  rowClass?: (row: T) => string | undefined;
  emptyText?: string;
  maxHeight?: number | string;
  // Optional grouping: whenever groupKey changes between consecutive rows a full-width
  // header row is inserted showing groupLabel (rows must arrive pre-sorted by group).
  groupKey?: (row: T) => string;
  groupLabel?: (row: T) => ReactNode;
  // Opt in to click-to-sort headers: first click sorts ascending, the second descending,
  // the third returns the grid to the order the caller supplied. Off by default so no
  // existing grid changes behaviour. Don't combine with grouping — a re-sort would
  // scatter the groups and repeat their header rows.
  sortable?: boolean;
}

// Compact read-only grid in the SpecGauge table style. Selection + double-click
// mirror the WPF GridControl behaviour.
export default function DataTable<T>({
  columns,
  rows,
  rowKey,
  selectedKey,
  onSelect,
  onDoubleClick,
  rowClass,
  emptyText = "No records",
  maxHeight,
  groupKey,
  groupLabel,
  sortable,
}: Props<T>) {
  const hasFit = columns.some((c) => c.fit);
  const wrapRef = useRef<HTMLDivElement>(null);
  const [hoverCol, setHoverCol] = useState<string | null>(null);
  const [sort, setSort] = useState<{ key: string; dir: "asc" | "desc" } | null>(null);

  const canSort = (c: Column<T>) => !!sortable && !c.noSort;
  // Shared by body/footer/filter cells: hover tint and the lavender super-admin shading.
  const cellClass = (c: Column<T>) =>
    [hoverCol === c.key ? "col-hover" : "", c.superAdmin ? "col-superadmin" : "", c.className ?? ""]
      .filter(Boolean)
      .join(" ") || undefined;
  // Header click cycles asc -> desc -> back to the caller's own order.
  const toggleSort = (c: Column<T>) => {
    if (!canSort(c)) return;
    setSort((prev) =>
      prev?.key !== c.key
        ? { key: c.key, dir: "asc" }
        : prev.dir === "asc"
          ? { key: c.key, dir: "desc" }
          : null,
    );
  };

  // The rows as displayed: the caller's order until a header is clicked. Blanks sort
  // last either way, so an empty cell never pushes real values off the top.
  const sortCol = sort ? columns.find((c) => c.key === sort.key) : null;
  const view = (() => {
    if (!sort || !sortCol) return rows;
    const value = (row: T) => {
      const v = sortCol.sortValue ? sortCol.sortValue(row) : (row as never)[sortCol.key];
      return v instanceof Date ? v.getTime() : v;
    };
    const dir = sort.dir === "asc" ? 1 : -1;
    return [...rows].sort((a, b) => {
      const x = value(a);
      const y = value(b);
      const xBlank = x == null || x === "";
      const yBlank = y == null || y === "";
      if (xBlank || yBlank) return xBlank && yBlank ? 0 : xBlank ? 1 : -1;
      if (typeof x === "number" && typeof y === "number") return (x - y) * dir;
      return String(x).localeCompare(String(y), "en-GB", { numeric: true }) * dir;
    });
  })();

  // The selected row is brought into view whenever the selection is set from outside — a
  // dialog opened on a record already chosen (the customer picker) shows that record, not the
  // top of the list. Only the grid's own box scrolls, never the page, and only as far as it
  // must: the row lands just under the sticky header or just above the bottom edge.
  useEffect(() => {
    if (selectedKey == null) return;
    const wrap = wrapRef.current;
    const row = wrap?.querySelector<HTMLTableRowElement>("tbody tr.selected");
    if (!wrap || !row) return;
    const head = wrap.querySelector<HTMLElement>("thead")?.offsetHeight ?? 0;
    const top = row.offsetTop;
    const bottom = top + row.offsetHeight;
    if (top - head < wrap.scrollTop) wrap.scrollTop = top - head;
    else if (bottom > wrap.scrollTop + wrap.clientHeight) wrap.scrollTop = bottom - wrap.clientHeight;
  }, [selectedKey]);

  // Arrow up/down moves the selection like the WPF GridControl.
  const handleKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (!onSelect || view.length === 0) return;
    if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
    e.preventDefault();
    const current = selectedKey != null ? view.findIndex((r) => rowKey(r) === selectedKey) : -1;
    const next =
      e.key === "ArrowDown" ? Math.min(current + 1, view.length - 1) : Math.max(current - 1, 0);
    if (next === current) return;
    onSelect(view[next]);
    wrapRef.current
      ?.querySelectorAll("tbody tr:not(.data-table-group-row)")
      [next]?.scrollIntoView({ block: "nearest" });
  };

  return (
    <div
      ref={wrapRef}
      className="data-table-wrap"
      style={maxHeight ? { maxHeight } : undefined}
      tabIndex={onSelect ? 0 : undefined}
      onKeyDown={handleKeyDown}
    >
      <table className="data-table">
        <thead>
          <tr>
            {columns.map((c) => (
              <th
                key={c.key}
                className={
                  [
                    hoverCol === c.key ? "col-hover" : "",
                    canSort(c) ? "sortable" : "",
                    c.superAdmin ? "col-superadmin" : "",
                    c.className ?? "",
                  ]
                    .filter(Boolean)
                    .join(" ") || undefined
                }
                style={{
                  width: c.fit ? "1%" : c.width,
                  minWidth: c.width,
                  whiteSpace: c.fit ? "nowrap" : undefined,
                  textAlign: c.align ?? "left",
                }}
                onMouseEnter={c.hoverHighlight ? () => setHoverCol(c.key) : undefined}
                onMouseLeave={c.hoverHighlight ? () => setHoverCol(null) : undefined}
                onClick={canSort(c) ? () => toggleSort(c) : undefined}
                title={canSort(c) ? "Click to sort" : undefined}
              >
                {c.header}
                {sort?.key === c.key && (
                  <span className="data-table-sort">{sort.dir === "asc" ? "▲" : "▼"}</span>
                )}
              </th>
            ))}
          </tr>
          {columns.some((c) => c.filter) && (
            <tr className="data-table-filter-row">
              {columns.map((c) => (
                <th key={c.key} className={cellClass(c)}>
                  {c.filter && (
                    <input
                      className="form-input"
                      value={c.filter.value}
                      placeholder={c.filter.placeholder}
                      onChange={(e) => c.filter!.onChange(e.target.value)}
                    />
                  )}
                </th>
              ))}
            </tr>
          )}
        </thead>
        <tbody>
          {view.length === 0 && (
            <tr>
              <td className="data-table-empty" colSpan={columns.length}>
                {emptyText}
              </td>
            </tr>
          )}
          {view.map((row, idx) => {
            const key = rowKey(row);
            const group = groupKey?.(row);
            const showGroup =
              groupKey && (idx === 0 || groupKey(view[idx - 1]) !== group);
            const classes = [
              selectedKey != null && key === selectedKey ? "selected" : "",
              rowClass?.(row) ?? "",
            ]
              .filter(Boolean)
              .join(" ");
            return (
              <Fragment key={key}>
              {showGroup && (
                <tr className="data-table-group-row">
                  <td colSpan={columns.length}>{groupLabel ? groupLabel(row) : group}</td>
                </tr>
              )}
              <tr
                className={classes || undefined}
                onClick={(e) => {
                  onSelect?.(row, e, view);
                  wrapRef.current?.focus();
                }}
                onDoubleClick={() => onDoubleClick?.(row)}
              >
                {columns.map((c) => (
                  <td
                    key={c.key}
                    className={cellClass(c)}
                    style={{
                      textAlign: c.align ?? "left",
                      // Flexible columns clamp (maxWidth 0 + the existing ellipsis CSS) when the
                      // table has best-fit columns, so it can never overflow horizontally.
                      maxWidth: c.width ?? (hasFit && !c.fit ? 0 : undefined),
                      width: c.fit ? "1%" : undefined,
                      whiteSpace: c.fit ? "nowrap" : undefined,
                    }}
                    title={
                      !c.render ? String((row as never)[c.key] ?? "") || undefined : undefined
                    }
                  >
                    {c.render ? c.render(row) : String((row as never)[c.key] ?? "")}
                  </td>
                ))}
              </tr>
              </Fragment>
            );
          })}
        </tbody>
        {/* Totals sit under the last row — skipped when there's nothing to total. */}
        {view.length > 0 && columns.some((c) => c.footer) && (
          <tfoot>
            <tr>
              {columns.map((c) => (
                <td
                  key={c.key}
                  className={cellClass(c)}
                  style={{ textAlign: c.align ?? "left" }}
                >
                  {c.footer?.(view)}
                </td>
              ))}
            </tr>
          </tfoot>
        )}
      </table>
    </div>
  );
}

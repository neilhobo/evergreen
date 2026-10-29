import { ReactNode } from "react";

export interface Column<T> {
  key: string;
  header: ReactNode; // pass a fragment with <br /> for a two-line header
  width?: number; // fixed px; omit for flexible
  // Best-fit: column shrinks to its content. When any column is best-fit, the flexible
  // (width-less) columns clamp with ellipsis so the table can never scroll horizontally.
  fit?: boolean;
  align?: "left" | "right" | "center";
  render?: (row: T) => ReactNode;
}

interface Props<T> {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => number | string;
  selectedKey?: number | string | null;
  onSelect?: (row: T) => void;
  onDoubleClick?: (row: T) => void;
  rowClass?: (row: T) => string | undefined;
  emptyText?: string;
  maxHeight?: number | string;
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
}: Props<T>) {
  const hasFit = columns.some((c) => c.fit);
  return (
    <div className="data-table-wrap" style={maxHeight ? { maxHeight } : undefined}>
      <table className="data-table">
        <thead>
          <tr>
            {columns.map((c) => (
              <th
                key={c.key}
                style={{
                  width: c.fit ? "1%" : c.width,
                  minWidth: c.width,
                  whiteSpace: c.fit ? "nowrap" : undefined,
                  textAlign: c.align ?? "left",
                }}
              >
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 && (
            <tr>
              <td className="data-table-empty" colSpan={columns.length}>
                {emptyText}
              </td>
            </tr>
          )}
          {rows.map((row) => {
            const key = rowKey(row);
            const classes = [
              selectedKey != null && key === selectedKey ? "selected" : "",
              rowClass?.(row) ?? "",
            ]
              .filter(Boolean)
              .join(" ");
            return (
              <tr
                key={key}
                className={classes || undefined}
                onClick={() => onSelect?.(row)}
                onDoubleClick={() => onDoubleClick?.(row)}
              >
                {columns.map((c) => (
                  <td
                    key={c.key}
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
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

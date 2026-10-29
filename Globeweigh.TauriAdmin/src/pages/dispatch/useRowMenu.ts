import { useEffect, useRef, useState } from "react";

// A grid's per-row "⋮" menu: which row's is open and where to draw it, with the open/close
// handling every such menu needs. The grid scrolls, so the menu is rendered position:fixed at
// page level (an absolute menu inside the cell would be clipped) and closed on any scroll, a
// click outside it, Escape or a resize. The Loads, Pending, Completed Loads and Orders (from
// sales) grids have one each.
export interface RowMenu<T> {
  row: T;
  top: number;
  right: number;
}
export function useRowMenu<T extends { id: number }>() {
  const [menu, setMenu] = useState<RowMenu<T> | null>(null);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!menu) return;
    const close = () => setMenu(null);
    const onDown = (e: Event) => {
      if (!ref.current?.contains(e.target as Node)) close();
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") close();
    };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    document.addEventListener("scroll", close, true);
    window.addEventListener("resize", close);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("scroll", close, true);
      window.removeEventListener("resize", close);
    };
  }, [menu]);
  const api = {
    ref,
    // Pressing the open row's button again closes it.
    open(row: T, btn: HTMLElement) {
      const rect = btn.getBoundingClientRect();
      setMenu((cur) =>
        cur?.row.id === row.id ? null : { row, top: rect.bottom + 4, right: window.innerWidth - rect.right },
      );
    },
    close() {
      setMenu(null);
    },
  };
  return [menu, api] as const;
}

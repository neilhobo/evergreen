import styles from "./ProductDetail.module.css";

// Money in a costing zone's currency: true = Euro, false = Sterling, null = Both (no symbol).
export function fmtMoney(n: number, isEuro: boolean | null | undefined): string {
  const sym = isEuro === true ? "€" : isEuro === false ? "£" : "";
  return `${sym}${n.toFixed(2)}`;
}

// A customer price against their zone price, as text and the class that colours it.
export function priceDiff(
  price: number,
  zonePrice: number,
  isEuro: boolean | null | undefined,
): { text: string; className: string } {
  const d = Math.round((price - zonePrice) * 100) / 100;
  if (d === 0) return { text: "Same", className: styles.diffSame };
  const pct = zonePrice !== 0 ? ` (${d > 0 ? "+" : "−"}${Math.abs((d / zonePrice) * 100).toFixed(1)}%)` : "";
  return {
    text: `${d > 0 ? "+" : "−"}${fmtMoney(Math.abs(d), isEuro)}${pct}`,
    className: d < 0 ? styles.diffBelow : styles.diffAbove,
  };
}

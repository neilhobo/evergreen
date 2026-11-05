import { LeaveCheck, STATUS_LABEL } from "../../api/annualLeave";

// Small pieces shared by the Annual Leave tabs.

const DOT: Record<LeaveCheck["level"], string> = { ok: "✓", info: "i", warn: "!", error: "×" };

/** The policy checks on a request: a coloured mark and the sentence, passes included. */
export function CheckList({ checks }: { checks: LeaveCheck[] }) {
  return (
    <>
      {checks.map((c, i) => (
        <div className="al-check" key={i}>
          <span className={`al-dot al-dot-${c.level}`} aria-label={c.level}>
            {DOT[c.level]}
          </span>
          <span>{c.text}</span>
        </div>
      ))}
    </>
  );
}

export function StatusChip({ status }: { status: number }) {
  return <span className={`al-chip al-chip-${status}`}>{STATUS_LABEL[status] ?? status}</span>;
}

/** The checks worth flagging in a list: warnings and errors, or "All checks pass". */
export function flagLine(checks: LeaveCheck[]): { text: string; warn: boolean } {
  const bad = checks.filter((c) => c.level === "warn" || c.level === "error");
  if (bad.length === 0) return { text: "All checks pass", warn: false };
  const short = bad.map((c) =>
    c.text.startsWith("Goes over")
      ? "Over allowance"
      : c.text.startsWith("Only")
        ? "Short notice"
        : c.text.startsWith("Also off")
          ? "Clash"
          : c.text.startsWith("Already has")
            ? "Overlaps other leave"
            : c.text.split(/[:(]/)[0].trim(),
  );
  return { text: short.join(" · "), warn: true };
}

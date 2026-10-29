import { ReactNode } from "react";

interface Props {
  text: string | null | undefined;
  term: string;
}

// Wraps case-insensitive matches of `term` in a highlight mark.
export default function Highlight({ text, term }: Props) {
  const value = text ?? "";
  const needle = term.trim().toLowerCase();
  if (!needle) return <>{value}</>;

  const lower = value.toLowerCase();
  const parts: ReactNode[] = [];
  let pos = 0;
  let idx: number;
  while ((idx = lower.indexOf(needle, pos)) !== -1) {
    if (idx > pos) parts.push(value.slice(pos, idx));
    parts.push(
      <mark className="hl" key={idx}>
        {value.slice(idx, idx + needle.length)}
      </mark>,
    );
    pos = idx + needle.length;
  }
  parts.push(value.slice(pos));
  return <>{parts}</>;
}

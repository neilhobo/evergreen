import { fmtDateTime } from "../format";

interface Props {
  sentAt: string | null;
  sentTo: string | null;
  // What went, for the tooltip: "Emailed" unless the grid says otherwise.
  label?: string;
}

// The green envelope a grid shows once a load's email has gone to the haulier — the "ready for
// collection" one on the Loads grid, the dispatch confirmation on both Completed Loads grids;
// the tooltip says what, when and to whom. Nothing while it has not. Styled by .dsp-emailed in
// pages/dispatch/Dispatch.css.
export default function EmailedMark({ sentAt, sentTo, label = "Emailed" }: Props) {
  if (!sentAt) return null;
  return (
    <span className="dsp-emailed" title={`${label} ${fmtDateTime(sentAt)} to ${sentTo ?? ""}`}>
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden
      >
        <rect x="3" y="5" width="18" height="14" rx="2" />
        <path d="M3 7l9 6 9-6" />
      </svg>
    </span>
  );
}

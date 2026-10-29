interface Props {
  // "From" or "To" — sits inside the field as a small caption, not as a word before it.
  label: string;
  // yyyy-mm-dd, as the native input speaks it.
  value: string;
  onChange: (value: string) => void;
}

// A toolbar date filter: the browser's own date input, sized to the strip (26px, like the search
// box) with its label inside the field as a caption, so From and To each read as one control and
// the pair sits tighter. The browser still supplies the calendar and the dd/mm/yyyy mask, and a
// date can still be typed. Styled by .toolbar-datefld in App.css.
export default function ToolbarDate({ label, value, onChange }: Props) {
  return (
    <label className="toolbar-datefld">
      <span className="toolbar-datefld-k">{label}</span>
      <input
        type="date"
        className="toolbar-datefld-in"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </label>
  );
}

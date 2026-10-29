import { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";

interface Opts {
  title?: string;
  password?: boolean; // mask the display with asterisks
  decimal?: boolean; // show a decimal-point key
  initial?: string;
  max?: number; // shows "Max N" under the display; over it the value turns red and OK is off
}

interface PanelProps extends Opts {
  onSubmit: (value: string) => void;
  // Leave out for a keypad that IS the page (the top bar's Back is the way out); pass it and
  // the panel grows a Cancel key and answers Escape.
  onCancel?: () => void;
  okLabel?: string;
  disabled?: boolean; // OK is unavailable (e.g. a lookup is in flight)
  className?: string; // extra class on the panel (e.g. the full-page sizing)
}

// On-screen numeric keypad, mirroring the WPF KeypadViewModel (digits + Clear + Del). Used
// inline as a whole page (the collection reference) and inside the overlay dialog below.
export function KeypadPanel({
  title, password, decimal, initial, max, onSubmit, onCancel, okLabel = "OK", disabled, className,
}: PanelProps) {
  const [value, setValue] = useState(initial ?? "");

  // Append a decimal point — only once, and not as the first character (mirrors WPF KeypadViewModel).
  const addDot = () => setValue((v) => (v.length === 0 || v.includes(".") ? v : v + "."));

  const over = max !== undefined && value.length > 0 && Number(value) > max;
  const okOff = disabled || over;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCancel?.();
      else if (e.key === "Enter") { if (!okOff) onSubmit(value); }
      else if (e.key === "Backspace") setValue((v) => v.slice(0, -1));
      else if (e.key === "." && decimal) addDot();
      else if (/^[0-9]$/.test(e.key)) setValue((v) => v + e.key);
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [value, onSubmit, onCancel, decimal, okOff]);

  const display = value.length === 0 ? "" : password ? "•".repeat(value.length) : value;
  const keys = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];
  // OK takes whatever of the bottom row the decimal point and Cancel leave it.
  const okSpan = decimal ? 1 : onCancel ? 2 : 3;

  return (
    <div className={`tt-pad${className ? " " + className : ""}`}>
      {title && <div className="tt-pad-header">{title}</div>}
      <div className={`tt-pad-display${over ? " tt-pad-over" : ""}`}>{display}</div>
      {max !== undefined && (
        <div className={`tt-pad-max${over ? " tt-pad-over" : ""}`}>Max {max}</div>
      )}
      <div className="tt-pad-grid">
        {keys.map((k) => (
          <button key={k} className="tt-key" onClick={() => setValue((v) => v + k)}>
            {k}
          </button>
        ))}
        <button className="tt-key" onClick={() => setValue("")}>C</button>
        <button className="tt-key" onClick={() => setValue((v) => v + "0")}>0</button>
        <button className="tt-key" onClick={() => setValue((v) => v.slice(0, -1))}>⌫</button>
        {decimal && <button className="tt-key" onClick={addDot}>.</button>}
        {onCancel && <button className="tt-key tt-cancel" onClick={onCancel}>Cancel</button>}
        <button
          className="tt-key tt-ok"
          style={{ gridColumn: `span ${okSpan}` }}
          disabled={okOff}
          onClick={() => onSubmit(value)}
        >
          {okLabel}
        </button>
      </div>
    </div>
  );
}

interface PadProps extends Opts {
  onResult: (value: string | null) => void;
}

// The same panel, floated over the screen and dismissable by tapping the backdrop.
function Pad({ onResult, ...opts }: PadProps) {
  return (
    <div className="tt-pad-overlay" onMouseDown={(e) => e.target === e.currentTarget && onResult(null)}>
      <KeypadPanel {...opts} onSubmit={(v) => onResult(v)} onCancel={() => onResult(null)} />
    </div>
  );
}

// Imperative promise dialog: resolves to the entered string, or null if cancelled.
export function keypad(opts: Opts = {}): Promise<string | null> {
  return new Promise((resolve) => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);
    const close = (value: string | null) => {
      root.unmount();
      host.remove();
      resolve(value);
    };
    root.render(<Pad {...opts} onResult={close} />);
  });
}

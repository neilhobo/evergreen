import { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";

interface Opts {
  title?: string;
  initial?: string;
}

interface KbdProps extends Opts {
  onResult: (value: string | null) => void;
}

const ROW1 = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"];
const ROW2 = ["Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P"];
const ROW3 = ["A", "S", "D", "F", "G", "H", "J", "K", "L"];
const ROW4 = ["Z", "X", "C", "V", "B", "N", "M"];

// On-screen alphanumeric keyboard, mirroring the WPF KeyboardViewModel. Used for the
// vehicle reg and trailer no. Uppercase (vehicle regs are entered uppercase).
function Kbd({ title, initial, onResult }: KbdProps) {
  const [value, setValue] = useState(initial ?? "");

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onResult(null);
      else if (e.key === "Enter") onResult(value);
      else if (e.key === "Backspace") setValue((v) => v.slice(0, -1));
      else if (e.key.length === 1 && /[a-zA-Z0-9 ]/.test(e.key)) setValue((v) => v + e.key.toUpperCase());
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [value, onResult]);

  const add = (c: string) => setValue((v) => v + c);
  const renderRow = (keys: string[]) =>
    keys.map((k) => (
      <button key={k} className="tt-kbd-key" onClick={() => add(k)}>{k}</button>
    ));

  return (
    <div className="tt-pad-overlay" onMouseDown={(e) => e.target === e.currentTarget && onResult(null)}>
      <div className="tt-pad tt-pad-kbd">
        {title && <div className="tt-pad-header">{title}</div>}
        <div className="tt-pad-display">{value}</div>
        <div className="tt-kbd-rows">
          <div className="tt-kbd-row">{renderRow(ROW1)}</div>
          <div className="tt-kbd-row">{renderRow(ROW2)}</div>
          <div className="tt-kbd-row">{renderRow(ROW3)}</div>
          <div className="tt-kbd-row">
            {renderRow(ROW4)}
            <button className="tt-kbd-key tt-flex2" onClick={() => setValue((v) => v.slice(0, -1))}>⌫</button>
          </div>
          <div className="tt-kbd-row">
            <button className="tt-kbd-key" onClick={() => setValue("")}>Clear</button>
            <button className="tt-kbd-key tt-flex5" onClick={() => add(" ")}>space</button>
            <button className="tt-kbd-key tt-flex2" onClick={() => onResult(null)}>Cancel</button>
            <button className="tt-kbd-key tt-flex2 tt-ok" onClick={() => onResult(value)}>OK</button>
          </div>
        </div>
      </div>
    </div>
  );
}

// Imperative promise dialog: resolves to the entered string, or null if cancelled.
export function keyboard(opts: Opts = {}): Promise<string | null> {
  return new Promise((resolve) => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);
    const close = (value: string | null) => {
      root.unmount();
      host.remove();
      resolve(value);
    };
    root.render(<Kbd {...opts} onResult={close} />);
  });
}

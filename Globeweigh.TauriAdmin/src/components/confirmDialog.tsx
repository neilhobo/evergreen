import { useEffect, useRef } from "react";
import { createRoot } from "react-dom/client";

interface BoxProps {
  message: string;
  okOnly?: boolean;
  onResult: (ok: boolean) => void;
}

function ConfirmBox({ message, okOnly, onResult }: BoxProps) {
  const okRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    okRef.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        onResult(false);
      }
    };
    document.addEventListener("keydown", onKey, true);
    return () => document.removeEventListener("keydown", onKey, true);
  }, [onResult]);

  return (
    <div className="modal-overlay confirm-overlay">
      <div className="modal confirm-box" style={{ width: 360 }}>
        <div className="modal-body confirm-message">{message}</div>
        <div className="modal-footer">
          {!okOnly && (
            <button className="toolbar-btn" onClick={() => onResult(false)}>
              Cancel
            </button>
          )}
          <button className="btn-blue" ref={okRef} onClick={() => onResult(true)}>
            OK
          </button>
        </div>
      </div>
    </div>
  );
}

function show(message: string, okOnly: boolean): Promise<boolean> {
  return new Promise((resolve) => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);
    const close = (ok: boolean) => {
      root.unmount();
      host.remove();
      resolve(ok);
    };
    root.render(<ConfirmBox message={message} okOnly={okOnly} onResult={close} />);
  });
}

// Centered replacement for window.confirm() — no title bar, just the message.
export const confirmDialog = (message: string) => show(message, false);

// Centered replacement for window.alert().
export const messageDialog = (message: string) => show(message, true).then(() => undefined);

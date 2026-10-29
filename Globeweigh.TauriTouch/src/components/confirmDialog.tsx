import { ReactNode, useEffect, useRef } from "react";
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

// Mounts a dialog on its own detached root and resolves once it closes. Shared by the small
// desktop-style box above and the big touch one below.
function mount<T = boolean>(render: (close: (result: T) => void) => ReactNode): Promise<T> {
  return new Promise<T>((resolve) => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);
    const close = (result: T) => {
      root.unmount();
      host.remove();
      resolve(result);
    };
    root.render(render(close));
  });
}

function show(message: string, okOnly: boolean): Promise<boolean> {
  return mount((close) => <ConfirmBox message={message} okOnly={okOnly} onResult={close} />);
}

export interface TouchConfirmOptions {
  /** Big emoji above the title. */
  icon?: string;
  title: string;
  /** The thing being acted on — a pallet code — shown large so it reads at arm's length. */
  detail?: string;
  /** What will happen, in a sentence. */
  message?: string;
  confirmText?: string;
  cancelText?: string;
  /** "danger" turns the confirm button red, for destructive actions. */
  tone?: "normal" | "danger";
}

function TouchConfirmBox({
  icon,
  title,
  detail,
  message,
  confirmText = "Confirm",
  cancelText = "Cancel",
  tone = "normal",
  onResult,
}: TouchConfirmOptions & { onResult: (ok: boolean) => void }) {
  useEffect(() => {
    // Deliberately NOT focusing the confirm button: on a touch screen there is no Enter key
    // to press by reflex, and a focused primary button invites a stray keyboard confirm.
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
    <div className="tt-confirm-overlay">
      <div className="tt-confirm-panel">
        {icon && <div className="tt-confirm-ico">{icon}</div>}
        <div className="tt-confirm-title">{title}</div>
        {detail && <div className="tt-confirm-detail">{detail}</div>}
        {message && <div className="tt-confirm-msg">{message}</div>}
        <div className="tt-confirm-actions">
          <button className="tt-btn tt-ghost tt-confirm-btn" onClick={() => onResult(false)}>
            {cancelText}
          </button>
          <button
            className={`tt-btn tt-confirm-btn ${tone === "danger" ? "tt-danger" : "tt-primary"}`}
            onClick={() => onResult(true)}
          >
            {confirmText}
          </button>
        </div>
      </div>
    </div>
  );
}

// Centered replacement for window.confirm() — no title bar, just the message.
export const confirmDialog = (message: string) => show(message, false);

// Full-screen confirm sized for the factory touch screens: big type, big tap targets, no
// keyboard needed. Use this for anything an operator confirms on the line; confirmDialog
// stays for the small desktop-style prompts (settings, updates).
export const touchConfirmDialog = (options: TouchConfirmOptions) =>
  mount((close) => <TouchConfirmBox {...options} onResult={close} />);

export interface TouchChoice<T extends string> {
  value: T;
  text: string;
  tone?: "primary" | "ghost";
}

// Touch-sized prompt with more than two answers (e.g. Print / Continue / Cancel). Buttons
// render in the order given; resolves the chosen value, or null on Escape.
function TouchChoiceBox<T extends string>({
  icon,
  title,
  message,
  choices,
  onResult,
}: {
  icon?: string;
  title: string;
  message?: string;
  choices: TouchChoice<T>[];
  onResult: (v: T | null) => void;
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        onResult(null);
      }
    };
    document.addEventListener("keydown", onKey, true);
    return () => document.removeEventListener("keydown", onKey, true);
  }, [onResult]);

  return (
    <div className="tt-confirm-overlay">
      <div className="tt-confirm-panel">
        {icon && <div className="tt-confirm-ico">{icon}</div>}
        <div className="tt-confirm-title">{title}</div>
        {message && <div className="tt-confirm-msg">{message}</div>}
        <div className="tt-confirm-actions">
          {choices.map((c) => (
            <button
              key={c.value}
              className={`tt-btn tt-confirm-btn ${c.tone === "primary" ? "tt-primary" : "tt-ghost"}`}
              onClick={() => onResult(c.value)}
            >
              {c.text}
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}

export const touchChoiceDialog = <T extends string>(options: {
  icon?: string;
  title: string;
  message?: string;
  choices: TouchChoice<T>[];
}) => mount<T | null>((close) => <TouchChoiceBox {...options} onResult={close} />);

// Centered replacement for window.alert().
export const messageDialog = (message: string) => show(message, true).then(() => undefined);

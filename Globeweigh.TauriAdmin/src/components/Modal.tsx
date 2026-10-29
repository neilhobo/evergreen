import { ReactNode, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

interface Props {
  title: string;
  width?: number;
  height?: number; // fixed dialog height; omit for content-sized
  minHeight?: number; // lower bound for a content-sized dialog
  maximizable?: boolean; // show a maximize/restore button beside the close ×
  fullScreen?: boolean; // open filling the viewport (ignores width/height)
  draggable?: boolean; // move the dialog by dragging its title bar
  resizable?: boolean; // native resize grip in the bottom-right corner
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  // A strip of its own between the body and the footer: stays in view while the body
  // scrolls (a warning about what is on show, say). Not printed.
  notice?: ReactNode;
}

// Stack of open modals so Escape only closes the topmost one.
const modalStack: Array<() => void> = [];

export default function Modal({
  title,
  width = 700,
  height,
  minHeight,
  maximizable,
  fullScreen,
  draggable,
  resizable,
  onClose,
  children,
  footer,
  notice,
}: Props) {
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  const [maximized, setMaximized] = useState(false);

  // Drag offset from the centred position (title-bar drag when `draggable`).
  const [drag, setDrag] = useState({ x: 0, y: 0 });
  const dragRef = useRef<{ startX: number; startY: number; baseX: number; baseY: number } | null>(null);

  function onHeaderMouseDown(e: React.MouseEvent) {
    if (!draggable || maximized) return;
    if ((e.target as HTMLElement).closest("button")) return; // keep the ×/maximize clickable
    dragRef.current = { startX: e.clientX, startY: e.clientY, baseX: drag.x, baseY: drag.y };
    const onMove = (ev: MouseEvent) => {
      const d = dragRef.current;
      if (!d) return;
      setDrag({ x: d.baseX + ev.clientX - d.startX, y: d.baseY + ev.clientY - d.startY });
    };
    const onUp = () => {
      dragRef.current = null;
      document.removeEventListener("mousemove", onMove);
      document.removeEventListener("mouseup", onUp);
    };
    document.addEventListener("mousemove", onMove);
    document.addEventListener("mouseup", onUp);
    e.preventDefault();
  }

  useEffect(() => {
    const entry = () => closeRef.current();
    modalStack.push(entry);
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && modalStack[modalStack.length - 1] === entry) {
        e.stopPropagation();
        entry();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => {
      const i = modalStack.indexOf(entry);
      if (i !== -1) modalStack.splice(i, 1);
      document.removeEventListener("keydown", onKey);
    };
  }, []);

  // Nudge embedded resize-aware content (e.g. noVNC scaleViewport) to reflow once
  // the new layout settles after a maximize/restore.
  useEffect(() => {
    if (!maximizable) return;
    const id = requestAnimationFrame(() => window.dispatchEvent(new Event("resize")));
    return () => cancelAnimationFrame(id);
  }, [maximized, maximizable]);

  // Maximised: fill the overlay (matches the modal's max-width/height caps).
  // fullScreen: open filling the viewport via the .modal-fullscreen class (overrides caps).
  const sizeStyle = maximized
    ? { width: "calc(100vw - 40px)", height: "calc(100vh - 60px)" }
    : fullScreen
      ? undefined
      : { width, height, minHeight };

  // Portal to <body>: keeps modals out of the app tree so print CSS can page a
  // modal's content (e.g. the delivery note) while hiding #root entirely.
  return createPortal(
    // Clicking the backdrop never closes a dialog — close is explicit (×, Escape, or a footer button).
    <div className="modal-overlay">
      <div
        className={`modal${fullScreen ? " modal-fullscreen" : ""}`}
        style={{
          ...sizeStyle,
          transform: drag.x || drag.y ? `translate(${drag.x}px, ${drag.y}px)` : undefined,
          resize: resizable && !maximized ? "both" : undefined,
        }}
      >
        <div
          className="modal-header"
          style={draggable ? { cursor: "move", userSelect: "none" } : undefined}
          onMouseDown={onHeaderMouseDown}
        >
          <span>{title}</span>
          <div className="modal-header-actions">
            {maximizable && (
              <button
                className="modal-icon-btn"
                onClick={() => setMaximized((m) => !m)}
                title={maximized ? "Restore" : "Maximize"}
                aria-label={maximized ? "Restore" : "Maximize"}
              >
                {maximized ? (
                  <svg width="12" height="12" viewBox="0 0 10 10" aria-hidden="true">
                    <rect x="0.5" y="2.5" width="6" height="6" fill="none" stroke="currentColor" strokeWidth="1" />
                    <path d="M2.5 2.5V0.5h6v6h-2" fill="none" stroke="currentColor" strokeWidth="1" />
                  </svg>
                ) : (
                  <svg width="12" height="12" viewBox="0 0 10 10" aria-hidden="true">
                    <rect x="0.5" y="0.5" width="9" height="9" fill="none" stroke="currentColor" strokeWidth="1" />
                  </svg>
                )}
              </button>
            )}
            <button className="modal-close" onClick={onClose} title="Close">
              ×
            </button>
          </div>
        </div>
        <div className="modal-body">{children}</div>
        {notice && <div className="modal-notice">{notice}</div>}
        {footer && <div className="modal-footer">{footer}</div>}
      </div>
    </div>,
    document.body,
  );
}

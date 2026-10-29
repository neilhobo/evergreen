import { ReactNode, useEffect, useRef } from "react";

interface Props {
  title: string;
  width?: number;
  height?: number; // fixed dialog height; omit for content-sized
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
}

// Stack of open modals so Escape only closes the topmost one.
const modalStack: Array<() => void> = [];

export default function Modal({ title, width = 700, height, onClose, children, footer }: Props) {
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

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

  return (
    <div className="modal-overlay" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="modal" style={{ width, height }}>
        <div className="modal-header">
          <span>{title}</span>
          <button className="modal-close" onClick={onClose} title="Close">
            ×
          </button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-footer">{footer}</div>}
      </div>
    </div>
  );
}

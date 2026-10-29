import { ReactNode } from "react";

interface Props {
  label: string;
  icon?: ReactNode;
  sub?: string;
  green?: boolean;
  disabled?: boolean;
  onClick?: () => void;
}

// Large shop-floor menu/action button.
export default function BigButton({ label, icon, sub, green, disabled, onClick }: Props) {
  return (
    <button
      className={`tt-bigbtn${green ? " tt-green" : ""}`}
      disabled={disabled}
      onClick={onClick}
    >
      {icon && <span className="tt-ico">{icon}</span>}
      <span>{label}</span>
      {sub && <span className="tt-sub">{sub}</span>}
    </button>
  );
}

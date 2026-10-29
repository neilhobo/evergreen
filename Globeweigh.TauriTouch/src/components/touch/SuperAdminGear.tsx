import { requireSuperAdminPin } from "./superAdminPin";

interface Props {
  onOpen: () => void;
}

// Discrete corner gear → passcode prompt → Super Admin screen-settings page (App renders it).
export default function SuperAdminGear({ onOpen }: Props) {
  async function onGear() {
    if (await requireSuperAdminPin()) onOpen();
  }

  return <button className="tt-gear" title="Settings" onClick={onGear}>⚙</button>;
}

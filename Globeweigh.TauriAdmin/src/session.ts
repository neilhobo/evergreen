import { createContext, useContext } from "react";
import { AdminUserDto } from "./types";

// The signed-in user and the session actions every page header needs, so a page can render
// its own PageHeader (profile chip / My Account / Switch user / Log out) without prop
// drilling. Provided by MainWindow from the props App already hands it.
export interface Session {
  user: AdminUserDto;
  signOut: () => void;
  updateUser: (user: AdminUserDto) => void;
  // Replace the session with another user's (Switch user / Return to my account).
  switchUser: (user: AdminUserDto) => void;
}

export const SessionContext = createContext<Session | null>(null);

export function useSession(): Session {
  const s = useContext(SessionContext);
  if (!s) throw new Error("useSession outside SessionContext");
  return s;
}

// "Save details" persistence. The WPF app stores these in HKCU\SOFTWARE\Globeweigh;
// localStorage is the web-side equivalent (same plaintext behaviour, same machine scope).

const KEY = "savedLoginDetails";

export interface SavedDetails {
  email: string;
  password: string;
}

export function loadSavedDetails(): SavedDetails | null {
  const raw = localStorage.getItem(KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as SavedDetails;
  } catch {
    return null;
  }
}

export function saveDetails(details: SavedDetails) {
  localStorage.setItem(KEY, JSON.stringify(details));
}

export function clearSavedDetails() {
  localStorage.removeItem(KEY);
}

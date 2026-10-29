// All server access goes through Globeweigh.Api — the app never talks to the database.
// Hosted inside a WPF WebView2 (a normal browser context) so we use plain fetch; CORS
// is enabled on the API for the dev/cross-origin case.
import { getApiBaseUrl } from "../config";

let bearerToken: string | null = null;

export function setToken(token: string | null) {
  bearerToken = token;
}

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (bearerToken) headers["Authorization"] = `Bearer ${bearerToken}`;

  const response = await fetch(`${getApiBaseUrl()}/api${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const data = await response.json();
      if (data?.message) message = data.message;
    } catch {
      // non-JSON error body — keep the status text
    }
    throw new ApiError(response.status, message);
  }

  if (response.status === 204) return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const apiGet = <T>(path: string) => request<T>("GET", path);
export const apiPost = <T>(path: string, body: unknown) => request<T>("POST", path, body);

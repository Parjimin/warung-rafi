import { HttpError, required } from "./http.ts";
export async function database(path: string, init: RequestInit = {}): Promise<unknown> {
  const key = required("SUPABASE_SERVICE_ROLE_KEY");
  const response = await fetch(`${required("SUPABASE_URL")}/rest/v1/${path}`, {
    ...init, cache: "no-store", signal: AbortSignal.timeout(12_000),
    headers: { apikey: key, Authorization: `Bearer ${key}`, "Content-Type": "application/json", ...init.headers }
  });
  if (!response.ok) {
    if (response.status === 409 || response.status === 400) throw new HttpError(409, "Data berubah atau urutan data belum cocok. Muat ulang dan coba kembali.");
    throw new HttpError(503, "Database belum dapat dihubungi.");
  }
  return response.json();
}
export const rpc = (name: string, data: unknown) => database(`rpc/${name}`, { method: "POST", body: JSON.stringify(data) });

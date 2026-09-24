import { timingSafeEqual, createHash } from "node:crypto";
import { cookies } from "next/headers";
import { HttpError, required } from "./http.ts";
export const SESSION_COOKIE = "warung_admin";
export function sameSecret(left: string, right: string) {
  return timingSafeEqual(createHash("sha256").update(left).digest(), createHash("sha256").update(right).digest());
}
export function device(request: Request) {
  const token = required("DEVICE_API_TOKEN");
  if (token.length < 32) throw new HttpError(503, "Token perangkat belum dikonfigurasi dengan benar.");
  if (!sameSecret(request.headers.get("authorization") ?? "", `Bearer ${token}`)) throw new HttpError(401, "Perangkat belum diizinkan.");
}
export function sameOrigin(request: Request) {
  if (request.headers.get("origin") !== required("APP_ORIGIN")) throw new HttpError(403, "Asal permintaan tidak diizinkan.");
}
export async function authUser(token: string) {
  const response = await fetch(`${required("SUPABASE_URL")}/auth/v1/user`, {
    headers: { apikey: required("SUPABASE_ANON_KEY"), Authorization: `Bearer ${token}` },
    cache: "no-store", signal: AbortSignal.timeout(8_000)
  });
  if (!response.ok) throw new HttpError(401, "Silakan masuk kembali.");
  const user = await response.json();
  if (user.id !== required("ADMIN_USER_ID")) throw new HttpError(403, "Akun ini bukan pengelola warung.");
  return user.id as string;
}
export async function admin() {
  const token = (await cookies()).get(SESSION_COOKIE)?.value;
  if (!token) throw new HttpError(401, "Silakan masuk dahulu.");
  return authUser(token);
}

import { cookies } from "next/headers";
import { authUser, sameOrigin, SESSION_COOKIE } from "../../../../lib/auth.ts";
import { body, handled, HttpError, json, object, required } from "../../../../lib/http.ts";
export async function POST(request: Request) {
  return handled(async () => {
    sameOrigin(request); const input = object(await body(request, 4000));
    if (typeof input.email !== "string" || typeof input.password !== "string" || input.email.length > 254 || input.password.length > 512) throw new HttpError(400, "Isi email dan kata sandi.");
    const response = await fetch(`${required("SUPABASE_URL")}/auth/v1/token?grant_type=password`, {
      method: "POST", headers: { apikey: required("SUPABASE_ANON_KEY"), "Content-Type": "application/json" },
      body: JSON.stringify({ email: input.email, password: input.password }), signal: AbortSignal.timeout(8_000), cache: "no-store"
    });
    if (!response.ok) throw new HttpError(response.status === 429 ? 429 : 401, "Belum berhasil masuk. Periksa akun atau coba beberapa saat lagi.");
    const result = await response.json(); await authUser(result.access_token);
    (await cookies()).set(SESSION_COOKIE, result.access_token, { httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "strict", path: "/", maxAge: Math.min(result.expires_in ?? 3600, 3600) });
    return json({ signedIn: true });
  });
}

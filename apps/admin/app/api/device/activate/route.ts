import { authUser } from "../../../../lib/auth.ts";
import { reserveLogin } from "../../../../lib/login-limit.ts";
import { body, handled, HttpError, json, object, required } from "../../../../lib/http.ts";
export async function POST(request: Request) {
 return handled(async () => {
  const input = object(await body(request, 4000));
  if (typeof input.email !== "string" || typeof input.password !== "string" || input.email.length > 254 || input.password.length > 512) throw new HttpError(400, "Isi email dan kata sandi.");
  await reserveLogin(input.email);
  const response = await fetch(`${required("SUPABASE_URL")}/auth/v1/token?grant_type=password`, {
   method: "POST", headers: { apikey: required("SUPABASE_ANON_KEY"), "Content-Type": "application/json" },
   body: JSON.stringify({ email: input.email, password: input.password }), cache: "no-store", redirect: "error", signal: AbortSignal.timeout(8000)
  });
  if (!response.ok) throw new HttpError(response.status === 429 ? 429 : 401, "Belum berhasil masuk. Periksa akun atau coba beberapa saat lagi.");
  const result = await response.json(); await authUser(result.access_token);
  const deviceToken = required("DEVICE_API_TOKEN"), deviceId = required("DEVICE_ID");
  if (deviceToken.length < 32 || deviceToken.length > 512 || /\s/.test(deviceToken) || !deviceId || deviceId.length > 80) throw new HttpError(503, "Koneksi kasir belum disiapkan pengelola.");
  return json({ deviceToken, deviceId });
 });
}

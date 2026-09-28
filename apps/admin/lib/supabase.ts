import { HttpError, required } from "./http.ts";

// Keep the existing environment name so installed servers need no key rotation.
// Hosted secret keys are opaque API keys, while legacy service_role keys are JWTs.
export function serviceHeaders(): Record<string, string> {
  const key = required("SUPABASE_SERVICE_ROLE_KEY");
  if (key.startsWith("sb_publishable_") || /\s/.test(key))
    throw new HttpError(503, "Kunci backend Supabase belum dikonfigurasi dengan benar.");
  return key.startsWith("sb_secret_") ? { apikey: key } : { apikey: key, Authorization: `Bearer ${key}` };
}

import { createHmac } from "node:crypto";
import { rpc } from "./db.ts";
import { HttpError, required } from "./http.ts";
export async function reserveLogin(email: string) {
 // No raw email or IP is stored. The limit is shared across serverless instances.
 const key = createHmac("sha256", required("SUPABASE_SERVICE_ROLE_KEY")).update(email.trim().toLowerCase()).digest("hex");
 const allowed = await rpc("reserve_login_attempt", { p_key: key });
 if (allowed !== true) throw new HttpError(429, "Percobaan masuk terlalu sering. Coba kembali setelah sepuluh menit.");
}

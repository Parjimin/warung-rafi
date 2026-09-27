import { device } from "../../../../lib/auth.ts";
import { handled, json, required } from "../../../../lib/http.ts";
import { rpc } from "../../../../lib/db.ts";
export async function GET(request: Request) {
 return handled(async () => {
  device(request);
  const setup = await rpc("device_setup", {}) as { schema: number; serverTime: string };
  return json({ ...setup, deviceId: required("DEVICE_ID"), qrisConfigured: !!process.env.MIDTRANS_SERVER_KEY && !!process.env.MIDTRANS_MERCHANT_ID, sheetsConfigured: !!process.env.GOOGLE_SHEETS_ID && !!process.env.GOOGLE_SERVICE_ACCOUNT_JSON });
 });
}

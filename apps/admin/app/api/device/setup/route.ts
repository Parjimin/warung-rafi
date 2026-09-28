import { device } from "../../../../lib/auth.ts";
import { handled, json, required } from "../../../../lib/http.ts";
import { rpc } from "../../../../lib/db.ts";
import { integrationConfiguration } from "../../../../lib/readiness.ts";
export async function GET(request: Request) {
 return handled(async () => {
  device(request);
  const setup = await rpc("device_setup", {}) as { schema: number; serverTime: string };
  const config = integrationConfiguration();
  return json({ ...setup, deviceId: required("DEVICE_ID"), qrisConfigured: config.qris, sheetsConfigured: config.sheets });
 });
}

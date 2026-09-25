import { device } from "../../../../lib/auth.ts";
import { body, handled, json, object, required } from "../../../../lib/http.ts";
import { financeEvents } from "../../../../lib/finance.ts";
import { rpc } from "../../../../lib/db.ts";
export const runtime = "nodejs";
export async function POST(request: Request) {
  return handled(async () => {
    device(request);
    const batch = financeEvents(object(await body(request)).events);
    const accepted = await rpc("receive_finance_batch", { p_device: required("DEVICE_ID"), p_events: batch });
    return json({ accepted });
  });
}

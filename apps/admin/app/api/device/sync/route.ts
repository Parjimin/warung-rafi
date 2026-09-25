import { device } from "../../../../lib/auth.ts";
import { body, handled, json, object, required } from "../../../../lib/http.ts";
import { events } from "../../../../lib/sync.ts";
import { rpc } from "../../../../lib/db.ts";
export const runtime = "nodejs";
export async function POST(request: Request) {
  return handled(async () => {
    device(request);
    const batch = events(object(await body(request)).events);
    const result = await rpc("receive_device_batch", { p_device: required("DEVICE_ID"), p_events: batch }) as { accepted: string[]; conflict: boolean };
    if (result.conflict) return json({ error: "Urutan data berbeda. Data tetap di laptop; pengelola dapat memeriksa sinkronisasi.", conflict: true }, 409);
    return json({ accepted: result.accepted });
  });
}

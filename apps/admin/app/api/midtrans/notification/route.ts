import { body, handled, json, required } from "../../../../lib/http.ts";
import { verifiedNotification } from "../../../../lib/midtrans.ts";
import { rpc } from "../../../../lib/db.ts";
export const runtime = "nodejs";
export async function POST(request: Request) {
  return handled(async () => {
    const payment = await verifiedNotification(await body(request, 32_000), {
      key: required("MIDTRANS_SERVER_KEY"), merchantId: required("MIDTRANS_MERCHANT_ID"), environment: required("MIDTRANS_ENV")
    });
    await rpc("record_provider_payment", { p_payment: payment });
    // Acknowledge only after durable database commit. Retries are deduplicated by transaction ID.
    return json({ received: true });
  });
}

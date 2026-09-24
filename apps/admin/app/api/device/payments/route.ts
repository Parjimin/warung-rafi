import { device } from "../../../../lib/auth.ts";
import { handled, HttpError, json } from "../../../../lib/http.ts";
import { database } from "../../../../lib/db.ts";
export async function GET(request: Request) {
  return handled(async () => {
    device(request);
    const after = new URL(request.url).searchParams.get("after") ?? "0";
    if (!/^\d{1,15}$/.test(after) || !Number.isSafeInteger(Number(after))) throw new HttpError(400, "Cursor tidak valid.");
    const rows = await database(`payment_notifications?sequence=gt.${after}&order=sequence.asc&limit=100&select=sequence,transaction_id,amount,paid_at`) as { sequence: number; transaction_id: string; amount: number; paid_at: string }[];
    return json({ payments: rows.map(row => ({ sequence: row.sequence, transactionId: row.transaction_id, amount: row.amount, paidAt: row.paid_at })) });
  });
}

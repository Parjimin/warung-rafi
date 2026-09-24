import { createHash, timingSafeEqual } from "node:crypto";
import { HttpError, object } from "./http.ts";
export type VerifiedPayment = { transactionId: string; merchantId: string; orderId: string; amount: number; status: string; paidAt: string };
export function rupiah(input: unknown): number {
  if (typeof input !== "string" || !/^[0-9]{1,12}(\.00)?$/.test(input)) throw new HttpError(400, "Nominal pembayaran tidak valid.");
  const amount = Number(input);
  if (!Number.isSafeInteger(amount) || amount <= 0) throw new HttpError(400, "Nominal pembayaran tidak valid.");
  return amount;
}
export function verifySignature(input: unknown, key: string): Record<string, unknown> {
  const value = object(input);
  for (const field of ["order_id", "status_code", "gross_amount", "signature_key", "transaction_id"]) {
    if (typeof value[field] !== "string" || !value[field] || String(value[field]).length > 256) throw new HttpError(400, "Notifikasi tidak lengkap.");
  }
  if (!/^[a-fA-F0-9]{128}$/.test(String(value.signature_key))) throw new HttpError(401, "Tanda tangan tidak valid.");
  const expected = createHash("sha512").update(`${value.order_id}${value.status_code}${value.gross_amount}${key}`).digest();
  if (!timingSafeEqual(expected, Buffer.from(String(value.signature_key), "hex"))) throw new HttpError(401, "Tanda tangan tidak valid.");
  rupiah(value.gross_amount);
  return value;
}
function providerTime(value: unknown): string {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$/.test(value)) throw new HttpError(502, "Waktu provider tidak valid.");
  const parsed = new Date(value.replace(" ", "T") + "+07:00");
  if (!Number.isFinite(parsed.getTime())) throw new HttpError(502, "Waktu provider tidak valid.");
  return parsed.toISOString();
}
export function authoritativePayment(notification: Record<string, unknown>, input: unknown, merchantId: string): VerifiedPayment {
  const status = object(input);
  if (status.transaction_id !== notification.transaction_id || status.order_id !== notification.order_id || status.merchant_id !== merchantId || status.payment_type !== "qris" || status.currency !== "IDR" || rupiah(status.gross_amount) !== rupiah(notification.gross_amount)) throw new HttpError(422, "Identitas pembayaran tidak cocok.");
  if (!["pending", "settlement", "deny", "cancel", "expire", "refund", "partial_refund"].includes(String(status.transaction_status))) throw new HttpError(422, "Status belum didukung.");
  if (status.transaction_status === "settlement" && status.fraud_status && status.fraud_status !== "accept") throw new HttpError(422, "Pembayaran belum diterima provider.");
  return {
    transactionId: String(status.transaction_id), merchantId, orderId: String(status.order_id),
    amount: rupiah(status.gross_amount), status: String(status.transaction_status),
    paidAt: providerTime(status.settlement_time ?? status.transaction_time)
  };
}
export async function verifiedNotification(input: unknown, config: { key: string; merchantId: string; environment: string }, fetcher: typeof fetch = fetch) {
  const notification = verifySignature(input, config.key);
  if (!["sandbox", "production"].includes(config.environment)) throw new HttpError(503, "Lingkungan pembayaran tidak valid.");
  const host = config.environment === "production" ? "api.midtrans.com" : "api.sandbox.midtrans.com";
  // The SHA512 signature does not bind status/merchant/transaction_id. Always ask the provider.
  const response = await fetcher(`https://${host}/v2/${encodeURIComponent(String(notification.transaction_id))}/status`, {
    headers: { Authorization: `Basic ${Buffer.from(config.key + ":").toString("base64")}`, Accept: "application/json" },
    cache: "no-store", signal: AbortSignal.timeout(8_000)
  });
  if (!response.ok) throw new HttpError(503, "Status provider belum dapat diverifikasi.");
  return authoritativePayment(notification, await response.json(), config.merchantId);
}

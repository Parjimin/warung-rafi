import { HttpError, object } from "./http.ts";

const fail = (message = "Isian keuangan belum lengkap atau tidak valid."): never => { throw new HttpError(400, message); };
const text = (value: unknown, min = 1, max = 200): string => typeof value === "string" && value.trim().length >= min && value.trim().length <= max ? value.trim() : fail();
const id = (value: unknown): string => typeof value === "string" && /^[a-f0-9]{32}$/.test(value) ? value : fail();
const integer = (value: unknown, min = 0, max = 1_000_000_000): number => typeof value === "number" && Number.isSafeInteger(value) && value >= min && value <= max ? value : fail("Nominal harus berupa rupiah bulat dalam batas yang diizinkan.");
export function calendarDate(value: unknown): string {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value) || !Number.isFinite(Date.parse(value)) || new Date(value).toISOString().slice(0, 10) !== value) return fail("Tanggal tidak valid.");
  return value;
}
export function reportQuery(params: URLSearchParams) {
  const from = calendarDate(params.get("from")); const to = calendarDate(params.get("to"));
  const days = (Date.parse(to) - Date.parse(from)) / 86400000;
  if (days < 0 || days > 30) fail("Pilih rentang tanggal maksimal 31 hari.");
  const page = (key: string) => { const value = params.get(key) ?? "0"; if (!/^\d{1,6}$/.test(value)) return fail(); return integer(Number(value), 0, 100000); };
  return { p_from: from, p_to: to, p_order_page: page("orders"), p_provider_page: page("providers"), p_payout_page: page("payouts") };
}
export type FinanceCommand = { id: string; revision: number; action: string; reason: string; [key: string]: unknown };
export function financeCommand(input: unknown): FinanceCommand {
  const value = object(input);
  const common = { id: id(value.id), revision: integer(value.revision, 0, Number.MAX_SAFE_INTEGER), action: text(value.action), reason: text(value.reason, 3) };
  const transaction = () => text(value.transactionId, 1, 200);
  switch (common.action) {
    case "match": return { ...common, transactionId: transaction(), deviceId: text(value.deviceId, 1, 80), orderId: id(value.orderId), allowDifference: value.allowDifference === true };
    case "unmatch": case "estimate_fee": return { ...common, transactionId: transaction() };
    case "fee_profile": {
      const from = calendarDate(value.from); const to = calendarDate(value.to);
      if (to < from) fail("Akhir masa tarif harus sesudah awalnya.");
      if (!["half_up", "floor", "ceiling"].includes(String(value.rounding))) fail();
      return { ...common, merchantId: text(value.merchantId, 1, 200), label: text(value.label, 3, 80), from, to, rateBps: integer(value.rateBps, 0, 10000), fixedFee: integer(value.fixedFee), rounding: value.rounding };
    }
    case "actual_cost": return { ...common, transactionId: transaction(), mdr: integer(value.mdr), other: integer(value.other), refunded: integer(value.refunded), reference: text(value.reference, 3) };
    case "payout": {
      if (!Array.isArray(value.transactions) || !value.transactions.length || value.transactions.length > 50) fail("Pilih 1–50 transaksi untuk satu pencairan.");
      const transactions = (value.transactions as unknown[]).map(v => text(v, 1, 200)).sort();
      if (new Set(transactions).size !== transactions.length) fail("Transaksi pencairan tidak boleh berulang.");
      if (typeof value.accountLast4 !== "string" || !/^\d{4}$/.test(value.accountLast4)) fail("Isi empat digit terakhir rekening.");
      return { ...common, transactions, reference: text(value.reference, 3), reportedOn: calendarDate(value.reportedOn), bankName: text(value.bankName, 2, 50), accountLast4: value.accountLast4, fee: integer(value.fee), adjustment: integer(value.adjustment, -1_000_000_000) };
    }
    case "bank_receipt": return { ...common, payoutId: id(value.payoutId), receivedOn: calendarDate(value.receivedOn), amount: integer(value.amount, 0, 100_000_000_000), reference: text(value.reference, 3) };
    case "void_payout": return { ...common, payoutId: id(value.payoutId) };
    default: return fail("Tindakan keuangan tidak dikenal.");
  }
}

// Browser input is deliberately strict: empty input never means a known zero.
export function rupiahInput(value: string, signed = false): number {
  if (!(signed ? /^[+-]?\d+$/ : /^\d+$/).test(value.trim())) throw new Error("Isi nominal rupiah bulat tanpa titik/koma; isi 0 jika memang nol.");
  const n = Number(value); if (!Number.isSafeInteger(n)) throw new Error("Nominal terlalu besar."); return n;
}
export function estimateFee(amount: number, rateBps: number, fixedFee: number, rounding: string): number {
  integer(amount, 1); integer(rateBps, 0, 10000); integer(fixedFee);
  const product = BigInt(amount) * BigInt(rateBps); const base = 10000n;
  const fee = rounding === "floor" ? product / base : rounding === "ceiling" ? (product + base - 1n) / base : rounding === "half_up" ? (product + base / 2n) / base : fail();
  return Number(fee) + fixedFee;
}

export type Match = { transaction_id: string; device_id: string; order_id: string; difference: number; reason: string };
export type Cost = { estimated_fee: number | null; profile: Record<string, unknown> | null; actual_mdr: number | null; actual_other: number | null; actual_refund: number | null; reference: string | null };
export type ProviderRow = { transaction_id: string; merchant_id: string; order_id: string; amount: number; paid_at: string; status: string; match: Match | null; cost: Cost | null; payout_id: string | null };
export type OrderRow = { device_id: string; id: string; number: string; paid_at: string; amount: number; method: number; match: Match | null };
export type PayoutRow = { id: string; reference: string; reported_on: string; bank_name: string; account_last4: string; gross: number; transaction_fees: number; refunds: number; fee: number; adjustment: number; expected_net: number; received_amount: number | null; received_on: string | null; bank_reference: string | null; voided_at: string | null; reason: string; transactions: string[] };
export type FinanceSnapshot = {
  revision: number; generatedAt: string; from: string; to: string;
  summary: { salesCount: number; gross: number; cashSales: number; qrisSales: number; refunds: number; providerCount: number; providerGross: number; unmatchedOrders: number; unmatchedProviders: number; differences: number; actualFees: number; actualKnown: number; estimateFees: number; estimateKnown: number; providerRefunds: number; payoutNet: number; receivedBank: number };
  counts: { orders: number; providers: number; payouts: number };
  orders: OrderRow[]; providers: ProviderRow[]; payouts: PayoutRow[];
  profiles: { id: string; merchant_id: string; label: string; valid_from: string; valid_to: string; rate_bps: number; fixed_fee: number; rounding: string }[];
  devices: { device_id: string; pending_count: number | null; last_seen_at: string | null; last_report_at: string | null }[];
  audit: { id: string; action: string; actor: string; reason: string; created_at: string; revision: number; before_value: unknown; after_value: unknown }[];
};

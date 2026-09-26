// Browser/API transport fixture; financial constraints and arithmetic are checked in PostgreSQL.
import type { FinanceSnapshot, FinanceCommand, ProviderRow } from "../lib/reconciliation.ts";
export function financeFixture(): FinanceSnapshot {
  const day = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Jakarta", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  const paid = day + "T04:00:00Z";
  const providers: ProviderRow[] = [1, 2].map(n => ({ transaction_id: `QRIS-UJI-00${n}`, merchant_id: "merchant-uji", order_id: `QR-${n}`, amount: 22500, paid_at: paid, status: "settlement", match: null, cost: null, payout_id: null }));
  return { revision: 0, from: day, to: day, generatedAt: new Date().toISOString(),
    summary: { salesCount: 5, gross: 90000, cashSales: 45000, qrisSales: 45000, refunds: 1000, providerCount: 2, providerGross: 45000, unmatchedOrders: 2, unmatchedProviders: 2, differences: 0, actualFees: 0, actualKnown: 0, estimateFees: 0, estimateKnown: 0, providerRefunds: 0, payoutNet: 0, receivedBank: 0 },
    counts: { orders: 2, providers: 2, payouts: 0 },
    orders: [1, 2].map(n => ({ device_id: "kasir-utama", id: String(n).repeat(32), number: `WR-260926-00${n}`, paid_at: paid, amount: 22500, method: 1, match: null })), providers, payouts: [], profiles: [],
    devices: [{ device_id: "kasir-utama", pending_count: 3, last_seen_at: paid, last_report_at: paid }], audit: [] };
}
export function applyFixture(s: FinanceSnapshot, c: FinanceCommand, actor: string) {
  const p = s.providers.find(p => p.transaction_id === c.transactionId); const q = s.payouts.find(p => p.id === c.payoutId);
  const before = structuredClone(p ?? q ?? null);
  if (c.action === "match") { const o = s.orders.find(o => o.id === c.orderId)!; p!.match = { transaction_id: p!.transaction_id, device_id: o.device_id, order_id: o.id, difference: p!.amount - o.amount, reason: c.reason }; o.match = p!.match; }
  if (c.action === "unmatch") { const o = s.orders.find(o => o.id === p!.match?.order_id); if (o) o.match = null; p!.match = null; }
  if (c.action === "fee_profile") s.profiles.push({ id: c.id, merchant_id: String(c.merchantId), label: String(c.label), valid_from: String(c.from), valid_to: String(c.to), rate_bps: Number(c.rateBps), fixed_fee: Number(c.fixedFee), rounding: String(c.rounding) });
  if (c.action === "actual_cost" || c.action === "estimate_fee") {
    p!.cost ??= { estimated_fee: null, profile: null, actual_mdr: null, actual_other: null, actual_refund: null, reference: null };
    if (c.action === "estimate_fee") { p!.cost.estimated_fee = 146; p!.cost.profile = { ...s.profiles[0] }; }
    else { p!.cost.actual_mdr = Number(c.mdr); p!.cost.actual_other = Number(c.other); p!.cost.actual_refund = Number(c.refunded); p!.cost.reference = String(c.reference); }
  }
  if (c.action === "payout") {
    const items = s.providers.filter(p => (c.transactions as string[]).includes(p.transaction_id));
    const gross = items.reduce((v, p) => v + p.amount, 0); const fees = items.reduce((v, p) => v + p.cost!.actual_mdr! + p.cost!.actual_other!, 0); const refunds = items.reduce((v, p) => v + p.cost!.actual_refund!, 0);
    s.payouts.push({ id: c.id, reference: String(c.reference), reported_on: String(c.reportedOn), bank_name: String(c.bankName), account_last4: String(c.accountLast4), gross, transaction_fees: fees, refunds, fee: Number(c.fee), adjustment: Number(c.adjustment), expected_net: gross - fees - refunds - Number(c.fee) + Number(c.adjustment), received_amount: null, received_on: null, bank_reference: null, voided_at: null, reason: c.reason, transactions: c.transactions as string[] });
    items.forEach(p => p.payout_id = c.id);
  }
  if (c.action === "bank_receipt") { q!.received_amount = Number(c.amount); q!.received_on = String(c.receivedOn); q!.bank_reference = String(c.reference); }
  if (c.action === "void_payout") { q!.voided_at = new Date().toISOString(); s.providers.filter(p => p.payout_id === q!.id).forEach(p => p.payout_id = null); }
  s.revision++; s.counts.payouts = s.payouts.length;
  s.summary.unmatchedOrders = s.orders.filter(o => !o.match).length; s.summary.unmatchedProviders = s.providers.filter(p => !p.match).length;
  s.summary.actualKnown = s.providers.filter(p => p.cost?.actual_mdr != null).length; s.summary.actualFees = s.providers.reduce((v, p) => v + (p.cost?.actual_mdr ?? 0) + (p.cost?.actual_other ?? 0), 0);
  s.summary.estimateKnown = s.providers.filter(p => p.cost?.estimated_fee != null).length; s.summary.estimateFees = s.providers.reduce((v, p) => v + (p.cost?.estimated_fee ?? 0), 0);
  s.summary.providerRefunds = s.providers.reduce((v, p) => v + (p.cost?.actual_refund ?? 0), 0);
  s.summary.payoutNet = s.payouts.filter(p => !p.voided_at).reduce((v, p) => v + p.expected_net, 0); s.summary.receivedBank = s.payouts.filter(p => !p.voided_at).reduce((v, p) => v + (p.received_amount ?? 0), 0);
  s.audit.unshift({ id: c.id, action: c.action, actor, reason: c.reason, created_at: new Date().toISOString(), revision: s.revision, before_value: before, after_value: structuredClone(p ?? q ?? s.payouts.at(-1) ?? {}) });
}

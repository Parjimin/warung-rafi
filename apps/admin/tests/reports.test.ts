import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { buildReport, csvCell, reportCsv, reportFilter, type ReportSource } from "../lib/reports.ts";
const source = () => JSON.parse(readFileSync(new URL("../../../database/tests/fixtures/report-contract.json", import.meta.url), "utf8")) as ReportSource;
const id = "a".repeat(32);
test("real PostgreSQL projection matches C# ledger and manual totals", () => {
 assert.equal(source().orders.some(o => "customerLabel" in o.payload.order), false);
 const report = buildReport(id, source());
 assert.deepEqual(report.totals, { sales: 4, gross: 46500, cash: 31500, qris: 15000, refunds: 37500, expenses: 10000, providerGross: 0, actualFees: 0, unknownFees: 0, payoutNet: 0, receivedBank: 0 });
 const daily = report.tables.find(t => t.name === "Ringkasan_Harian")!;
 assert.equal(daily.rows[0][daily.columns.indexOf("bruto_kurang_refund_periode")], 9000);
 const cash = report.tables.find(t => t.name === "Kas_Harian")!;
 const closed = cash.rows.find(r => r[cash.columns.indexOf("status")] === "Ditutup")!;
 assert.equal(closed[cash.columns.indexOf("seharusnya")], 112500); assert.equal(closed[cash.columns.indexOf("fisik")], 112000); assert.equal(closed[cash.columns.indexOf("selisih")], -500);
 assert.equal(report.tables.length, 14); assert.equal(report.tables.find(t => t.name === "Detail_Penjualan")!.rows.reduce((n, r) => n + Number(r[8]), 0), report.totals.gross);
});
test("CSV neutralizes formulas while preserving numeric money and escaped multiline text", () => {
 for (const s of ["=SUM(1,2)", "+cmd", "-cmd", "@SUM(1)", " \t=1", "\r=1", "\tname", "0001", "1234567890123456789"]) assert.ok(csvCell(s).startsWith('"\''), s);
 assert.equal(csvCell(-125), "-125"); assert.equal(csvCell(null), ""); assert.equal(csvCell(0), "0"); assert.equal(csvCell('name,"q"\nline'), '"name,""q""\nline"');
 const s = source(); s.orders[0].payload.order.lines[0].name = '=HYPERLINK("https://invalid.test")';
 const report = buildReport(id, s), csv = reportCsv(report, "Detail_Penjualan");
 assert.ok(csv.startsWith("\uFEFF")); assert.ok(csv.includes("snapshot_wib")); assert.ok(csv.includes("'=")); assert.ok(csv.endsWith("\r\n"));
 assert.throws(() => reportCsv(report, "../../secrets"));
});
test("unknown provider fees remain blank and known zero stays numeric", () => {
 const s = source(); const base = { transaction_id: "p1", merchant_id: "fixture", order_id: "q1", amount: 10000, paid_at: "2026-09-25T10:00:00Z", updated_at: "2026-09-25T10:00:00Z", status: "settlement", in_period: true, match: null, payout_id: null };
 s.providers = [{ ...base, cost: null }, { ...base, transaction_id: "p2", cost: { estimated_fee: 50, profile: null, actual_mdr: 0, actual_other: 0, actual_refund: 0, reference: "Report" } }];
 const r = buildReport(id, s); assert.equal(r.totals.gross, 46500); assert.equal(r.totals.providerGross, 20000); assert.equal(r.totals.unknownFees, 1);
 const t = r.tables.find(t => t.name === "Pembayaran_QRIS")!; const idx = t.columns.indexOf("mdr_aktual"); assert.equal(t.rows[0][idx], null); assert.equal(t.rows[1][idx], 0);
});
test("old-order refund in the current period affects period refunds without inventing sales", () => {
 const s = source(); const r = s.refunds.find(r => r.state === 1)!; s.orders = []; s.refunds = [r]; s.sessions = []; s.events = [];
 const report = buildReport(id, s); assert.equal(report.totals.gross, 0); assert.equal(report.totals.refunds, r.amount); assert.equal(report.tables.find(t => t.name === "Rekap_Produk")!.rows.length, 0);
});
test("report fails closed on source inconsistencies and filters reject excess periods", () => {
 const s = source(); s.orders[0].payload.order.lines[0].subtotal++; assert.throws(() => buildReport(id, s));
 const other = source(); other.sessions[0].expected++; assert.throws(() => buildReport(id, other));
 assert.throws(() => reportFilter({ mode: "calendar", from: "2026-09-01", to: "2026-10-02" })); assert.throws(() => reportFilter({ mode: "session", deviceId: "kasir", sessionId: "invalid" }));
 assert.deepEqual(reportFilter({ mode: "calendar", from: "2026-09-01", to: "2026-09-30", actor: "forged" }), { mode: "calendar", from: "2026-09-01", to: "2026-09-30" });
});
test("bank receipt from an earlier payout belongs only to the receipt period and void is excluded", () => {
 const s = source(); s.payouts = [{ id: "payout", reference: "Earlier payout", reported_on: "2026-09-24", bank_name: "Bank Uji", account_last4: "1234", gross: 10000, transaction_fees: 100, refunds: 0, fee: 0, adjustment: 0, expected_net: 9900, received_amount: 9800, received_on: "2026-09-25", bank_reference: "Bank report", voided_at: null, reason: "Fixture", transactions: [], items: [], reported_in_period: false, received_in_period: true }];
 const r = buildReport(id, s); assert.equal(r.totals.payoutNet, 0); assert.equal(r.totals.receivedBank, 9800); assert.equal(r.totals.gross, 46500);
 s.payouts[0].voided_at = s.capturedAt; assert.equal(buildReport(id, s).totals.receivedBank, 0);
});

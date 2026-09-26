import { test } from "node:test";
import assert from "node:assert/strict";
import { calendarDate, reportQuery, financeCommand, rupiahInput, estimateFee } from "../lib/reconciliation.ts";
const base = { id: "a".repeat(32), revision: 1, reason: "Checked statement" };
test("WIB report calendar accepts leap days and at most 31 inclusive days", () => {
  assert.equal(calendarDate("2024-02-29"), "2024-02-29");
  for (const date of ["2026-02-29", "2026-02-30", "yesterday", null]) assert.throws(() => calendarDate(date));
  assert.deepEqual(reportQuery(new URLSearchParams("from=2026-01-01&to=2026-01-31&providers=2")), { p_from: "2026-01-01", p_to: "2026-01-31", p_order_page: 0, p_provider_page: 2, p_payout_page: 0 });
  for (const query of ["from=2026-01-01&to=2026-02-01", "from=2026-02-01&to=2026-01-01", "from=2026-01-01&to=2026-01-01&orders=-1"]) assert.throws(() => reportQuery(new URLSearchParams(query)));
});
test("money inputs distinguish unknown from zero and reject fractions/formatting", () => {
  for (const value of ["", " ", "1,000", "1.5", "1e3", "-1", "Infinity"]) assert.throws(() => rupiahInput(value));
  assert.equal(rupiahInput("0"), 0); assert.equal(rupiahInput("-125", true), -125); assert.equal(rupiahInput("+125", true), 125);
  assert.throws(() => rupiahInput("9007199254740992"));
});
test("estimated MDR uses integer arithmetic and explicit rounding", () => {
  assert.equal(estimateFee(22500, 65, 10, "half_up"), 156);
  assert.equal(estimateFee(100, 50, 0, "half_up"), 1);
  assert.equal(estimateFee(100, 50, 0, "floor"), 0);
  assert.equal(estimateFee(101, 50, 7, "ceiling"), 8);
  assert.equal(estimateFee(1000000000, 9999, 0, "half_up"), 999900000);
  assert.throws(() => estimateFee(22500, 65, 0, "automatic"));
});
test("actual cost command strips actor and keeps explicit zeroes", () => {
  const c = financeCommand({ ...base, action: "actual_cost", transactionId: "proof", mdr: 0, other: 0, refunded: 0, reference: "statement", actor: "forged" });
  assert.equal(c.actor, undefined); assert.equal(c.mdr, 0);
  for (const mdr of [null, undefined, "0", -1, 1.5]) assert.throws(() => financeCommand({ ...c, mdr }));
  assert.throws(() => financeCommand({ ...c, id: "1", revision: -1 }));
});
test("payout command sorts identities, rejects duplicates and limits batches", () => {
  const c = { ...base, action: "payout", transactions: ["b", "a"], reference: "report", reportedOn: "2026-09-26", bankName: "Bank", accountLast4: "1234", fee: 0, adjustment: -5 };
  assert.deepEqual(financeCommand(c).transactions, ["a", "b"]);
  for (const transactions of [[], ["a", "a"], Array.from({ length: 51 }, (_, i) => String(i))]) assert.throws(() => financeCommand({ ...c, transactions }));
  assert.throws(() => financeCommand({ ...c, accountLast4: "12345" }));
});
test("mismatch acknowledgement requires a real boolean; command actions are closed", () => {
  const c = { ...base, action: "match", transactionId: "proof", deviceId: "kasir", orderId: "b".repeat(32) };
  assert.equal(financeCommand({ ...c, allowDifference: "true" }).allowDifference, false);
  assert.equal(financeCommand({ ...c, allowDifference: true }).allowDifference, true);
  assert.throws(() => financeCommand({ ...base, action: "delete_sale" }));
  assert.throws(() => financeCommand({ ...base, action: "fee_profile", merchantId: "m", label: "tariff", from: "2026-10-01", to: "2026-09-01", rateBps: 0, fixedFee: 0, rounding: "floor" }));
});

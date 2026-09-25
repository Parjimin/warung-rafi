import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { financeEvents } from "../lib/finance.ts";
import { HttpError } from "../lib/http.ts";
const contract = JSON.parse(readFileSync(new URL("../../../database/tests/fixtures/finance-contract.json", import.meta.url), "utf8"));
test("actual C# journal serialization crosses the finance API boundary", () => {
  const events = financeEvents(contract.events);
  assert.equal(events.length, contract.events.length);
  assert.deepEqual(events.map(e => [e.id, e.amount, e.cashDelta]), contract.events.map((e: Record<string, unknown>) => [e.id, e.amount, e.cashDelta]));
  assert.equal(events.reduce((total,e) => total+e.cashDelta,0), 126500); // Two sessions' movements; closing isn't a withdrawal.
});
test("finance boundary rejects forged cash effects, authorization labels, states and identities", () => {
  const reject = (event: unknown) => assert.throws(() => financeEvents([event]), (error: unknown) => error instanceof HttpError && error.status === 400);
  const byKind = (kind: string) => contract.events.find((e: Record<string, unknown>) => e.kind === kind);
  const sale = byKind("sale"); reject({ ...sale, cashDelta: sale.details.tendered }); reject({ ...sale, amount: 22.5 }); reject({ ...sale, id: "anything" });
  reject({ ...sale, details: { ...sale.details, change: 0 } });
  const request = byKind("refund_requested"); reject({ ...request, cashDelta: -request.amount });
  const complete = byKind("refund_completed"); reject({ ...complete, actor: "Kasir" }); reject({ ...complete, details: { ...complete.details, approvedBy: "" } });
  reject({ ...complete, details: { ...complete.details, reference: "" } }); reject({ ...complete, sessionId: null });
  const close = byKind("session_closed"); reject({ ...close, details: { ...close.details, countedCash: 0 } });
  assert.throws(() => financeEvents([])); assert.throws(() => financeEvents(Array(51).fill(sale)));
});

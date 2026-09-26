import { test } from "node:test";
import assert from "node:assert/strict";
import { startHarness } from "./harness.ts";
test("built reconciliation API enforces owner, origin, validation, actor and replay transport", async () => {
 const h = await startHarness();
 const headers = { "Content-Type": "application/json", Origin: h.origin, Cookie: "warung_admin=fixture-admin" };
 const command = { id: "a".repeat(32), revision: 0, action: "actual_cost", transactionId: "QRIS-UJI-001", mdr: 0, other: 0, refunded: 0, reference: "Fixture statement", reason: "Checked report", actor: "forged" };
 const post = (data: unknown, custom = headers) => fetch(h.origin + "/api/admin/finance", { method: "POST", headers: custom, body: JSON.stringify(data) });
 try {
  assert.equal((await fetch(h.origin + "/api/admin/finance?from=2026-09-26&to=2026-09-26")).status, 401);
  assert.equal((await post(command, { ...headers, Cookie: "warung_admin=fixture-other" })).status, 403);
  assert.equal((await post(command, { ...headers, Origin: "https://wrong.test" })).status, 403);
  assert.equal((await post({ ...command, mdr: null })).status, 400);
  assert.equal((await fetch(h.origin + "/api/admin/finance?from=2026-09-01&to=2026-10-02", { headers })).status, 400);
  h.control.dbFail = true; assert.equal((await post(command)).status, 503); assert.equal(h.control.financeAdminWrites.length, 0);
  h.control.dbFail = false; h.control.conflict = true; assert.equal((await post(command)).status, 409);
  h.control.conflict = false; h.control.financeResponseLost = true; assert.equal((await post(command)).status, 503);
  h.control.financeResponseLost = false; const retry = await post(command); assert.equal(retry.status, 200); assert.equal(h.control.financeAdminWrites.length, 1);
  assert.equal(h.control.financeAdminWrites[0].p_actor, "fixture-admin-id"); assert.equal(h.control.financeAdminWrites[0].p_command.actor, undefined);
  const report = await fetch(h.origin + "/api/admin/finance?from=2026-09-26&to=2026-09-26", { headers }); assert.equal(report.status, 200); assert.equal(report.headers.get("cache-control"), "no-store"); assert.equal((await report.json()).summary.actualKnown, 1);
 } finally { await h.stop(); }
});

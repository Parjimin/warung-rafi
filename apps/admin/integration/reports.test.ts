import { test } from "node:test";
import assert from "node:assert/strict";
import { startHarness } from "./harness.ts";
test("built report APIs protect snapshots/CSV, recover creation and verify Sheets before success", async () => {
 const h = await startHarness({ reports: true }); const id = "a".repeat(32);
 const headers = { "Content-Type": "application/json", Origin: h.origin, Cookie: "warung_admin=fixture-admin" };
 const post = (data: unknown, custom = headers) => fetch(h.origin + "/api/admin/reports", { method: "POST", headers: custom, body: JSON.stringify(data) });
 const create = { id, action: "create", filter: { mode: "calendar", from: "2026-09-25", to: "2026-09-25" }, actor: "forged" };
 try {
  assert.equal((await fetch(h.origin + "/api/admin/reports")).status, 401);
  assert.equal((await post(create, { ...headers, Origin: "https://other.test" })).status, 403);
  assert.equal((await post(create, { ...headers, Cookie: "warung_admin=fixture-other" })).status, 403);
  assert.equal((await post({ ...create, filter: { mode: "calendar", from: "2026-09-01", to: "2026-10-02" } })).status, 400);
  h.control.reportLostCreate = true; assert.equal((await post(create)).status, 503); h.control.reportLostCreate = false;
  assert.equal((await post(create)).status, 200); assert.equal(h.control.reportJobs.length, 1); assert.deepEqual(h.control.reportActors, ["fixture-admin-id"]);
  assert.equal((await fetch(h.origin + `/api/admin/reports/${id}?csv=Transaksi`)).status, 401);
  const csv = await fetch(h.origin + `/api/admin/reports/${id}?csv=Transaksi`, { headers }); assert.equal(csv.headers.get("cache-control"), "no-store"); assert.match(csv.headers.get("content-type")!, /text\/csv/); assert.match(await csv.text(), /snapshot_wib/);
  const report = await (await fetch(h.origin + `/api/admin/reports/${id}`, { headers })).json(); assert.equal(report.totals.gross, 46500); assert.equal(report.totals.refunds, 37500);
  assert.equal((await fetch(h.origin + `/api/admin/reports/${id}?csv=secret`, { headers })).status, 404);
  const status = JSON.stringify(await (await fetch(h.origin + "/api/admin/reports", { headers })).json()); assert.ok(!status.includes("private_key") && !status.includes("snapshot"));
  await post({ id, action: "queue" }); h.control.googleLostWrite = true;
  const lost = await (await post({ id, action: "run" })).json(); assert.equal(lost.verified, false); assert.equal(h.control.reportJobs[0].state, "failed"); assert.equal(h.control.reportJobs[0].verified_at, null);
  h.control.googleLostWrite = false; await post({ id, action: "queue" }); const retry = await (await post({ id, action: "run" })).json(); assert.equal(retry.verified, true); assert.equal(h.control.googleSheets.length, 14);
  assert.equal((await fetch(h.origin + "/api/jobs/sheets", { method: "POST", headers: { Authorization: "Bearer fixture-device-token-at-least-32-characters" } })).status, 401);
  await post({ id, action: "queue" }); h.control.googleCorrupt = true;
  const runner = await fetch(h.origin + "/api/jobs/sheets", { method: "POST", headers: { Authorization: "Bearer fixture-runner-token-with-at-least-32-characters" } }); assert.equal(runner.status, 200); assert.equal((await runner.json()).verified, false); assert.equal(h.control.reportJobs[0].error_code, "verification");
 } finally { await h.stop(); }
});

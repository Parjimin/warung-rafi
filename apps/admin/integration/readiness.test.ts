import { test } from "node:test";
import assert from "node:assert/strict";
import { startHarness } from "./harness.ts";

test("readiness authenticates first, distinguishes configuration and never exposes secrets", async t => {
 const revision = "c".repeat(40);
 const app = await startHarness({ payments: true, reports: true, environment: { VERCEL_GIT_COMMIT_SHA: revision } }); t.after(app.stop);
 const url = app.origin + "/api/admin/readiness";
 assert.equal((await fetch(url)).status, 401);
 assert.equal((await fetch(url, { headers: { authorization: "Bearer fixture-device-token-at-least-32-characters" } })).status, 401);
 assert.equal((await fetch(url, { headers: { cookie: "warung_admin=fixture-other" } })).status, 403);
 const headers = { cookie: "warung_admin=fixture-admin" };
 const response = await fetch(url, { headers }); assert.equal(response.status, 200); assert.equal(response.headers.get("cache-control"), "no-store");
 const data = await response.json(); assert.equal(data.revision, revision); assert.equal(data.checks.length, 6);
 assert.equal(data.checks.find((c: any) => c.id === "database").state, "checked");
 for (const id of ["origin", "device", "qris", "sheets", "runner"]) assert.equal(data.checks.find((c: any) => c.id === id).state, "configured");
 const serialized = JSON.stringify(data);
 for (const secret of ["fixture-service", "fixture-device-token", "fixture-midtrans-key", "fixture-merchant", "fixture_workbook", "fixture@", "PRIVATE KEY", "fixture-runner-token"]) assert.ok(!serialized.includes(secret), secret);
 app.control.dbFail = true;
 assert.equal((await fetch(url)).status, 401);
 const unavailable = await (await fetch(url, { headers })).json();
 assert.equal(unavailable.checks.find((c: any) => c.id === "database").state, "missing");
 assert.ok(!JSON.stringify(unavailable).includes("fixture database unavailable"));
});

test("malformed integrations are not reported configured to admin or cashier", async t => {
 const app = await startHarness({ environment: {
  GOOGLE_SHEETS_ID: "invalid", GOOGLE_SERVICE_ACCOUNT_JSON: "{bad-json-secret", MIDTRANS_SERVER_KEY: "secret", MIDTRANS_MERCHANT_ID: "merchant", MIDTRANS_ENV: "invalid",
  EXPORT_RUNNER_TOKEN: "short", VERCEL_GIT_COMMIT_SHA: "unexpected-secret-value"
 } }); t.after(app.stop);
 const data = await (await fetch(app.origin + "/api/admin/readiness", { headers: { cookie: "warung_admin=fixture-admin" } })).json();
 assert.equal(data.revision, null);
 for (const id of ["qris", "sheets", "runner"]) assert.equal(data.checks.find((c: any) => c.id === id).state, "missing");
 const setup = await (await fetch(app.origin + "/api/device/setup", { headers: { authorization: "Bearer fixture-device-token-at-least-32-characters" } })).json();
 assert.equal(setup.qrisConfigured, false); assert.equal(setup.sheetsConfigured, false);
});

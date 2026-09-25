import { test } from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { startHarness } from "./harness.ts";

test("M4 built payment routes with isolated provider and database fixtures", async t => {
  const app = await startHarness({ payments: true }); t.after(app.stop);
  const device = { authorization: "Bearer fixture-device-token-at-least-32-characters" };
  const notice = { order_id: "QRIS-fixture-1", status_code: "200", gross_amount: "22500.00", transaction_id: "fixture-txn-1", signature_key: createHash("sha512").update("QRIS-fixture-120022500.00fixture-midtrans-key").digest("hex") };
  const status = { ...notice, merchant_id: "fixture-merchant", payment_type: "qris", currency: "IDR", transaction_status: "settlement", settlement_time: "2026-09-25 18:44:00" };
  const post = (body: unknown) => fetch(app.origin + "/api/midtrans/notification", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  const inbox = (after = "0", headers: Record<string,string> = device) => fetch(app.origin + "/api/device/payments?after=" + after, { headers });
  await t.test("invalid signature is rejected before contacting provider", async () => {
    assert.equal((await post({ ...notice, signature_key: "0".repeat(128) })).status,401);
    assert.equal(app.control.providerCalls,0); assert.equal(app.control.providerWrites.length,0);
  });
  await t.test("provider identity and outage cannot create a payment record", async () => {
    for (const patch of [{ merchant_id: "another" }, { transaction_id: "another" }, { payment_type: "bank_transfer" }, { gross_amount: "23000.00" }]) {
      app.control.providerStatus = { ...status, ...patch };
      assert.equal((await post(notice)).status,422);
    }
    app.control.providerStatus = status; app.control.providerFail = true;
    assert.equal((await post(notice)).status,503); app.control.providerFail = false;
    assert.equal(app.control.providerWrites.length,0);
  });
  await t.test("unsigned settlement in webhook cannot override provider pending", async () => {
    app.control.providerStatus = { ...status, transaction_status: "pending" };
    assert.equal((await post({ ...notice, transaction_status: "settlement" })).status,200);
    assert.equal(app.control.providerWrites.at(-1)?.status,"pending");
    assert.deepEqual((await (await inbox()).json()).payments,[]);
  });
  await t.test("database failure is not acknowledged and retry exposes a single durable notification", async () => {
    app.control.providerStatus = status; app.control.dbFail = true;
    assert.equal((await post(notice)).status,503); assert.equal(app.control.paymentRows.length,0);
    app.control.dbFail = false;
    assert.equal((await post(notice)).status,200); assert.equal((await post(notice)).status,200);
    const result = await (await inbox()).json(); assert.equal(result.payments.length,1);
    assert.deepEqual(result.payments[0], { sequence: 1, transactionId: "fixture-txn-1", amount: 22500, paidAt: "2026-09-25T11:44:00.000Z" });
  });
  await t.test("equal amounts remain distinct and polling resumes from cursor", async () => {
    app.control.providerStatus = { ...status, transaction_id: "fixture-txn-2" };
    assert.equal((await post({ ...notice, transaction_id: "fixture-txn-2" })).status,200);
    const all = await (await inbox()).json(); assert.equal(all.payments.length,2);
    const next = await (await inbox("1")).json(); assert.equal(next.payments.length,1); assert.equal(next.payments[0].transactionId,"fixture-txn-2");
    assert.deepEqual((await (await inbox("2")).json()).payments,[]);
  });
  await t.test("inbox requires device authentication and valid cursor", async () => {
    assert.equal((await inbox("0",{})).status,401);
    for (const cursor of ["-1","NaN","1.5","1000000000000000"]) assert.equal((await inbox(cursor)).status,400);
    app.control.dbFail = true; assert.equal((await inbox()).status,503); app.control.dbFail = false;
  });
});

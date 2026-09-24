import { test } from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { authoritativePayment, rupiah, verifySignature, verifiedNotification } from "../lib/midtrans.ts";
const key = "test-server-key";
const notice = { order_id: "static-order", status_code: "200", gross_amount: "22500.00", transaction_id: "txn-1", signature_key: createHash("sha512").update("static-order20022500.00" + key).digest("hex") };
const status = { ...notice, merchant_id: "merchant-1", transaction_status: "settlement", payment_type: "qris", currency: "IDR", settlement_time: "2026-09-22 18:44:00", fraud_status: "accept" };
test("signature checks exact signed amount and key", () => {
  assert.equal(verifySignature(notice,key).transaction_id,"txn-1");
  assert.throws(() => verifySignature({...notice,gross_amount:"25000.00"},key));
  assert.throws(() => verifySignature(notice,"wrong-key"));
  assert.throws(() => verifySignature({...notice,signature_key:"x"},key));
});
test("integer rupiah rejects fractions, NaN, negative and scientific notation", () => {
  assert.equal(rupiah("22500.00"),22500);
  for (const value of ["22500.01","-10","NaN","1e5",0,"0"]) assert.throws(() => rupiah(value));
});
test("provider status binds unsigned identity fields and merchant", () => {
  const payment = authoritativePayment(notice,status,"merchant-1");
  assert.equal(payment.paidAt,"2026-09-22T11:44:00.000Z");
  for (const patch of [{merchant_id:"another"},{currency:"USD"},{transaction_id:"another"},{gross_amount:"10.00"},{payment_type:"bank_transfer"},{fraud_status:"challenge"}]) assert.throws(() => authoritativePayment(notice,{...status,...patch},"merchant-1"));
});
test("a forged settlement field cannot bypass authoritative pending status", async () => {
  const proof = await verifiedNotification({...notice,transaction_status:"settlement"},{key,merchantId:"merchant-1",environment:"sandbox"},async () => Response.json({...status,transaction_status:"pending"}));
  assert.equal(proof.status,"pending");
});
test("provider outage fails rather than acknowledging the webhook", async () => {
  await assert.rejects(verifiedNotification(notice,{key,merchantId:"merchant-1",environment:"sandbox"},async () => new Response("",{status:503})));
});

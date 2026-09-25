import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { startHarness } from "./harness.ts";
const contract = JSON.parse(readFileSync(new URL("../../../database/tests/fixtures/finance-contract.json", import.meta.url), "utf8"));
test("built finance API authenticates, validates and acknowledges only committed journal batches", async () => {
  const h = await startHarness();
  const post = (events: unknown, authorized = true) => fetch(h.origin + "/api/device/finance", { method: "POST", headers: { "Content-Type": "application/json", ...(authorized ? { Authorization: "Bearer fixture-device-token-at-least-32-characters" } : {}) }, body: JSON.stringify({ events }) });
  try {
    assert.equal((await post(contract.events, false)).status, 401);
    assert.equal((await post([{ ...contract.events[0], cashDelta: -1 }])).status, 400);
    h.control.dbFail = true; assert.equal((await post(contract.events)).status, 503); assert.equal(h.control.financeWrites.length, 0);
    h.control.dbFail = false; h.control.conflict = true; assert.equal((await post(contract.events)).status, 409);
    h.control.conflict = false; const response = await post(contract.events); assert.equal(response.status, 200);
    assert.deepEqual((await response.json()).accepted, contract.events.map((e: { id: string }) => e.id));
    assert.equal(h.control.financeWrites.length, 1); assert.equal(response.headers.get("cache-control"), "no-store");
  } finally { await h.stop(); }
});

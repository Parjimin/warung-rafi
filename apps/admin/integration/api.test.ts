import { test } from "node:test";
import assert from "node:assert/strict";
import sharp from "sharp";
import { startHarness } from "./harness.ts";

test("M3 routes through the built Next server with an isolated Supabase fixture", async t => {
  const app = await startHarness(); t.after(app.stop);
  const owner = { cookie: "warung_admin=fixture-admin", origin: app.origin };
  const device = { authorization: "Bearer fixture-device-token-at-least-32-characters" };
  const post = (path: string, data: unknown, headers: Record<string,string> = owner) => fetch(app.origin + path, { method: "POST", headers: { ...headers, "Content-Type": "application/json" }, body: JSON.stringify(data) });
  await t.test("admin reads and changes require the allowed account and same origin", async () => {
    assert.equal((await fetch(app.origin + "/api/admin/catalog")).status,401);
    assert.equal((await fetch(app.origin + "/api/admin/devices", { headers: { cookie: "warung_admin=fixture-other" } })).status,403);
    assert.equal((await post("/api/admin/catalog", { action: "publish", version: 1 }, { ...owner, origin: "https://wrong.invalid" })).status,403);
    assert.equal((await post("/api/auth/login", { email: "other@fixture.test", password: "fixture" })).status,403);
    const login = await post("/api/auth/login", { email: "owner@fixture.test", password: "fixture" });
    assert.equal(login.status,200); assert.match(login.headers.get("set-cookie")!, /HttpOnly/i);
  });
  await t.test("upload requires auth, validates file, then reports storage failure truthfully", async () => {
    const image = await sharp({ create: { width: 30, height: 30, channels: 3, background: "#214c3e" } }).png().toBuffer();
    const upload = (headers: Record<string,string>, data = image) => fetch(app.origin + "/api/admin/photos", { method: "POST", headers: { "Content-Type": "image/png", ...headers }, body: new Uint8Array(data) });
    assert.equal((await upload({ origin: app.origin })).status,401);
    assert.equal((await upload({ ...owner, cookie: "warung_admin=fixture-other" })).status,403);
    assert.equal((await upload({ ...owner, origin: "https://wrong.invalid" })).status,403);
    assert.equal((await upload(owner, Buffer.from("not an image"))).status,400);
    const result = await upload(owner); assert.equal(result.status,201); assert.match((await result.json()).imageUrl,/\.jpg$/);
    assert.equal((await sharp(app.control.uploaded).metadata()).format,"jpeg");
    app.control.storageFail = true; assert.equal((await upload(owner)).status,503); app.control.storageFail = false;
  });
  await t.test("save rejects stale versions and publication reaches device catalog", async () => {
    const products = [...app.state.draft, { id: "new", name: "Sate Telur", category: "Sundukan", price: 4000, available: true, imageUrl: null }];
    assert.equal((await post("/api/admin/catalog", { action: "save", version: 1, products })).status,200);
    assert.equal((await post("/api/admin/catalog", { action: "save", version: 1, products: [] })).status,409);
    assert.equal(app.state.draft.length,5);
    assert.equal((await post("/api/admin/catalog", { action: "publish", version: 2 })).status,200);
    const response = await fetch(app.origin + "/api/device/catalog", { headers: device });
    const catalog = await response.json(); assert.equal(catalog.version,2); assert.equal(catalog.products.length,5);
    assert.equal((await fetch(app.origin + "/api/device/catalog")).status,401);
  });
  await t.test("heartbeat exposes observed backlog only through protected admin API", async () => {
    assert.equal((await post("/api/device/status", { pendingCount: 4, catalogVersion: 1 }, device)).status,200);
    assert.equal((await post("/api/device/status", { pendingCount: -1, catalogVersion: 1 }, device)).status,400);
    const response = await fetch(app.origin + "/api/admin/devices", { headers: owner });
    const rows = await response.json(); assert.equal(rows[0].pending_count,4); assert.equal(rows[0].catalog_version,1);
    assert.equal((await fetch(app.origin + "/api/admin/devices", { headers: device })).status,401);
  });
  await t.test("sync does not acknowledge conflicts or database failure", async () => {
    const id = "0123456789abcdef0123456789abcdef";
    const event = { id: id+":1", aggregateId: id, version: 1, payload: { order: { id, version: 1, status: 0, number: "WR-1", customerLabel: "", createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(), total: 0, lines: [] } } };
    const success = await post("/api/device/sync", { events: [event] }, device); assert.deepEqual((await success.json()).accepted,[event.id]);
    app.control.conflict = true;
    const conflict = await post("/api/device/sync", { events: [event] }, device); assert.equal(conflict.status,409); assert.equal((await conflict.json()).accepted,undefined);
    app.control.conflict = false; app.control.dbFail = true;
    assert.equal((await post("/api/device/sync", { events: [event] }, device)).status,503); app.control.dbFail = false;
  });
});

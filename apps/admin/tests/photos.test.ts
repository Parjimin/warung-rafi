import { test } from "node:test";
import assert from "node:assert/strict";
import sharp from "sharp";
import { preparePhoto, PHOTO_MAX_BYTES, uploadPhoto } from "../lib/photos.ts";
import { deviceReport } from "../lib/device-status.ts";
const request = (body: Buffer, type = "image/png") => new Request("http://fixture", { method: "POST", headers: { "Content-Type": type }, body: new Uint8Array(body) });
test("photo is decoded, resized and normalized to JPEG without original metadata", async () => {
  const png = await sharp({ create: { width: 1600, height: 800, channels: 3, background: "#24513e" } }).png().withMetadata().toBuffer();
  const image = await sharp(await preparePhoto(request(png))).metadata();
  assert.equal(image.format, "jpeg"); assert.equal(image.width, 1200); assert.equal(image.height, 600); assert.equal(image.exif, undefined);
});
test("photo rejects disguised, damaged, oversized and unsupported input", async () => {
  const png = await sharp({ create: { width: 2, height: 2, channels: 3, background: "white" } }).png().toBuffer();
  await assert.rejects(preparePhoto(request(png, "image/jpeg")), { status: 400 });
  await assert.rejects(preparePhoto(request(png.subarray(0, 30))), { status: 400 });
  await assert.rejects(preparePhoto(request(Buffer.from("<svg></svg>"), "image/svg+xml")), { status: 415 });
  await assert.rejects(preparePhoto(request(Buffer.alloc(PHOTO_MAX_BYTES + 1))), { status: 413 });
  const big = await sharp({ create: { width: 6000, height: 5000, channels: 3, background: "white" } }).png().toBuffer();
  await assert.rejects(preparePhoto(request(big)), { status: 400 });
});
test("storage uses a fresh path and does not report success after upload failure", async t => {
  process.env.SUPABASE_URL = "https://fixture.invalid"; process.env.SUPABASE_SERVICE_ROLE_KEY = "test-only-key";
  const paths: string[] = [];
  t.mock.method(globalThis, "fetch", async (url: string, init: RequestInit) => { paths.push(url); assert.equal((init.headers as Record<string,string>)["x-upsert"], "false"); return new Response("{}", { status: 200 }); });
  const a = await uploadPhoto(Buffer.from("fixture")); const b = await uploadPhoto(Buffer.from("fixture"));
  assert.notEqual(a,b); assert.equal(paths.length,2); assert.match(a,/\/object\/public\/menu-photos\/menu\/.+\.jpg$/);
  t.mock.method(globalThis, "fetch", async () => new Response("provider detail", { status: 503 }));
  await assert.rejects(uploadPhoto(Buffer.from("fixture")), { status: 503 });
});
test("device reports reject invalid counts and versions", () => {
  assert.deepEqual(deviceReport({ pendingCount: 10, catalogVersion: 3 }), { pendingCount: 10, catalogVersion: 3 });
  for (const report of [{ pendingCount: -1, catalogVersion: 1 }, { pendingCount: 1.2, catalogVersion: 0 }, { pendingCount: 0, catalogVersion: "3" }]) assert.throws(() => deviceReport(report));
});

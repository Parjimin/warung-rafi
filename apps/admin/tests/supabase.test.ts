import { test } from "node:test";
import assert from "node:assert/strict";
import { database } from "../lib/db.ts";
import { uploadPhoto } from "../lib/photos.ts";
import { serviceHeaders } from "../lib/supabase.ts";

test("REST and Storage preserve legacy JWT auth and use only apikey for opaque secret keys", async t => {
  const saved = { url: process.env.SUPABASE_URL, key: process.env.SUPABASE_SERVICE_ROLE_KEY };
  t.after(() => {
    for (const [name, value] of [["SUPABASE_URL", saved.url], ["SUPABASE_SERVICE_ROLE_KEY", saved.key]]) {
      if (value === undefined) delete process.env[name!]; else process.env[name!] = value;
    }
  });
  process.env.SUPABASE_URL = "https://fixture.invalid";
  let calls = 0;
  t.mock.method(globalThis, "fetch", async (_url: string, init: RequestInit) => {
    const headers = new Headers(init.headers), key = process.env.SUPABASE_SERVICE_ROLE_KEY!;
    assert.equal(headers.get("apikey"), key);
    assert.equal(headers.get("authorization"), key.startsWith("sb_secret_") ? null : `Bearer ${key}`);
    assert.equal(init.redirect, "error"); calls++;
    return Response.json([]);
  });
  for (const key of ["fixture.legacy.jwt", "sb_secret_fixture_only"]) {
    process.env.SUPABASE_SERVICE_ROLE_KEY = key;
    await database("catalog_state?select=id");
    await uploadPhoto(Buffer.from("fixture"));
  }
  assert.equal(calls, 4);
  process.env.SUPABASE_SERVICE_ROLE_KEY = "sb_publishable_fixture_only";
  assert.throws(serviceHeaders, { status: 503 });
});

import { test } from "node:test";
import assert from "node:assert/strict";
import { startHarness } from "./harness.ts";

test("M6 protected operations through the built server", async t => {
 const app=await startHarness();t.after(app.stop);
 const device={authorization:"Bearer fixture-device-token-at-least-32-characters"};
 const login=(email="owner@fixture.test",origin=app.origin)=>fetch(app.origin+"/api/auth/login",{method:"POST",headers:{origin,"Content-Type":"application/json"},body:JSON.stringify({email,password:"fixture"})});
 await t.test("login limits persist outside the app process and fail closed",async()=>{
  assert.equal((await login()).status,200);const key=app.control.loginKeys.at(-1)!;assert.match(key,/^[a-f0-9]{64}$/);assert.ok(!key.includes("owner"));
  await login(" OWNER@FIXTURE.TEST ");assert.equal(app.control.loginKeys.at(-1),key);
  const count=app.control.loginKeys.length;assert.equal((await login("owner@fixture.test","https://wrong.invalid")).status,403);assert.equal(app.control.loginKeys.length,count);
  app.control.loginAllowed=false;const blocked=await login();assert.equal(blocked.status,429);assert.equal(blocked.headers.get("set-cookie"),null);app.control.loginAllowed=true;
  app.control.dbFail=true;assert.equal((await login()).status,503);app.control.dbFail=false;
 });
 await t.test("setup and recovery require device credentials and valid cursors",async()=>{
  for(const path of ["/api/device/setup","/api/device/recovery"]){assert.equal((await fetch(app.origin+path)).status,401);assert.equal((await fetch(app.origin+path,{headers:{cookie:"warung_admin=fixture-admin"}})).status,401);}
  const setup=await (await fetch(app.origin+"/api/device/setup",{headers:device})).json();assert.equal(setup.schema,6);assert.equal(setup.deviceId,"kasir-utama");assert.equal(setup.qrisConfigured,false);assert.equal(setup.sheetsConfigured,false);assert.ok(!JSON.stringify(setup).includes("fixture-service"));
  assert.equal((await fetch(app.origin+"/api/device/recovery?after=bad",{headers:device})).status,400);
  assert.equal((await fetch(app.origin+"/api/device/recovery?state="+"b".repeat(32),{headers:device})).status,409);
  const page=await (await fetch(app.origin+"/api/device/recovery",{headers:device})).json();assert.equal(page.state,app.control.recoveryState);assert.deepEqual(page.orders,[]);
  app.control.dbFail=true;assert.equal((await fetch(app.origin+"/api/device/recovery",{headers:device})).status,503);app.control.dbFail=false;
 });
 await t.test("previous device token is accepted only during rotation window",async()=>{
  assert.equal((await fetch(app.origin+"/api/device/setup",{headers:{authorization:"Bearer fixture-previous-token-at-least-32-characters"}})).status,200);
  assert.equal((await fetch(app.origin+"/api/device/setup",{headers:{authorization:"Bearer wrong-token"}})).status,401);
 });
});
test("expired previous token cannot authenticate",async t=>{
 const app=await startHarness({previousUntil:new Date(Date.now()-60000).toISOString()});t.after(app.stop);
 assert.equal((await fetch(app.origin+"/api/device/setup",{headers:{authorization:"Bearer fixture-previous-token-at-least-32-characters"}})).status,401);
 assert.equal((await fetch(app.origin+"/api/device/setup",{headers:{authorization:"Bearer fixture-device-token-at-least-32-characters"}})).status,200);
});

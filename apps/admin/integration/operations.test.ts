import { test } from "node:test";
import assert from "node:assert/strict";
import { startHarness } from "./harness.ts";
import sharp from "sharp";

test("modern Supabase keys support built login, database and Storage routes", async t => {
 const app=await startHarness({modernKeys:true});t.after(app.stop);
 const login=await fetch(app.origin+"/api/auth/login",{method:"POST",headers:{origin:app.origin,"Content-Type":"application/json"},body:JSON.stringify({email:"owner@fixture.test",password:"fixture"})});
 assert.equal(login.status,200);
 const headers={origin:app.origin,cookie:"warung_admin=fixture-admin"};
 assert.equal((await fetch(app.origin+"/api/admin/catalog",{headers})).status,200);
 assert.equal((await fetch(app.origin+"/api/device/setup",{headers:{authorization:"Bearer fixture-device-token-at-least-32-characters"}})).status,200);
 const photo=await sharp({create:{width:2,height:2,channels:3,background:"white"}}).png().toBuffer();
 const upload=await fetch(app.origin+"/api/admin/photos",{method:"POST",headers:{...headers,"Content-Type":"image/png"},body:photo});
 assert.equal(upload.status,201);assert.ok(app.control.uploaded.length>0);
});

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

test("simple cashier activation and live export require appropriate credentials",async t=>{
 const app=await startHarness();t.after(app.stop);
 const activate=(email="owner@fixture.test")=>fetch(app.origin+"/api/device/activate",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({email,password:"fixture"})});
 assert.equal((await activate("other@fixture.test")).status,403);
 const response=await activate();assert.equal(response.status,200);assert.equal(response.headers.get("cache-control"),"no-store");
 const credentials=await response.json();assert.deepEqual(Object.keys(credentials).sort(),["deviceId","deviceToken"]);assert.equal(credentials.deviceId,"kasir-utama");
 app.control.loginAllowed=false;assert.equal((await activate()).status,429);app.control.loginAllowed=true;
 assert.equal((await fetch(app.origin+"/api/device/sheets",{method:"POST"})).status,401);
 const result=await fetch(app.origin+"/api/device/sheets",{method:"POST",headers:{authorization:"Bearer "+credentials.deviceToken}});
 assert.equal(result.status,200);assert.equal((await result.json()).state,"unavailable");
});

test("device automatically captures, exports and skips unchanged reports without creating manual jobs",async t=>{
 const app=await startHarness({reports:true});t.after(app.stop);
 const send=()=>fetch(app.origin+"/api/device/sheets",{method:"POST",headers:{authorization:"Bearer fixture-device-token-at-least-32-characters"}});
 const first=await send();assert.equal(first.status,200);assert.equal((await first.json()).state,"verified");
 assert.equal(app.control.googleSheets.length,2);assert.equal(app.control.reportJobs.length,0);
 const writes=app.control.googleWrites;
 const second=await send();assert.equal((await second.json()).state,"unchanged");assert.equal(app.control.googleWrites,writes);assert.equal(app.control.googleSheets.length,2);
});

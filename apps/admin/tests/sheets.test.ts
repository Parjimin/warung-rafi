import { test } from "node:test";
import assert from "node:assert/strict";
import { generateKeyPairSync, verify } from "node:crypto";
import { readFileSync } from "node:fs";
import { liveHash, liveReport } from "../lib/live-sheets.ts";
import { buildReport } from "../lib/reports.ts";
import { exportSheets, sheetPlans, signedAssertion, reportHash, writeBatches, ExportError } from "../lib/sheets.ts";
const keys = generateKeyPairSync("rsa", { modulusLength: 2048 });
const config = { target: "fixture_workbook_1234567890", email: "fixture@fixture.iam.gserviceaccount.com", privateKey: keys.privateKey.export({ type: "pkcs8", format: "pem" }).toString() };
const source = JSON.parse(readFileSync(new URL("../../../database/tests/fixtures/report-contract.json", import.meta.url), "utf8"));
const report = buildReport("b".repeat(32), source);
function googleFixture() {
 const sheets: any[] = []; const control = { lostWrite: false, lostCreate: false, corrupt: false, status: 200, writes: 0, requests: [] as unknown[] };
 const fetcher: typeof fetch = async (url, init) => {
  const u = String(url); if (u === "https://oauth2.googleapis.com/token") return Response.json({ access_token: "fixture-token" });
  assert.ok(u.startsWith("https://sheets.googleapis.com/v4/spreadsheets/" + config.target)); assert.equal((init!.headers as Record<string, string>).Authorization, "Bearer fixture-token");
  if (control.status !== 200) return Response.json({ error: "sensitive fixture detail" }, { status: control.status });
  if (init?.method === "POST") {
   const body = JSON.parse(String(init.body)); control.requests.push(body); let created = false, wrote = false;
   for (const r of body.requests) {
    if (r.addSheet) { assert.ok(!sheets.some(s => s.properties.sheetId === r.addSheet.properties.sheetId)); sheets.push({ properties: r.addSheet.properties, data: [{ rowData: [] }] }); created = true; }
    if (r.updateSheetProperties) { const v=r.updateSheetProperties.properties, s=sheets.find(s=>s.properties.sheetId===v.sheetId); s.properties.gridProperties={...s.properties.gridProperties,...v.gridProperties}; s.data[0].rowData=s.data[0].rowData.slice(0,v.gridProperties.rowCount); }
    if (r.updateCells) { const v = r.updateCells, s = sheets.find(s => s.properties.sheetId === v.range.sheetId); v.rows.forEach((row: unknown, i: number) => s.data[0].rowData[v.range.startRowIndex + i] = row); control.writes++; wrote = true; }
   }
   if ((control.lostCreate && created) || (control.lostWrite && wrote)) throw new Error("response lost after commit"); return Response.json({});
  }
  const response = structuredClone(sheets);
  if (u.includes("includeGridData") && control.corrupt) response[0].data[0].rowData[1].values[0] = { userEnteredValue: { formulaValue: "=1" } };
  return Response.json({ sheets: response });
 };
 return { sheets, control, fetcher };
}
test("service-account JWT uses signed RS256, fixed audience and least Sheets scope", () => {
 const jwt = signedAssertion(config, 1700000000), [header, payload, signature] = jwt.split(".");
 assert.equal(JSON.parse(Buffer.from(header, "base64url").toString()).alg, "RS256");
 const claims = JSON.parse(Buffer.from(payload, "base64url").toString()); assert.equal(claims.aud, "https://oauth2.googleapis.com/token"); assert.equal(claims.exp - claims.iat, 3600); assert.equal(claims.scope, "https://www.googleapis.com/auth/spreadsheets"); assert.equal(claims.sub, undefined);
 assert.ok(verify("RSA-SHA256", Buffer.from(header + "." + payload), keys.publicKey, Buffer.from(signature, "base64url")));
});
test("lost creation and write responses recover with identical tabs and no append", async () => {
 const f = googleFixture(); f.control.lostCreate = true;
 await assert.rejects(exportSheets(report, config.target, config, f.fetcher), (e: ExportError) => e.code === "network"); assert.equal(f.sheets.length, 14);
 f.control.lostCreate = false; f.control.lostWrite = true; await assert.rejects(exportSheets(report, config.target, config, f.fetcher));
 f.control.lostWrite = false; const hash = await exportSheets(report, config.target, config, f.fetcher); assert.equal(hash, reportHash(report)); assert.equal(f.sheets.length, 14);
 assert.equal(JSON.stringify(f.control.requests).includes("appendCells"), false); assert.equal(JSON.stringify(f.control.requests).includes('"formulaValue"'), false);
 const second = { ...report, id: "c".repeat(32) }; assert.equal(sheetPlans(report).some(a => sheetPlans(second).some(b => a.id === b.id || a.title === b.title)), false);
});
test("read-back mismatch, permission/quota and renamed tabs are not acknowledged", async () => {
 const f = googleFixture(); f.control.corrupt = true; await assert.rejects(exportSheets(report, config.target, config, f.fetcher), (e: ExportError) => e.code === "verification" && e.retry);
 f.control.corrupt = false; f.sheets[0].properties.title = "Owner renamed"; await assert.rejects(exportSheets(report, config.target, config, f.fetcher), (e: ExportError) => e.code === "target_changed");
 for (const [status, code, retry] of [[403, "permission", false], [429, "quota", true], [503, "service", true]] as const) { const fail = googleFixture(); fail.control.status = status; await assert.rejects(exportSheets(report, config.target, config, fail.fetcher), (e: ExportError) => e.code === code && e.retry === retry && !e.message.includes("sensitive")); }
});
test("typed strings never become formulas and requests stay within batch budget", async () => {
 const clone = structuredClone(report); clone.tables[0].rows[0][1] = "=SUM(A1:A9)"; const batches = writeBatches(sheetPlans(clone), 100000);
 for (const b of batches) assert.ok(Buffer.byteLength(JSON.stringify({ requests: b })) <= 100000);
 const serialized = JSON.stringify(batches); assert.ok(serialized.includes('"stringValue":"=SUM(A1:A9)"'));
 const f = googleFixture(); assert.equal(await exportSheets(clone, config.target, config, f.fetcher), reportHash(clone));
});

test("live export grows and shrinks the same 14 tabs without touching manual snapshots", async () => {
 const f=googleFixture(); await exportSheets(report,config.target,config,f.fetcher);
 const manual=structuredClone(f.sheets);
 const first=liveReport(source);await exportSheets(first,config.target,config,f.fetcher,true);
 assert.equal(f.sheets.length,28);
 const next=structuredClone(first);next.tables[0].rows.push([...next.tables[0].rows[0]]);
 await exportSheets(next,config.target,config,f.fetcher,true);assert.equal(f.sheets.length,28);
 const small=structuredClone(first);for(const t of small.tables)t.rows=[];
 f.control.lostWrite=true;await assert.rejects(exportSheets(small,config.target,config,f.fetcher,true));
 f.control.lostWrite=false;await exportSheets(small,config.target,config,f.fetcher,true);
 assert.equal(f.sheets.length,28);assert.deepEqual(f.sheets.slice(0,14),manual);
 for(const s of f.sheets.slice(14)){assert.equal(s.properties.gridProperties.rowCount,2);assert.ok(s.properties.title.startsWith("WR_live_"));assert.ok(s.data[0].rowData[1].values.every((v:any)=>!v.userEnteredValue));}
});
test("live change detection ignores capture time but includes completeness and source changes",()=>{
 const first=liveReport(source),next=structuredClone(source);next.capturedAt=new Date(Date.parse(source.capturedAt)+1000).toISOString();
 assert.equal(liveHash(first),liveHash(liveReport(next)));
 const changed=structuredClone(first);changed.tables[0].rows[0][1]="different";assert.notEqual(liveHash(first),liveHash(changed));
});

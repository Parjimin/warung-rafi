import { createHash, createPrivateKey, sign } from "node:crypto";
import type { Cell, Report, ReportTable } from "./reports.ts";
export class ExportError extends Error {
 code: string; retry: boolean;
 constructor(code: string, retry = false) { super(code); this.code = code; this.retry = retry; }
}
const TOKEN_URL = "https://oauth2.googleapis.com/token";
const API = "https://sheets.googleapis.com/v4/spreadsheets/";
const SCOPE = "https://www.googleapis.com/auth/spreadsheets";
export type SheetsConfig = { target: string; email: string; privateKey: string };
export function sheetsConfig(): SheetsConfig {
 try {
  const c = JSON.parse(process.env.GOOGLE_SERVICE_ACCOUNT_JSON ?? "{}"); const target = process.env.GOOGLE_SHEETS_ID ?? "";
  if (c.type !== "service_account" || typeof c.client_email !== "string" || !/^[a-z0-9._-]+@[a-z0-9.-]+\.gserviceaccount\.com$/i.test(c.client_email) || typeof c.private_key !== "string" || !/^[A-Za-z0-9_-]{20,150}$/.test(target)) throw new Error();
  if (createPrivateKey(c.private_key).asymmetricKeyType !== "rsa") throw new Error();
  return { target, email: c.client_email, privateKey: c.private_key };
 } catch { throw new ExportError("configuration"); }
}
export function signedAssertion(config: SheetsConfig, now = Math.floor(Date.now() / 1000)) {
 const encode = (v: unknown) => Buffer.from(JSON.stringify(v)).toString("base64url");
 const message = `${encode({ alg: "RS256", typ: "JWT" })}.${encode({ iss: config.email, scope: SCOPE, aud: TOKEN_URL, iat: now, exp: now + 3600 })}`;
 return `${message}.${sign("RSA-SHA256", Buffer.from(message), config.privateKey).toString("base64url")}`;
}
export type SheetPlan = { id: number; title: string; table: ReportTable };
export function sheetPlans(report: Report): SheetPlan[] {
 const result = report.tables.map(table => ({ id: createHash("sha256").update(report.id + "/" + table.name).digest().readUInt32BE(0) & 0x7fffffff, title: report.id === "live" ? table.name : `WR_${report.id}_${table.name}`, table }));
 if (new Set(result.map(p => p.id)).size !== result.length) throw new ExportError("target_changed"); return result;
}
const cell = (v: Cell) => v == null || v === "" ? {} : { userEnteredValue: typeof v === "number" ? { numberValue: v } : { stringValue: v } };
export function writeBatches(plans: SheetPlan[], maxBytes = 1500000): object[][] {
 const batches: object[][] = []; let current: object[] = []; let bytes = 20;
 for (const p of plans) {
  const data = [p.table.columns, ...p.table.rows];
  if (data.length === 1) data.push(p.table.columns.map(() => null));
  for (let i = 0; i < data.length; i += 150) {
   const request = { updateCells: { range: { sheetId: p.id, startRowIndex: i, endRowIndex: Math.min(i + 150, data.length), startColumnIndex: 0, endColumnIndex: p.table.columns.length }, rows: data.slice(i, i + 150).map(row => ({ values: row.map(cell) })), fields: "userEnteredValue" } };
   const size = Buffer.byteLength(JSON.stringify(request)) + 1;
   if (size > maxBytes) throw new ExportError("capacity");
   if (current.length && bytes + size > maxBytes) { batches.push(current); current = []; bytes = 20; }
   current.push(request); bytes += size;
  }
 }
 if (current.length) batches.push(current); return batches;
}
export function reportHash(report: Report) { return createHash("sha256").update(JSON.stringify(report.tables)).digest("hex"); }
type GridValue = { stringValue?: string; numberValue?: number; formulaValue?: string; boolValue?: boolean };
type SheetResponse = { sheets?: { properties: { sheetId: number; title: string; gridProperties: { rowCount: number; columnCount: number } }; data?: { startRow?: number; startColumn?: number; rowData?: { values?: { userEnteredValue?: GridValue }[] }[] }[] }[] };
function existingPlans(plans: SheetPlan[], response: SheetResponse, mutable = false): SheetPlan[] {
 return plans.filter(p => {
  const byId = response.sheets?.find(s => s.properties.sheetId === p.id), byTitle = response.sheets?.find(s => s.properties.title === p.title);
  if (!byId && !byTitle) return false;
  if (!byId || !byTitle || byId !== byTitle || (!mutable && (byId.properties.gridProperties.rowCount !== Math.max(2, p.table.rows.length + 1) || byId.properties.gridProperties.columnCount !== p.table.columns.length))) throw new ExportError("target_changed");
  return true;
 });
}
export function verifySheets(plans: SheetPlan[], response: SheetResponse): boolean {
 if (existingPlans(plans, response).length !== plans.length) return false;
 return plans.every(p => {
  const sheet = response.sheets!.find(s => s.properties.sheetId === p.id)!; const data = sheet.data ?? [];
  if (data.length !== 1 || (data[0].startRow ?? 0) !== 0 || (data[0].startColumn ?? 0) !== 0) return false;
  const actual = data[0].rowData ?? []; const expected = [p.table.columns, ...p.table.rows];
  const rowCount = Math.max(2, expected.length);
  if (actual.length > rowCount || actual.some(row => (row.values?.length ?? 0) > p.table.columns.length)) return false;
  return Array.from({ length: rowCount }, (_, ri) => p.table.columns.every((_, ci) => {
   const v = expected[ri]?.[ci] ?? null;
   const got = actual[ri]?.values?.[ci]?.userEnteredValue ?? {};
   if (got.formulaValue !== undefined || got.boolValue !== undefined) return false;
   const value = got.numberValue ?? got.stringValue ?? null;
   return (v === "" ? null : v) === (value === "" ? null : value);
  })).every(Boolean);
 });
}
export async function exportSheets(report: Report, target: string, config = sheetsConfig(), request: typeof fetch = fetch, live = false): Promise<string> {
 if (config.target !== target) throw new ExportError("target_changed");
 const overall = AbortSignal.timeout(35000);
 async function call(url: string, init: RequestInit = {}) {
  let response: Response;
  try { response = await request(url, { ...init, cache: "no-store", redirect: "error", signal: AbortSignal.any([overall, AbortSignal.timeout(12000)]) }); }
  catch { throw new ExportError("network", true); }
  if (!response.ok) {
   if ([401, 403].includes(response.status)) throw new ExportError("permission");
   if (response.status === 429) throw new ExportError("quota", true);
   if (response.status >= 500) throw new ExportError("service", true);
   throw new ExportError("target_changed");
  }
  try { return await response.json(); } catch { throw new ExportError("service", true); }
 }
 const token = await call(TOKEN_URL, { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: new URLSearchParams({ grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer", assertion: signedAssertion(config) }) });
 if (typeof token.access_token !== "string" || token.access_token.length < 1) throw new ExportError("permission");
 const headers = { Authorization: `Bearer ${token.access_token}`, "Content-Type": "application/json" }; const base = API + target;
 const plans = sheetPlans(live ? { ...report, id: "live" } : report);
 const metadata: SheetResponse = await call(base + "?fields=sheets(properties(sheetId,title,gridProperties))", { headers });
 const existing = new Set(existingPlans(plans, metadata, live).map(p => p.id));
 const add = plans.filter(p => !existing.has(p.id)).flatMap(p => [
  { addSheet: { properties: { sheetId: p.id, title: p.title, gridProperties: { rowCount: Math.max(2, p.table.rows.length + 1), columnCount: p.table.columns.length, frozenRowCount: 1 } } } },
  { addProtectedRange: { protectedRange: { range: { sheetId: p.id }, description: "Salinan laporan Warung Rafi; analisis bebas gunakan tab lain.", warningOnly: false, editors: { users: [config.email] } } } },
  { repeatCell: { range: { sheetId: p.id, startRowIndex: 0, endRowIndex: 1 }, cell: { userEnteredFormat: { backgroundColor: { red: 0.13, green: 0.3, blue: 0.24 }, textFormat: { bold: true, foregroundColor: { red: 1, green: 1, blue: 1 } } } }, fields: "userEnteredFormat" } }
 ]);
 if (add.length) await call(base + ":batchUpdate", { method: "POST", headers, body: JSON.stringify({ requests: add }) });
 if (live && existing.size) {
  // Resize to the exact new bounds: shrinking removes stale rows on month rollover.
  const requests = plans.filter(p => existing.has(p.id)).map(p => ({ updateSheetProperties: {
   properties: { sheetId: p.id, gridProperties: { rowCount: Math.max(2, p.table.rows.length + 1), columnCount: p.table.columns.length } },
   fields: "gridProperties.rowCount,gridProperties.columnCount"
  } }));
  await call(base + ":batchUpdate", { method: "POST", headers, body: JSON.stringify({ requests }) });
 }
 // Every retry writes the same values to the same ranges, never append. Jobs use disjoint tabs.
 for (const requests of writeBatches(plans)) await call(base + ":batchUpdate", { method: "POST", headers, body: JSON.stringify({ requests }) });
 const query = new URLSearchParams({ includeGridData: "true", fields: "sheets(properties(sheetId,title,gridProperties),data(startRow,startColumn,rowData(values(userEnteredValue))))" });
 for (const p of plans) query.append("ranges", `'${p.title}'`);
 const persisted: SheetResponse = await call(base + "?" + query, { headers });
 if (!verifySheets(plans, persisted)) throw new ExportError("verification", true);
 // Retire only the old auto-generated live tabs after the new view has passed read-back.
 // Preserve their contents and leave manual snapshots / user-created sheets alone.
 if (live) {
  const legacyNames = new Set(["Ringkasan_Harian","Transaksi","Detail_Penjualan","Rekap_Produk","Pembayaran_QRIS","Pencairan_Dana","Detail_Pencairan","Pengembalian","Kas_Harian","Pergerakan_Kas","Pengeluaran","Pemeriksaan_Data","Aktivitas","Info_Laporan"]);
  const retired = (metadata.sheets ?? []).filter(s => {
   const name=s.properties.title.replace(/^WR_live_/, "");
   return s.properties.title === "WR_live_"+name && legacyNames.has(name) && s.properties.sheetId === (createHash("sha256").update("live/"+name).digest().readUInt32BE(0)&0x7fffffff);
  });
  if(retired.length) await call(base+":batchUpdate",{method:"POST",headers,body:JSON.stringify({requests:retired.map(s=>({updateSheetProperties:{properties:{sheetId:s.properties.sheetId,hidden:true},fields:"hidden"}}))})});
 }
 return reportHash(report);
}

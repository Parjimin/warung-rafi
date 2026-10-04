import { createHash } from "node:crypto";
import { simpleSalesReport } from "./simple-sales.ts";
import { exportSheets, ExportError, type SheetsConfig } from "./sheets.ts";
import type { ReportSource } from "./reports.ts";
export type DirectConfig = { supabaseUrl: string; serviceKey: string; google: SheetsConfig };
export type DirectInput = { config: DirectConfig; hash?: string; verifiedAt?: string };
export function validateDirectConfig(config: DirectConfig) {
 const url=new URL(config.supabaseUrl);
 if(url.protocol!=="https:"||url.username||url.password||url.pathname!=="/"||url.search||url.hash||!config.serviceKey||/\s/.test(config.serviceKey)||config.serviceKey.startsWith("sb_publishable_"))throw new ExportError("configuration");
 return url.origin;
}
export async function runDirectSheets(input: DirectInput, request: typeof fetch=fetch, now=new Date()) {
 const origin=validateDirectConfig(input.config), key=input.config.serviceKey;
 const today=new Date(now.getTime()+7*3600000).toISOString().slice(0,10);
 let response:Response;
 try {response=await request(origin+"/rest/v1/rpc/report_source",{method:"POST",headers:{apikey:key,...(key.startsWith("sb_secret_")?{}:{Authorization:"Bearer "+key}),"Content-Type":"application/json"},body:JSON.stringify({p_filter:{mode:"calendar",from:today.slice(0,7)+"-01",to:today}}),redirect:"error",signal:AbortSignal.timeout(15000)});}catch{throw new ExportError("database_network");}
 if(!response.ok)throw new ExportError(response.status===401||response.status===403?"database_permission":"database_service");
 const source=await response.json() as ReportSource;
 const report=simpleSalesReport("0".repeat(32),source),hash=createHash("sha256").update(JSON.stringify([origin,input.config.google.target,report.tables])).digest("hex");
 const elapsed=now.getTime()-Date.parse(input.verifiedAt??"");
 if(input.hash===hash&&elapsed>=0&&elapsed<15*60000)return {state:"unchanged",hash,verifiedAt:input.verifiedAt};
 await exportSheets(report,input.config.google.target,input.config.google,request,true);
 return {state:"verified",hash,verifiedAt:now.toISOString()};
}

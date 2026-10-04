import { simpleSalesReport } from "./simple-sales.ts";
import { createHash, randomUUID } from "node:crypto";
import { rpc } from "./db.ts";
import { type Report, type ReportSource } from "./reports.ts";
import { exportSheets, ExportError, sheetsConfig } from "./sheets.ts";
export function liveReport(source: ReportSource): Report {
 return simpleSalesReport("0".repeat(32), source);
}
export function liveHash(report: Report): string {
 return createHash("sha256").update(JSON.stringify(report.tables)).digest("hex");
}
type Claim = { claimed: boolean; source?: ReportSource; hash?: string; verifiedAt: string | null; code: string | null };
export async function refreshLiveSheets() {
 let config; try { config = sheetsConfig(); } catch { return { state: "unavailable", code: "configuration" }; }
 const token = randomUUID().replaceAll("-", "");
 const job = await rpc("claim_live_sheets", { p_target: config.target, p_token: token }) as Claim;
 if (!job.claimed) return { state: job.code ? "retry" : "waiting", verifiedAt: job.verifiedAt };
 try {
  const report = liveReport(job.source!), hash = liveHash(report);
  // Re-read at least every 15 minutes even without source changes, repairing manual edits.
  const unchanged = hash === job.hash && !job.code && job.verifiedAt && Date.now() - Date.parse(job.verifiedAt) < 15 * 60_000;
  if (unchanged) {
   // Release without moving the last real read-back timestamp.
   await rpc("release_live_sheets", { p_target: config.target, p_token: token });
   return { state: "unchanged", verifiedAt: job.verifiedAt };
  }
  await exportSheets(report, config.target, config, fetch, true);
  await rpc("finish_live_sheets", { p_target: config.target, p_token: token, p_hash: hash });
  return { state: "verified", verifiedAt: new Date().toISOString() };
 } catch (error) {
  const code = error instanceof ExportError ? error.code : "service";
  await rpc("finish_live_sheets", { p_target: config.target, p_token: token, p_code: code });
  return { state: "retry", code, verifiedAt: job.verifiedAt };
 }
}

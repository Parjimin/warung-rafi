import { randomUUID } from "node:crypto";
import { rpc } from "./db.ts";
import { buildReport, type ReportSource } from "./reports.ts";
import { ExportError, exportSheets } from "./sheets.ts";
export type ReportJob = { id: string; filter: ReportSource["filter"]; snapshot: ReportSource; actor: string; created_at: string; target: string | null; state: string; attempts: number; failures: number; next_attempt_at: string | null; error_code: string | null; verified_at: string | null; content_hash: string | null };
export async function runExport(id: string | null = null) {
 const token = randomUUID().replaceAll("-", ""); const job = await rpc("claim_report", { p_token: token, p_id: id }) as ReportJob | null;
 if (!job) return { processed: false };
 try {
  const hash = await exportSheets(buildReport(job.id, job.snapshot), job.target!);
  await rpc("finish_report", { p_id: job.id, p_token: token, p_success: true, p_hash: hash });
  return { processed: true, id: job.id, verified: true };
 } catch (error) {
  // Remote or SQL error text may contain internal data; persist only the closed code set.
  const code = error instanceof ExportError ? error.code : "service", retry = error instanceof ExportError ? error.retry : true;
  await rpc("finish_report", { p_id: job.id, p_token: token, p_success: false, p_code: code, p_retry: retry });
  return { processed: true, id: job.id, verified: false, code };
 }
}

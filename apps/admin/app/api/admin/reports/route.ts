import { admin, sameOrigin } from "../../../../lib/auth.ts";
import { body, handled, HttpError, json, object } from "../../../../lib/http.ts";
import { database, rpc } from "../../../../lib/db.ts";
import { reportFilter, reportId } from "../../../../lib/reports.ts";
import { sheetsConfig } from "../../../../lib/sheets.ts";
import { runExport } from "../../../../lib/report-worker.ts";
export const maxDuration = 60;
export async function GET() {
 return handled(async () => {
  await admin(); let configured = false; try { sheetsConfig(); configured = true; } catch { /* UI shows unavailable configuration */ }
  const [jobs, sessions] = await Promise.all([
   database("report_jobs?select=id,filter,created_at,state,attempts,failures,next_attempt_at,error_code,verified_at,target,content_hash&order=created_at.desc,id&limit=30"),
   database("cash_sessions?select=device_id,id,closed,payload&order=payload->>openedAt.desc&limit=50")
  ]);
  return json({ jobs, sessions, configured });
 });
}
export async function POST(request: Request) {
 return handled(async () => {
  sameOrigin(request); const actor = await admin(); const input = object(await body(request, 16000)); const id = reportId(input.id);
  if (input.action === "create") return json(await rpc("create_report", { p_id: id, p_filter: reportFilter(input.filter), p_actor: actor }));
  if (input.action === "queue") {
   let target: string; try { target = sheetsConfig().target; } catch { throw new HttpError(503, "Google Sheets belum dikonfigurasi. CSV tetap tersedia."); }
   await rpc("queue_report", { p_id: id, p_target: target }); return json({ queued: true });
  }
  if (input.action === "run") return json(await runExport(id));
  throw new HttpError(400, "Tindakan laporan tidak dikenal.");
 });
}

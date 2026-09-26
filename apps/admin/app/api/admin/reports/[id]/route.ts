import { admin } from "../../../../../lib/auth.ts";
import { database } from "../../../../../lib/db.ts";
import { handled, HttpError, json } from "../../../../../lib/http.ts";
import { buildReport, reportCsv, reportId } from "../../../../../lib/reports.ts";
import type { ReportJob } from "../../../../../lib/report-worker.ts";
export async function GET(request: Request, context: { params: Promise<{ id: string }> }) {
 return handled(async () => {
  await admin(); const id = reportId((await context.params).id);
  const jobs = await database(`report_jobs?id=eq.${id}&select=id,snapshot`) as ReportJob[];
  if (!jobs[0]) throw new HttpError(404, "Laporan tidak ditemukan.");
  const report = buildReport(id, jobs[0].snapshot), table = new URL(request.url).searchParams.get("csv");
  if (table) return new Response(reportCsv(report, table), { headers: { "Content-Type": "text/csv; charset=utf-8", "Content-Disposition": `attachment; filename="WarungRafi-${id}-${report.tables.find(t => t.name === table)?.name ?? "report"}.csv"`, "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" } });
  return json({ ...report, tables: report.tables.map(t => ({ ...t, count: t.rows.length, rows: t.rows.slice(0, 20) })) });
 });
}

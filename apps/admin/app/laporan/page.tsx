import { redirect } from "next/navigation";
import { admin } from "../../lib/auth.ts";
import { HttpError } from "../../lib/http.ts";
import Reports from "./reports.tsx";
export const dynamic = "force-dynamic";
export default async function ReportPage() {
 try { await admin(); }
 catch (error) {
  if (error instanceof HttpError && [401, 403].includes(error.status)) redirect("/login");
  return <main><section className="notice"><h1>Layanan belum terhubung.</h1><p>Laporan belum dapat dimuat. Coba kembali setelah layanan siap.</p></section></main>;
 }
 return <Reports />;
}

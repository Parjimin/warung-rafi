import { sheetsConfig } from "./sheets.ts";
import { rpc } from "./db.ts";

export type ReadinessCheck = { id: string; name: string; state: "checked" | "configured" | "missing"; detail: string };
export type Readiness = { checkedAt: string; revision: string | null; checks: ReadinessCheck[] };
const clean = (value: string | undefined) => !!value && value.trim() === value;
export function integrationConfiguration() {
 let sheets = false;
 try { sheetsConfig(); sheets = true; } catch { /* Report configuration only; never expose key parsing errors. */ }
 const environment = process.env.MIDTRANS_ENV;
 return {
  sheets,
  qris: clean(process.env.MIDTRANS_SERVER_KEY) && clean(process.env.MIDTRANS_MERCHANT_ID) && (environment === "sandbox" || environment === "production"),
  environment: environment === "sandbox" || environment === "production" ? environment : null,
  runner: clean(process.env.EXPORT_RUNNER_TOKEN) && (process.env.EXPORT_RUNNER_TOKEN?.length ?? 0) >= 32
 };
}
export async function readiness(): Promise<Readiness> {
 const checks: ReadinessCheck[] = [];
 try {
  const setup = await rpc("device_setup", {}) as { schema?: number };
  checks.push({ id: "database", name: "Database", state: setup.schema === 6 ? "checked" : "missing", detail: setup.schema === 6 ? "Koneksi dan fungsi setup versi 6 berhasil diperiksa. Hak akses dan transaksi tetap perlu diuji." : "Versi setup database belum cocok; periksa migrasi 001–006." });
 } catch {
  checks.push({ id: "database", name: "Database", state: "missing", detail: "Koneksi atau fungsi setup gagal diperiksa. Periksa konfigurasi server dan migrasi." });
 }
 let origin = false;
 try { const value = process.env.APP_ORIGIN ?? ""; const u = new URL(value); origin = u.origin === value && (u.protocol === "https:" || u.protocol === "http:" && ["localhost", "127.0.0.1"].includes(u.hostname)); } catch { /* Invalid URL. */ }
 checks.push({ id: "origin", name: "Alamat admin", state: origin ? "configured" : "missing", detail: origin ? "Format origin valid. Pastikan sama persis dengan domain yang dibuka saat login." : "Isi APP_ORIGIN dengan origin HTTPS, tanpa path atau garis miring akhir." });
 const device = clean(process.env.DEVICE_API_TOKEN) && (process.env.DEVICE_API_TOKEN?.length ?? 0) >= 32 && clean(process.env.DEVICE_ID) && (process.env.DEVICE_ID?.length ?? 0) <= 80;
 checks.push({ id: "device", name: "Konfigurasi kasir", state: device ? "configured" : "missing", detail: device ? "Identitas dan token server tersedia. Koneksi laptop dibuktikan oleh laporan perangkat di bawah." : "Periksa DEVICE_ID dan token perangkat minimal 32 karakter pada server." });
 const config = integrationConfiguration();
 checks.push({ id: "qris", name: "QRIS", state: config.qris ? "configured" : "missing", detail: config.qris ? `Konfigurasi ${config.environment} tersedia. Webhook, merchant dan pembayaran nyata belum dibuktikan oleh pemeriksaan ini.` : "Lengkapi key, merchant dan lingkungan Midtrans yang valid pada server." });
 checks.push({ id: "sheets", name: "Google Sheets", state: config.sheets ? "configured" : "missing", detail: config.sheets ? "Format akun layanan dan ID spreadsheet valid. Hak akses serta ekspor harus diuji dari halaman Laporan." : "Konfigurasi spreadsheet atau akun layanan belum valid. CSV tetap tersedia." });
 checks.push({ id: "runner", name: "Pemroses ekspor", state: config.runner ? "configured" : "missing", detail: config.runner ? "Token pemroses tersedia. Jadwal otomatis dan keberhasilan ekspor belum diverifikasi." : "Token pemroses ekspor belum valid. Pemrosesan manual tetap tersedia jika Sheets dikonfigurasi." });
 const revision = process.env.VERCEL_GIT_COMMIT_SHA ?? "";
 return { checkedAt: new Date().toISOString(), revision: /^[a-f0-9]{40}$/i.test(revision) ? revision : null, checks };
}

import { HttpError, object } from "./http.ts";
import { reportQuery } from "./reconciliation.ts";
export type Cell = string | number | null;
export type ReportFilter = { mode: "calendar"; from: string; to: string } | { mode: "session"; deviceId: string; sessionId: string };
export function reportFilter(input: unknown): ReportFilter {
 const v = object(input);
 if (v.mode === "calendar") { const q = reportQuery(new URLSearchParams({ from: String(v.from), to: String(v.to) })); return { mode: "calendar", from: q.p_from, to: q.p_to }; }
 if (v.mode === "session" && typeof v.deviceId === "string" && v.deviceId.length > 0 && v.deviceId.length <= 80 && typeof v.sessionId === "string" && /^[a-f0-9]{32}$/.test(v.sessionId)) return { mode: "session", deviceId: v.deviceId, sessionId: v.sessionId };
 throw new HttpError(400, "Pilih sesi kas atau periode tanggal yang valid.");
}
export function reportId(value: unknown): string { if (typeof value !== "string" || !/^[a-f0-9]{32}$/.test(value)) throw new HttpError(400, "Identitas laporan tidak valid."); return value; }
// These source rows come only from report_source(), never from a browser request.
type Line = { productId: string; name: string; category: string; quantity: number; unitPrice: number; subtotal: number };
type Order = { device_id: string; id: string; status: number; version: number; cashier: string | null; session_id: string | null; payload: { order: { number: string; createdAt: string; updatedAt: string; cancellationReason: string | null; lines: Line[] }; payment: { method: number; amount: number; tendered: number; change: number; paidAt: string } | null }; match: { transaction_id: string; difference: number } | null };
type Refund = { device_id: string; id: string; order_id: string; amount: number; state: number; completed_in_period: boolean; requested_by: string | null; payload: { channel: number; reason: string; requestedAt: string; completedAt: string | null; sessionId: string | null; approvedBy: string; reference: string; failureReason: string } };
type Session = { device_id: string; id: string; expected: number; closed: boolean; payload: { openedAt: string; closedAt: string | null; openingCash: number; openedBy: string; countedCash: number | null; expectedAtClose: number | null; difference: number | null; closingNote: string } };
type Event = { device_id: string; id: string; sequence: number; session_id: string | null; order_id: string | null; kind: string; amount: number; cash_delta: number; occurred_at: string; in_period: boolean; payload: { actor: string; reason: string; details: { id?: string } } };
import type { ProviderRow, PayoutRow, FinanceSnapshot } from "./reconciliation.ts";
export type ReportSource = {
 schema: number; capturedAt: string; revision: number; filter: ReportFilter; start: string; end: string; from: string; to: string;
 orders: Order[]; refunds: Refund[]; sessions: Session[]; events: Event[];
 providers: (ProviderRow & { in_period: boolean; updated_at: string; received_at?: string | null })[];
 payouts: (PayoutRow & { reported_in_period: boolean; received_in_period: boolean; items: { transaction_id: string; gross: number; fees: number; refunded: number }[] })[];
 devices: FinanceSnapshot["devices"];
 activity: { id: string; created_at: string; actor: string; action: string; reason: string; object: string | null }[];
};
export type ReportTable = { name: string; columns: string[]; rows: Cell[][] };
export type Report = { id: string; capturedAt: string; filter: ReportFilter; from: string; to: string; revision: number; totals: { sales: number; gross: number; cash: number; qris: number; refunds: number; expenses: number; providerGross: number; actualFees: number; unknownFees: number; payoutNet: number; receivedBank: number }; tables: ReportTable[] };
const key = (...v: (string | number)[]) => v.map(s => encodeURIComponent(String(s))).join("/");
export const wib = (s: string | null) => s ? new Date(Date.parse(s) + 7 * 3600000).toISOString().replace("Z", "+07:00") : "";
const day = (s: string) => wib(s).slice(0, 10);
const total = (values: number[]) => { const n = values.reduce((a, b) => a + b, 0); if (!Number.isSafeInteger(n)) throw new HttpError(409, "Angka laporan di luar batas rupiah bulat."); return n; };
export function buildReport(id: string, s: ReportSource): Report {
 reportId(id); if (s.schema !== 1) throw new HttpError(409, "Versi laporan belum didukung.");
 const tables: ReportTable[] = []; const table = (name: string, columns: string[], rows: Cell[][]) => { tables.push({ name, columns, rows }); };
 const sales = s.orders.filter(o => o.status === 2); const completed = s.refunds.filter(r => r.state === 1 && r.completed_in_period);
 const events = s.events.filter(e => e.in_period); const proofs = s.providers.filter(p => p.in_period);
 const totals = { sales: sales.length, gross: total(sales.map(o => o.payload.payment!.amount)), cash: total(sales.filter(o => o.payload.payment!.method === 0).map(o => o.payload.payment!.amount)), qris: total(sales.filter(o => o.payload.payment!.method === 1).map(o => o.payload.payment!.amount)), refunds: total(completed.map(r => r.amount)), expenses: total(events.filter(e => e.kind === "cash_out").map(e => e.amount)), providerGross: total(proofs.map(p => p.amount)), actualFees: total(proofs.map(p => (p.cost?.actual_mdr ?? 0) + (p.cost?.actual_other ?? 0))), unknownFees: proofs.filter(p => p.cost?.actual_mdr == null).length, payoutNet: total(s.payouts.filter(p => p.reported_in_period && !p.voided_at).map(p => p.expected_net)), receivedBank: total(s.payouts.filter(p => p.received_in_period && !p.voided_at).map(p => p.received_amount ?? 0)) };
 const exceptions: Cell[][] = [];
 const exception = (id: string, kind: string, object: string, amount: Cell, note: string) => exceptions.push([id, kind, object, amount, note, "Perlu diperiksa"]);
 if (!s.devices.length) exception("sync/no-device", "Kelengkapan", "Perangkat", null, "Belum ada laporan perangkat; data cloud mungkin belum lengkap.");
 for (const d of s.devices) if (d.pending_count == null || d.pending_count > 0 || !d.last_report_at || Date.parse(s.capturedAt) - Date.parse(d.last_report_at) > 900000) exception(key("sync", d.device_id), "Sinkronisasi", d.device_id, d.pending_count, "Antrean belum kosong atau laporan perangkat lebih dari 15 menit.");
 for (const o of sales) {
  if (total(o.payload.order.lines.map(l => l.subtotal)) !== o.payload.payment!.amount) throw new HttpError(409, "Subtotal item berbeda dari pembayaran; periksa data sumber.");
  if (!o.session_id) exception(key("session", o.device_id, o.id), "Sesi belum diketahui", o.payload.order.number, o.payload.payment!.amount, "Penjualan lama atau jurnal sesi belum tersinkron.");
  if (o.payload.payment!.method === 1 && !o.match) exception(key("order", o.device_id, o.id), "Pesanan QRIS belum cocok", o.payload.order.number, o.payload.payment!.amount, "Tidak dipasangkan otomatis berdasarkan nominal.");
 }
 for (const p of proofs) {
  if (!p.match) exception(key("proof", p.transaction_id), "Bukti belum cocok", p.transaction_id, p.amount, "Bukti provider bukan penjualan tambahan.");
  if (p.match?.difference) exception(key("difference", p.transaction_id), "Selisih pasangan", p.transaction_id, p.match.difference, "Periksa alasan pencocokan.");
  if (p.cost?.actual_mdr == null) exception(key("cost", p.transaction_id), "Biaya belum diketahui", p.transaction_id, null, "Estimasi tidak dianggap biaya aktual.");
 }
 for (const p of s.payouts.filter(p => !p.voided_at)) if (p.received_amount == null || p.received_amount !== p.expected_net) exception(key("bank", p.id), p.received_amount == null ? "Mutasi belum diketahui" : "Selisih bank", p.reference, p.received_amount == null ? null : p.received_amount - p.expected_net, "Periksa laporan provider dan mutasi bank.");
 const dates = [...new Set([...sales.map(o => day(o.payload.payment!.paidAt)), ...completed.map(r => day(r.payload.completedAt!)), ...proofs.map(p => day(p.paid_at)), ...events.map(e => day(e.occurred_at)), ...s.orders.filter(o => o.status === 3).map(o => day(o.payload.order.updatedAt)), ...s.payouts.flatMap(p => [p.reported_in_period ? p.reported_on : "", p.received_in_period ? p.received_on ?? "" : ""])].filter(Boolean))].sort();
 table("Ringkasan_Harian", ["id", "tanggal_wib", "pesanan_selesai", "bruto", "tunai", "qris_kasir", "refund_selesai_periode", "bruto_kurang_refund_periode", "qris_provider", "biaya_aktual_tercatat", "bukti_biaya_belum_diketahui", "estimasi_tercatat", "neto_pencairan", "mutasi_bank", "pengeluaran_laci", "kas_masuk_lain", "pesanan_dibatalkan", "catatan"], dates.map(d => {
  const orders = sales.filter(o => day(o.payload.payment!.paidAt) === d), ps = proofs.filter(p => day(p.paid_at) === d), rs = completed.filter(r => day(r.payload.completedAt!) === d);
  const gross = total(orders.map(o => o.payload.payment!.amount)), refunded = total(rs.map(r => r.amount));
  return [key("day", d), d, orders.length, gross, total(orders.filter(o => o.payload.payment!.method === 0).map(o => o.payload.payment!.amount)), total(orders.filter(o => o.payload.payment!.method === 1).map(o => o.payload.payment!.amount)), refunded, gross - refunded, total(ps.map(p => p.amount)), total(ps.map(p => (p.cost?.actual_mdr ?? 0) + (p.cost?.actual_other ?? 0))), ps.filter(p => p.cost?.actual_mdr == null).length, total(ps.map(p => p.cost?.estimated_fee ?? 0)), total(s.payouts.filter(p => !p.voided_at && p.reported_in_period && p.reported_on === d).map(p => p.expected_net)), total(s.payouts.filter(p => !p.voided_at && p.received_in_period && p.received_on === d).map(p => p.received_amount ?? 0)), total(events.filter(e => e.kind === "cash_out" && day(e.occurred_at) === d).map(e => e.amount)), total(events.filter(e => e.kind === "cash_in" && day(e.occurred_at) === d).map(e => e.amount)), s.orders.filter(o => o.status === 3 && day(o.payload.order.updatedAt) === d).length, "Bukan laba; refund mengikuti waktu berhasil. Periksa Pemeriksaan_Data."];
 }));
 table("Transaksi", ["id", "perangkat", "pesanan_id", "nomor", "kasir_tercatat", "sesi_selesai", "dibuat_wib", "selesai_atau_batal_wib", "status", "metode", "bruto", "tunai_diterima", "kembalian", "bukti_provider", "refund_kumulatif_sampai_snapshot", "versi", "alasan_batal"], s.orders.map(o => { const p = o.payload.payment; return [key(o.device_id, o.id), o.device_id, o.id, o.payload.order.number, o.cashier, o.session_id, wib(o.payload.order.createdAt), wib(p?.paidAt ?? o.payload.order.updatedAt), o.status === 2 ? "Selesai" : "Dibatalkan", p ? p.method === 0 ? "Tunai" : "QRIS dicatat kasir" : null, p?.amount ?? null, p?.method === 0 ? p.tendered : null, p?.method === 0 ? p.change : null, o.match?.transaction_id ?? null, total(s.refunds.filter(r => r.device_id === o.device_id && r.order_id === o.id && r.state === 1).map(r => r.amount)), o.version, o.payload.order.cancellationReason]; }));
 const detail: Cell[][] = []; const products = new Map<string, { id: string; name: string; category: string; units: number; gross: number }>();
 for (const o of sales) for (const l of o.payload.order.lines) {
  if (l.quantity * l.unitPrice !== l.subtotal) throw new HttpError(409, "Perhitungan item tidak cocok.");
  detail.push([key(o.device_id, o.id, l.productId), key(o.device_id, o.id), o.payload.order.number, l.productId, l.name, l.category, l.quantity, l.unitPrice, l.subtotal, wib(o.payload.payment!.paidAt), "Jenis paket/unit/versi katalog belum direkam; harga adalah snapshot transaksi"]);
  const k = key(l.productId, l.name, l.category), prev = products.get(k) ?? { id: l.productId, name: l.name, category: l.category, units: 0, gross: 0 }; prev.units += l.quantity; prev.gross += l.subtotal; products.set(k, prev);
 }
 table("Detail_Penjualan", ["id", "transaksi_id", "nomor", "produk_id", "nama_snapshot", "kategori_snapshot", "jumlah", "harga_snapshot", "subtotal", "bayar_wib", "batas_data"], detail);
 table("Rekap_Produk", ["id", "produk_id", "nama_snapshot", "kategori_snapshot", "unit_bruto", "nilai_bruto", "refund_item", "catatan"], [...products].sort(([a], [b]) => a.localeCompare(b)).map(([k, p]) => [k, p.id, p.name, p.category, p.units, p.gross, null, "Refund hanya nominal; unit/nilai neto per produk belum dapat dihitung."]));
 table("Pembayaran_QRIS", ["id", "order_provider", "merchant", "bayar_wib", "diterima_server_wib", "status", "nominal", "dalam_cakupan", "perangkat_pasangan", "pesanan_pasangan", "selisih_pasangan", "estimasi", "tarif_snapshot", "mdr_aktual", "biaya_lain_aktual", "refund_provider", "referensi_biaya", "sumber"], s.providers.map(p => [p.transaction_id, p.order_id, p.merchant_id, wib(p.paid_at), wib(p.received_at ?? null), p.status, p.amount, p.in_period ? "Ya" : "Konteks pasangan di luar periode", p.match?.device_id ?? null, p.match?.order_id ?? null, p.match?.difference ?? null, p.cost?.estimated_fee ?? null, p.cost?.profile ? JSON.stringify(p.cost.profile) : null, p.cost?.actual_mdr ?? null, p.cost?.actual_other ?? null, p.cost?.actual_refund ?? null, p.cost?.reference ?? null, "Webhook provider; biaya manual berreferensi"]));
 table("Pencairan_Dana", ["id", "referensi_laporan", "tanggal_laporan", "bruto", "biaya_transaksi", "refund_provider", "biaya_pencairan", "penyesuaian", "neto", "bank", "rekening_tersamar", "tanggal_mutasi", "nominal_mutasi", "selisih_bank", "referensi_mutasi", "status", "laporan_dalam_periode", "mutasi_dalam_periode", "sumber"], s.payouts.map(p => [p.id, p.reference, p.reported_on, p.gross, p.transaction_fees, p.refunds, p.fee, p.adjustment, p.expected_net, p.bank_name, "•••• " + p.account_last4, p.received_on, p.received_amount, p.received_amount == null ? null : p.received_amount - p.expected_net, p.bank_reference, p.voided_at ? "Dibatalkan" : p.received_amount == null ? "Belum dikonfirmasi bank" : "Mutasi tercatat", p.reported_in_period ? "Ya" : "Tidak", p.received_in_period ? "Ya" : "Tidak", "Manual berreferensi laporan dan mutasi"]));
 table("Detail_Pencairan", ["id", "pencairan_id", "provider_id", "bruto", "biaya_transaksi", "refund_provider", "neto_sebelum_biaya_batch"], s.payouts.flatMap(p => p.items.map(i => [key(p.id, i.transaction_id), p.id, i.transaction_id, i.gross, i.fees, i.refunded, i.gross - i.fees - i.refunded])));
 table("Pengembalian", ["id", "transaksi_id", "sesi_pengembalian", "nominal", "metode", "status", "diminta_wib", "selesai_wib", "selesai_dalam_periode", "alasan", "pemohon", "penyetuju", "referensi", "alasan_gagal"], s.refunds.map(r => [key(r.device_id, r.id), key(r.device_id, r.order_id), r.payload.sessionId, r.amount, r.payload.channel === 0 ? "Tunai" : "Transfer/provider", ["Diminta", "Selesai", "Gagal"][r.state], wib(r.payload.requestedAt), wib(r.payload.completedAt), r.completed_in_period ? "Ya" : "Tidak / konteks transaksi asal", r.payload.reason, r.requested_by, r.payload.approvedBy, r.payload.reference, r.payload.failureReason]));
 table("Kas_Harian", ["id", "perangkat", "sesi_id", "buka_wib", "tutup_wib", "pembuka", "penutup", "modal", "penjualan_tunai_neto_kembalian", "kas_masuk_lain", "refund_tunai", "kas_keluar_lain", "seharusnya", "fisik", "selisih", "status", "catatan", "basis"], s.sessions.map(x => {
  const es = s.events.filter(e => e.device_id === x.device_id && e.session_id === x.id), delta = total(es.map(e => e.cash_delta));
  if (delta !== x.expected) throw new HttpError(409, "Jurnal kas belum cocok dengan saldo sesi.");
  return [key(x.device_id, x.id), x.device_id, x.id, wib(x.payload.openedAt), wib(x.payload.closedAt), x.payload.openedBy, es.find(e => e.kind === "session_closed")?.payload.actor ?? null, x.payload.openingCash, total(es.filter(e => e.kind === "sale").map(e => e.cash_delta)), total(es.filter(e => e.kind === "cash_in").map(e => e.amount)), -total(es.filter(e => e.kind === "refund_completed").map(e => e.cash_delta)), total(es.filter(e => e.kind === "cash_out").map(e => e.amount)), x.expected, x.payload.countedCash, x.payload.difference, x.closed ? "Ditutup" : "Terbuka pada waktu snapshot", x.payload.closingNote, "Seluruh sesi; dapat melewati tanggal filter"];
 }));
 table("Pergerakan_Kas", ["id", "sesi_id", "waktu_wib", "jenis", "masuk", "keluar", "transaksi_id", "alasan", "petugas", "dalam_periode"], s.events.filter(e => e.cash_delta !== 0 || e.kind === "session_closed").map(e => [key(e.device_id, e.id), key(e.device_id, e.session_id ?? ""), wib(e.occurred_at), e.kind, Math.max(0, e.cash_delta), Math.max(0, -e.cash_delta), e.order_id ? key(e.device_id, e.order_id) : null, e.payload.reason, e.payload.actor, e.in_period ? "Ya" : "Konteks sesi di luar periode"]));
 table("Pengeluaran", ["id", "waktu_wib", "nominal", "sumber", "sesi_id", "alasan", "petugas", "kategori", "lampiran"], events.filter(e => e.kind === "cash_out").map(e => [key(e.device_id, e.id), wib(e.occurred_at), e.amount, "Laci kas", key(e.device_id, e.session_id ?? ""), e.payload.reason, e.payload.actor, "Belum direkam", "Belum direkam"]));
 table("Pemeriksaan_Data", ["id", "jenis", "objek", "nominal_atau_antrean", "keterangan", "status"], exceptions);
 table("Aktivitas", ["id", "waktu_wib", "pelaku", "tindakan", "objek", "alasan"], [...s.activity.map(a => [a.id, wib(a.created_at), a.actor, a.action, a.object, a.reason] as Cell[]), ...events.map(e => [key("ledger", e.device_id, e.id), wib(e.occurred_at), e.payload.actor, e.kind, e.order_id ?? e.session_id, e.payload.reason] as Cell[])]);
 table("Info_Laporan", ["id", "nilai"], [["laporan_id", id], ["format", "1"], ["basis", s.filter.mode], ["periode_awal", s.from], ["periode_akhir", s.to], ["snapshot_wib", wib(s.capturedAt)], ["revisi_keuangan", s.revision], ["mata_uang", "IDR"], ["status_sheets", "Lihat status dan waktu verifikasi pada halaman Laporan admin"], ["catatan", "Salinan tetap; buat laporan baru setelah sinkronisasi atau koreksi. Jangan menjumlahkan omzet + QRIS provider + pencairan."], ["cakupan_sesi", s.filter.mode === "session" ? "Bukti provider hanya pasangan pesanan sesi. Pencairan/mutasi bank tersedia pada laporan tanggal kalender." : "Kas_Harian memuat seluruh sesi yang bersinggungan dengan periode."], ["kelengkapan", `${exceptions.length} pengecualian; periksa Pemeriksaan_Data`], ["batas_data", "Tidak memuat label pelanggan. Kasir bernama pribadi, tipe paket/unit/versi katalog, lampiran dan refund item belum direkam."]]);
 for (const t of tables) {
  if (new Set(t.rows.map(r => r[0])).size !== t.rows.length) throw new HttpError(409, "Identitas baris laporan berulang.");
  for (const row of t.rows) if (row.length !== t.columns.length || row.some(c => c === undefined || typeof c === "number" && !Number.isSafeInteger(c))) throw new HttpError(409, "Struktur laporan tidak konsisten.");
 }
 if (tables.reduce((n, t) => n + (t.rows.length + 1) * t.columns.length, 0) > 150000) throw new HttpError(400, "Laporan terlalu besar. Pilih periode lebih pendek.");
 return { id, capturedAt: s.capturedAt, filter: s.filter, from: s.from, to: s.to, revision: s.revision, totals, tables };
}
export function csvCell(cell: Cell): string {
 if (cell == null) return ""; if (typeof cell === "number") return String(cell);
 // Spreadsheet programs may interpret quoted text as formulas; prefix dangerous text.
 const safe = /^[\s\u0000-\u001f]*[=+@-]/.test(cell) || /^[\t\r\n]/.test(cell) || /^0\d+$/.test(cell) || /^\d{15,}$/.test(cell) ? "'" + cell : cell;
 return '"' + safe.replaceAll('"', '""') + '"';
}
export function reportCsv(report: Report, name: string): string {
 const t = report.tables.find(t => t.name === name); if (!t) throw new HttpError(404, "Bagian laporan tidak ditemukan.");
 const columns = ["laporan_id", "basis", "periode_awal", "periode_akhir", "snapshot_wib", "mata_uang", ...t.columns];
 const prefix: Cell[] = [report.id, report.filter.mode, report.from, report.to, wib(report.capturedAt), "IDR"];
 return "\uFEFF" + [columns, ...t.rows.map(row => [...prefix, ...row])].map(row => row.map(csvCell).join(",")).join("\r\n") + "\r\n";
}

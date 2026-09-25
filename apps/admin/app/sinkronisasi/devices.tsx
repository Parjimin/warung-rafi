"use client";
import { useCallback, useEffect, useState } from "react";
import type { DeviceStatus } from "../../lib/device-status.ts";
const time = (value: string | null) => value ? new Intl.DateTimeFormat("id-ID", { dateStyle: "medium", timeStyle: "medium", timeZone: "Asia/Jakarta" }).format(new Date(value)) + " WIB" : "Belum ada laporan";
export default function Devices() {
  const [devices, setDevices] = useState<DeviceStatus[] | null>(null);
  const [version, setVersion] = useState<number | null>(null);
  const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
  const [checked, setChecked] = useState<string | null>(null);
  const load = useCallback(async () => {
    setBusy(true);
    try {
      const responses = await Promise.all([fetch("/api/admin/devices"), fetch("/api/admin/catalog")]);
      if (responses.some(r => r.status === 401)) { location.assign("/login"); return; }
      const [rows, catalog] = await Promise.all(responses.map(r => r.json()));
      if (!responses[0].ok || !responses[1].ok) throw new Error(rows.error || catalog.error || "Status belum berhasil diperiksa.");
      setDevices(rows); setVersion(catalog.published_version); setChecked(new Date().toISOString()); setMessage("");
    } catch (error) { setMessage(error instanceof Error ? error.message : "Status belum berhasil diperiksa."); }
    finally { setBusy(false); }
  }, []);
  useEffect(() => { void load(); const timer = setInterval(() => void load(), 15_000); return () => clearInterval(timer); }, [load]);
  return <main><div className="heading"><div><p className="eyebrow">LAPTOP KASIR</p><h1>Data tetap terpantau.</h1><p>Lihat antrean pengiriman dan katalog yang sudah tersimpan di laptop.</p></div><button className="secondary" disabled={busy} onClick={() => void load()}>{busy ? "Memeriksa…" : "Periksa sekarang"}</button></div>
    <p role="status" className={message ? "message error" : "message"}>{message || (checked ? `Terakhir diperiksa: ${time(checked)}` : "Memuat status…")}</p>
    <section className="devices">{devices?.map(device => {
      const stale = Date.now() - Date.parse(device.last_seen_at) > 90_000;
      const reported = device.catalog_version !== null;
      return <article className="device" key={device.device_id}><div className="device-head"><h2>{device.device_id === "kasir-utama" ? "Kasir utama" : device.device_id}</h2><span className={`device-state ${stale || device.conflict_at ? "warning" : ""}`}>{device.conflict_at ? "Perlu diperiksa" : stale ? "Belum ada kabar terbaru" : "Baru terhubung"}</span></div>
        <dl><div><dt>Antrean menurut laporan laptop</dt><dd>{device.pending_count === null ? "Belum diketahui" : `${device.pending_count} perubahan`}</dd></div><div><dt>Katalog tersimpan / diterbitkan</dt><dd>{device.catalog_version ?? "—"} / {version ?? "—"}</dd></div><div><dt>Terakhir terhubung</dt><dd>{time(device.last_seen_at)}</dd></div></dl>
        <p className="sync-note">{reported && version !== null ? device.catalog_version! < version ? "Katalog terbaru belum dilaporkan tersimpan di laptop." : "Versi katalog sudah tersimpan di laptop. Pesanan yang sedang berjalan mempertahankan harga sebelumnya." : "Versi katalog laptop belum dilaporkan."}</p>
        <p className="sync-note">Laporan antrean: {time(device.last_report_at)} · Pengiriman berhasil: {time(device.last_sync_at)}</p>
        {device.conflict_at && <div className="conflict"><strong>Urutan data perlu diperiksa</strong><p>Batch ditolak pada {time(device.conflict_at)}. Data tetap tersimpan di laptop. Jangan menghapus database atau antrean.</p><details><summary>Lihat ID perubahan untuk pemeriksaan</summary><ul>{device.conflict_events.map(id => <li key={id}><code>{id}</code></li>)}</ul></details></div>}
      </article>;
    })}</section>
    {devices?.length === 0 && <section className="empty"><h2>Belum ada laporan laptop.</h2><p>Buka aplikasi kasir yang sudah terhubung ke layanan ini, lalu periksa kembali.</p></section>}
    <p className="sync-note">Angka adalah laporan terakhir, bukan kondisi langsung saat laptop offline. Antrean menghitung perubahan data, bukan jumlah transaksi. Status diperiksa otomatis setiap 15 detik.</p>
  </main>;
}

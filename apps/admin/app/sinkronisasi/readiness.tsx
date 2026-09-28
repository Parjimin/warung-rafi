"use client";
import { useEffect, useState } from "react";
import type { Readiness } from "../../lib/readiness.ts";
export default function ReadinessPanel() {
 const [result, setResult] = useState<Readiness | null>(null);
 const [message, setMessage] = useState("");
 const [busy, setBusy] = useState(false);
 async function load() {
  setBusy(true); setMessage(""); setResult(null);
  try {
   const response = await fetch("/api/admin/readiness", { cache: "no-store" });
   if (response.status === 401 || response.status === 403) { location.assign("/login"); return; }
   const data = await response.json();
   if (!response.ok) throw new Error(data.error || "Pemeriksaan belum berhasil.");
   setResult(data);
  } catch { setMessage("Konfigurasi belum berhasil diperiksa. Coba lagi."); }
  finally { setBusy(false); }
 }
 useEffect(() => { void load(); }, []);
 return <section aria-labelledby="readiness-title" className="notice">
  <h2 id="readiness-title">Kesiapan layanan</h2>
  <p>Konfigurasi tersedia belum berarti koneksi sudah teruji. Selesaikan uji laptop, printer, pembayaran dan pemulihan sebelum berjualan.</p>
  <button className="secondary" disabled={busy} onClick={() => void load()}>{busy ? "Memeriksa konfigurasi…" : "Periksa konfigurasi"}</button>
  <p role="status">{message || (result ? `Diperiksa ${new Intl.DateTimeFormat("id-ID", {dateStyle:"medium",timeStyle:"medium",timeZone:"Asia/Jakarta"}).format(new Date(result.checkedAt))} WIB` : "Memuat konfigurasi…")}</p>
  {result && <><div className="devices">{result.checks.map(check => <article className="device" key={check.id}>
   <h3>{check.name}</h3><strong>{check.state === "checked" ? "Pemeriksaan berhasil" : check.state === "configured" ? "Konfigurasi tersedia · perlu uji" : "Perlu dilengkapi"}</strong><p>{check.detail}</p>
  </article>)}</div><details><summary>Identitas deployment untuk pemeriksaan</summary><p style={{overflowWrap:"anywhere"}}>{result.revision || "Identitas commit tidak tersedia dari hosting. Cocokkan deployment dengan rilis yang sudah lulus CI."}</p></details></>}
 </section>;
}

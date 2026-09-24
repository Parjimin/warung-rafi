"use client";
import { useCallback, useEffect, useState } from "react";
import { categories, type CatalogState, type Product } from "../lib/catalog.ts";
const rupiah = (value: number) => new Intl.NumberFormat("id-ID", { style: "currency", currency: "IDR", maximumFractionDigits: 0 }).format(value);
export default function Catalog() {
  const [state, setState] = useState<CatalogState | null>(null); const [items, setItems] = useState<Product[]>([]);
  const [filter, setFilter] = useState("Semua"); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false); const [dirty, setDirty] = useState(false);
  const [editing, setEditing] = useState<Product | null>(null);
  const load = useCallback(async () => {
    const response = await fetch("/api/admin/catalog"); const data = await response.json();
    if (response.status === 401) { location.assign("/login"); return; }
    if (!response.ok) throw new Error(data.error);
    setState(data); setItems(data.draft); setDirty(false);
  }, []);
  useEffect(() => { void load().catch(error => setMessage(error.message)); }, [load]);
  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => { if (dirty) { event.preventDefault(); event.returnValue = ""; } };
    window.addEventListener("beforeunload", warn); return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);
  async function save(action: "save" | "publish") {
    if (!state) return; setBusy(true); setMessage("");
    try {
      const response = await fetch("/api/admin/catalog", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ action, version: state.draft_version, products: items }) });
      const data = await response.json(); if (!response.ok) throw new Error(data.error);
      await load(); setMessage(action === "save" ? "Draf tersimpan. Terbitkan saat menu sudah siap." : "Menu diterbitkan. Kasir mengambil pembaruan ketika online; harga pesanan lama tetap.");
    } catch (error) { setMessage(error instanceof Error ? error.message : "Belum berhasil menyimpan."); } finally { setBusy(false); }
  }
  return <main><div className="heading"><div><p className="eyebrow">MENU WARUNG</p><h1>Siap disajikan hari ini.</h1><p>Atur pilihan makanan, harga, dan ketersediaan untuk kasir.</p></div><button className="secondary" onClick={async () => { if (dirty && !confirm("Ada perubahan belum disimpan. Tetap keluar?")) return; await fetch("/api/auth/logout", { method: "POST" }); location.assign("/login"); }}>Keluar</button></div>
    <section className="stats"><article><span>Menu di draf</span><strong>{state ? items.length : "—"}</strong></article><article><span>Tersedia</span><strong>{state ? items.filter(item => item.available).length : "—"}</strong></article><article><span>Versi di kasir</span><strong>{state?.published_version ?? "—"}</strong><small>Perangkat memperbarui saat online</small></article></section>
    <div className="toolbar"><div className="tabs">{["Semua", ...categories].map(value => <button className={filter === value ? "active" : "secondary"} key={value} onClick={() => setFilter(value)}>{value}</button>)}</div><button disabled={!state || busy} onClick={() => setEditing({ id: crypto.randomUUID().replaceAll("-", ""), name: "", category: "Nasi", price: 0, available: true, imageUrl: null })}>+ Tambah menu</button></div>
    <div role="status" className="message">{message || (dirty ? "Ada perubahan yang belum disimpan." : "Draf dapat diperiksa sebelum diterbitkan ke kasir.")}</div>
    <section className="product-grid">{items.filter(item => filter === "Semua" || item.category === filter).map(item => <article key={item.id} className="product"><div className={`food food-${item.category.toLowerCase()}`}>{item.imageUrl ? <img src={item.imageUrl} alt={item.name} loading="lazy" referrerPolicy="no-referrer" /> : <span>{({ Nasi: "🍚", Lauk: "🍽️", Sundukan: "🍢", Minuman: "☕" } as Record<string,string>)[item.category]}</span>}<span className="availability">{item.available ? "Tersedia" : "Habis"}</span></div><div className="product-body"><small>{item.category}</small><h2>{item.name}</h2><strong>{rupiah(item.price)}</strong><button className="secondary" disabled={busy} onClick={() => setEditing({ ...item })}>Ubah menu</button></div></article>)}</section>
    {state && items.length === 0 && <section className="empty"><h2>Menu pertama dimulai di sini.</h2><p>Tambahkan nasi, lauk, sundukan, atau minuman. Kasir memakai menu contoh sampai katalog pertama diterbitkan.</p></section>}
    <footer className="publish"><div><strong>{dirty ? "Draf perlu disimpan" : `Draf versi ${state?.draft_version ?? "—"}`}</strong><p>Terbitkan agar perubahan tersedia di laptop kasir.</p></div><button disabled={!dirty || busy || !!editing} className="secondary" onClick={() => void save("save")}>Simpan draf</button><button disabled={!state || dirty || busy || items.length === 0 || !!editing} onClick={() => void save("publish")}>{busy ? "Menyimpan…" : "Terbitkan ke kasir"}</button></footer>
    {editing && <div className="scrim"><section role="dialog" aria-modal="true" aria-labelledby="edit-title" className="editor"><h2 id="edit-title">Detail menu</h2><form onSubmit={event => { event.preventDefault(); setItems(previous => previous.some(item => item.id === editing.id) ? previous.map(item => item.id === editing.id ? editing : item) : [...previous, editing]); setDirty(true); setEditing(null); }}>
      <label>Nama menu<input autoFocus value={editing.name} maxLength={60} required onChange={event => setEditing({ ...editing, name: event.target.value })} /></label><div className="form-row"><label>Kategori<select value={editing.category} onChange={event => setEditing({ ...editing, category: event.target.value })}>{categories.map(value => <option key={value}>{value}</option>)}</select></label><label>Harga (rupiah)<input type="number" min="1" max="1000000000" step="1" required value={editing.price || ""} onChange={event => setEditing({ ...editing, price: Number(event.target.value) })} /></label></div><label>Alamat foto HTTPS (opsional)<input type="url" value={editing.imageUrl ?? ""} onChange={event => setEditing({ ...editing, imageUrl: event.target.value || null })} /></label><label className="check"><input type="checkbox" checked={editing.available} onChange={event => setEditing({ ...editing, available: event.target.checked })} /> Tersedia untuk dijual</label><div className="form-row"><button type="button" className="secondary" onClick={() => setEditing(null)}>Batal</button><button>Terapkan ke draf</button></div></form></section></div>}
  </main>;
}

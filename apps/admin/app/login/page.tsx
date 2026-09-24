"use client";
import { useState } from "react";
export default function Login() {
  const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  return <main className="login"><section className="notice"><p className="eyebrow">SELAMAT DATANG KEMBALI</p><h1>Warung kecil.<br/>Catatan rapi.</h1><p>Masuk untuk mengatur menu dan harga yang tampil di kasir.</p><form onSubmit={async event => {
    event.preventDefault(); setBusy(true); setError(""); const form = new FormData(event.currentTarget);
    try {
      const response = await fetch("/api/auth/login", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email: form.get("email"), password: form.get("password") }) });
      const data = await response.json(); if (!response.ok) throw new Error(data.error); location.assign("/");
    } catch (error) { setError(error instanceof Error ? error.message : "Koneksi belum berhasil."); } finally { setBusy(false); }
  }}><label>Email pengelola<input name="email" type="email" autoComplete="username" required /></label><label>Kata sandi<input name="password" type="password" autoComplete="current-password" required /></label><button disabled={busy}>{busy ? "Sedang masuk…" : "Masuk ke pengelola"}</button><p role="alert">{error}</p></form></section><aside><div className="large-mark">W</div><p>Wedangan & aneka nasi sayur</p><small>Satu tempat untuk menyiapkan menu hari ini.</small></aside></main>;
}

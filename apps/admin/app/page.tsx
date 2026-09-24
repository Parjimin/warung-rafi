import { redirect } from "next/navigation";
import { admin } from "../lib/auth.ts";
import { HttpError } from "../lib/http.ts";
import Catalog from "./catalog.tsx";
export const dynamic = "force-dynamic";
export default async function Home() {
  try { await admin(); }
  catch (error) {
    if (error instanceof HttpError && [401,403].includes(error.status)) redirect("/login");
    return <main><section className="notice"><p className="eyebrow">PERSIAPAN WARUNG</p><h1>Layanan belum terhubung.</h1><p>Lengkapi konfigurasi pengelola dan database sesuai panduan di repositori, lalu muat ulang.</p><a className="button" href="/login">Ke halaman masuk</a></section></main>;
  }
  return <Catalog />;
}

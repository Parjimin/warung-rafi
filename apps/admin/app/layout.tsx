import type { Metadata } from "next";
import "./style.css";
export const metadata: Metadata = { title: "Warung Rafi · Pengelola", description: "Kelola menu Warung Rafi" };
export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="id"><body><header className="topbar"><a className="brand" href="/">W<span>WARUNG RAFI<small>Ruang pengelola</small></span></a><nav><a href="/">Menu & harga</a><a href="/sinkronisasi">Sinkronisasi</a><a href="/keuangan">Keuangan</a><a href="/laporan">Laporan</a></nav><span className="pill">VERSI UJI</span></header>{children}</body></html>;
}

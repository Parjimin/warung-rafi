# Warung Rafi

Aplikasi kasir Windows untuk satu kedai: menu besar, pesanan ditunda, tunai, QRIS statis, dan pencatatan lokal. Web admin/API dirancang untuk Vercel dengan PostgreSQL terkelola.

> **Status: implementasi awal, belum siap dipakai menerima transaksi produksi.** Baca [STATUS.md](docs/STATUS.md) untuk bukti pengujian dan keterbatasan.

**M0 dan M1 selesai. M2 dalam review; M3 menunggu integrasi cloud; M4 selesai untuk software/simulasi.** [Validasi M1](docs/M1-VALIDATION.md): 23 pemeriksaan dasar + 60 pemeriksaan recovery/integritas lulus di CI Windows. Navigasi menyatu di atas, kartu menu 2×2, panel pesanan lebih lega, total/tombol tetap terlihat, nama pelanggan tersimpan otomatis, serta halaman pembayaran dan riwayat lebih rapi. Lihat [perubahan UI M2](docs/M2-UI-REVIEW.md). Kenyamanan dan respons preview baru perlu dicoba pada laptop pengguna. Uji touchscreen, printer fisik, dan akun merchant asli dijadwalkan pada M6; pengembangan fungsi lain tetap berjalan.

M3 menambahkan unggah foto menu, pemantauan antrean/versi katalog laptop, serta penanganan konflik draf. Lihat [alur, setup dan batas M3](docs/M3-ADMIN-SYNC.md). Deployment Supabase/Vercel dan uji cloud nyata belum dilakukan.

M4 memperkuat inbox QRIS, memisahkan polling pembayaran, dan menambahkan mode simulasi berlabel dengan database terpisah. [Panduan M4](docs/M4-QRIS.md). CI Windows, web dan PostgreSQL lulus. Total 219 pemeriksaan Windows. **M5 aktif:** sesi kas, pengembalian berizin dan jurnal sinkronisasi. [Panduan M5 tahap 1](docs/M5-CASH-REFUNDS.md). Tahap kas/refund lulus 306 pemeriksaan Windows, tes admin/API/browser dan PostgreSQL 17. Tombol kas tetap terlihat di layar kecil. [M5 tahap 2](docs/M5-RECONCILIATION.md) menambahkan dashboard keuangan, pencocokan QRIS eksplisit, estimasi/biaya aktual, laporan pencairan dan konfirmasi mutasi bank dengan audit permanen. Software tahap 2 terverifikasi. [M5 tahap 3](docs/M5-REPORTS-SHEETS.md) menambahkan laporan tetap per sesi/tanggal WIB, 14 bagian laporan, CSV dan antrean Google Sheets dengan retry serta verifikasi baca ulang. Implementasi diuji dengan layanan tiruan; akun Google nyata dan scheduler belum terhubung.

## Pantau progres

- [Milestone dan kriteria selesai](docs/MILESTONES.md)
- [Status implementasi dan hambatan](docs/STATUS.md)
- [Blueprint fungsional lengkap](docs/BLUEPRINT.md)
- [Backlog terstruktur](.github/planning.json)
- [Cara menjalankan](docs/DEVELOPMENT.md)
- [Keputusan arsitektur](docs/ARCHITECTURE.md)

Workflow **Sync project tracking** membuat/memperbarui GitHub Milestones dan Issues berdasarkan `.github/planning.json` saat planning diperbarui di `main` atau dijalankan manual di tab Actions. Ia tidak memberikan tanggal penyelesaian fiktif atau menutup issue sebelum kriterianya diverifikasi.

## Struktur

| Lokasi | Isi |
| --- | --- |
| `apps/desktop/WarungRafi.Desktop` | UI WPF Windows, cetak, popup pasif |
| `src/WarungRafi.Core` | Aturan pesanan dan perhitungan uang |
| `src/WarungRafi.Storage` | SQLite, pembayaran, outbox, inbox notifikasi |
| `tests/WarungRafi.Checks` | Pengujian domain dan integritas penyimpanan |
| `tests/WarungRafi.RecoveryChecks` | Penghentian proses, kapasitas SQLite, konkurensi, dan antrean besar |
| `tests/WarungRafi.FinanceChecks` | Sesi kas, refund, PIN, rollback jurnal dan migrasi |
| `tests/WarungRafi.PaymentChecks` | Inbox QRIS, cursor atomik, deduplikasi dan kesegaran notifikasi |
| `tests/WarungRafi.SyncChecks` | Transport HTTP, acknowledgement dan cache foto offline Windows |
| `tests/WarungRafi.UiChecks` | Layout WPF, alur kasir, dan screenshot review di Windows |
| `apps/admin` | Next.js admin/API serta pengujian kontrak webhook |
| `database` | Migrasi PostgreSQL untuk katalog, sinkronisasi, dan pembayaran |
| `docs` | Blueprint, milestone, status, petunjuk |

## Coba paket preview Windows

Buka [preview M5 laporan dan keuangan](https://github.com/Parjimin/warung-rafi/actions/runs/36244233885), lalu unduh artifact **WarungRafi-Windows-preview**. Screenshot kasir ada pada **WarungRafi-UI-review**; screenshot admin pada **WarungRafi-M3-admin-review**, **WarungRafi-M5-finance-review** dan **WarungRafi-M5-reports-review**. Run yang lebih baru dapat dilihat di [Actions](https://github.com/Parjimin/warung-rafi/actions/workflows/ci.yml). Ekstrak ZIP dan jalankan `WarungRafi.exe`. Untuk simulasi popup/suara QRIS tanpa merchant, buka `Coba-QRIS.cmd`; data simulasi terpisah dari data kasir normal. Paket ini untuk mencoba menu dummy dan alur kasir; belum untuk transaksi usaha nyata. Printer dan layanan online perlu dikonfigurasi mengikuti panduan.

## Mulai di Windows

Pasang .NET 10 SDK, lalu dari folder repo:

```powershell
dotnet run --project tests/WarungRafi.Checks
dotnet run --project apps/desktop/WarungRafi.Desktop
```

Data lokal disimpan di `%LOCALAPPDATA%\WarungRafi\warung-rafi.db`. Tidak ada data produksi atau kunci pembayaran di repository. Menu dan harga awal adalah dummy.

## Web admin

```bash
cd apps/admin
npm ci
cp .env.example .env.local
npm run dev
```

Tanpa konfigurasi layanan, dashboard menampilkan status belum dikonfigurasi. Endpoint pembayaran tidak berpura-pura berhasil menyimpan transaksi.

## Prinsip pembayaran

Popup QRIS hanya berisi **Pembayaran QRIS diterima**, nominal, dan jam. Tidak ada tombol pencocokan dan tidak mengambil fokus. Bukti Midtrans, pencatatan pembayaran oleh kasir, serta pencairan rekening merupakan catatan terpisah. Kesamaan nominal tidak otomatis melunasi pesanan.

## Repositori dan progres

- [Milestones](https://github.com/Parjimin/warung-rafi/milestones)
- [Issues](https://github.com/Parjimin/warung-rafi/issues)
- [Build dan paket preview](https://github.com/Parjimin/warung-rafi/actions)

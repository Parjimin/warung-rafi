# Warung Rafi

Aplikasi kasir Windows untuk satu kedai: menu besar, pesanan ditunda, tunai, QRIS statis, dan pencatatan lokal. Web admin/API dirancang untuk Vercel dengan PostgreSQL terkelola.

> **Status: implementasi awal, belum siap dipakai menerima transaksi produksi.** Baca [STATUS.md](docs/STATUS.md) untuk bukti pengujian dan keterbatasan.

**M0 dan M1 selesai. M2 dalam review; M3 aktif: admin dan sinkronisasi.** [Validasi M1](docs/M1-VALIDATION.md): 23 pemeriksaan dasar + 60 pemeriksaan recovery/integritas lulus di CI Windows. Navigasi menyatu di atas, kartu menu 2×2, panel pesanan lebih lega, total/tombol tetap terlihat, nama pelanggan tersimpan otomatis, serta halaman pembayaran dan riwayat lebih rapi. Lihat [perubahan UI M2](docs/M2-UI-REVIEW.md). Kenyamanan dan respons preview baru perlu dicoba pada laptop pengguna. Uji touchscreen, printer fisik, dan akun merchant asli dijadwalkan pada M6; pengembangan fungsi lain tetap berjalan.

M3 menambahkan unggah foto menu, pemantauan antrean/versi katalog laptop, serta penanganan konflik draf. Lihat [alur, setup dan batas M3](docs/M3-ADMIN-SYNC.md). Deployment Supabase/Vercel dan uji cloud nyata belum dilakukan.

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
| `tests/WarungRafi.SyncChecks` | Transport HTTP, acknowledgement dan cache foto offline Windows |
| `tests/WarungRafi.UiChecks` | Layout WPF, alur kasir, dan screenshot review di Windows |
| `apps/admin` | Next.js admin/API serta pengujian kontrak webhook |
| `database` | Migrasi PostgreSQL untuk katalog, sinkronisasi, dan pembayaran |
| `docs` | Blueprint, milestone, status, petunjuk |

## Coba paket preview Windows

Buka [preview M3 yang sudah lulus](https://github.com/Parjimin/warung-rafi/actions/runs/36111996368), lalu unduh artifact **WarungRafi-Windows-preview**. Screenshot kasir ada pada **WarungRafi-UI-review**; screenshot admin pada **WarungRafi-M3-admin-review**. Run yang lebih baru dapat dilihat di [Actions](https://github.com/Parjimin/warung-rafi/actions/workflows/ci.yml). Ekstrak ZIP dan jalankan `WarungRafi.exe`. Paket ini untuk mencoba menu dummy dan alur kasir; belum untuk transaksi usaha nyata. Printer dan layanan online perlu dikonfigurasi mengikuti panduan.

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

# Warung Rafi

Aplikasi kasir Windows untuk satu kedai: menu besar, pesanan ditunda, tunai, QRIS statis, dan pencatatan lokal. Web admin/API dirancang untuk Vercel dengan PostgreSQL terkelola.

> **Status: implementasi awal, belum siap dipakai menerima transaksi produksi.** Baca [STATUS.md](docs/STATUS.md) untuk bukti pengujian dan keterbatasan.

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
| `apps/admin` | Next.js admin/API serta pengujian kontrak webhook |
| `database` | Migrasi PostgreSQL untuk katalog, sinkronisasi, dan pembayaran |
| `docs` | Blueprint, milestone, status, petunjuk |

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
npm install
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

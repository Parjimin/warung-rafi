# Milestone pengembangan

Progres dinilai berdasarkan hasil yang dapat diperiksa. **Kode ditulis tidak sama dengan fitur sudah lolos uji pada Windows, printer, atau akun merchant.** Tidak ada estimasi tanggal sampai ketersediaan akun/perangkat dan kapasitas pengerjaan disepakati.

| Milestone | Hasil | Syarat selesai | Dependensi |
| --- | --- | --- | --- |
| M0 — Fondasi dan pelacakan | Blueprint, struktur repo, backlog, CI | Dokumen konsisten, workflow berjalan di repo | Akses GitHub |
| M1 — Domain dan data lokal | Pesanan, snapshot harga, tunai, ditunda, SQLite, outbox | Uji integritas dan recovery lulus | .NET SDK |
| M2 — Kasir Windows | UI sentuh, keranjang, pembayaran, riwayat, cetak 58 mm | Build Windows lulus; kasir dan printer fisik diuji | M1, laptop/printer |
| M3 — Admin dan sinkronisasi | Login, CRUD katalog, publikasi, penerimaan penjualan | Hak akses, idempotensi, konflik, offline diuji | Database dan hosting |
| M4 — QRIS statis | Webhook terverifikasi, inbox, suara, popup pasif | Tidak ada salah pelunasan/duplikasi; akun merchant diuji | M3, Midtrans aktif |
| M5 — Keuangan dan laporan | Sesi kas, refund, biaya aktual, pencairan, Sheets | Rekonsiliasi dan total laporan lulus | M1, M3, M4 |
| M6 — Rilis dan pemulihan | Installer, backup/restore, hardening, panduan | UAT penjual, restore, paket hosting, kredensial produksi selesai | Seluruh milestone inti |

## Aturan status

- **Planned**: belum diimplementasikan.
- **In progress**: sudah ada implementasi sebagian; kriteria akhir belum selesai.
- **Blocked**: membutuhkan akses/perangkat/keputusan yang belum tersedia.
- **Done**: kriteria penerimaan dan bukti uji tersedia, bukan sekadar file dibuat.

Lihat `STATUS.md` untuk keadaan terukur terkini. GitHub Issues akan memakai satu issue per hasil kerja penting dan memiliki checklist penerimaan. Milestone tidak ditutup otomatis hanya karena beberapa file di-push.

# Milestone pengembangan

Progres dinilai berdasarkan hasil yang dapat diperiksa. **Kode ditulis tidak sama dengan fitur sudah lolos uji pada Windows, printer, atau akun merchant.** Tidak ada estimasi tanggal sampai ketersediaan akun/perangkat dan kapasitas pengerjaan disepakati.

| Milestone | Hasil | Syarat selesai | Dependensi |
| --- | --- | --- | --- |
| M0 — Fondasi dan pelacakan | Blueprint, struktur repo, backlog, CI | Dokumen konsisten, workflow berjalan di repo | Akses GitHub |
| M1 — Domain dan data lokal | Pesanan, snapshot harga, tunai, ditunda, SQLite, outbox | Uji integritas dan recovery lulus | .NET SDK |
| M2 — Kasir Windows | Perapian UI/UX, keranjang, pembayaran, riwayat, layout struk | Build lulus; alur nyaman dengan mouse/touchpad; tombol dan teks jelas; respons cepat terjaga | M1, laptop Windows yang tersedia |
| M3 — Admin dan sinkronisasi | Login, CRUD katalog, publikasi, penerimaan penjualan | Hak akses, idempotensi, konflik, offline diuji | Database dan hosting |
| M4 — QRIS statis | Webhook terverifikasi, inbox, suara, popup pasif | Verifikasi, deduplikasi, kegagalan, dan popup lulus dengan mock/fixture berlabel uji; jalur produksi tetap fail closed | M3, lingkungan uji perangkat lunak |
| M5 — Keuangan dan laporan | Sesi kas, refund, biaya aktual, pencairan, Sheets | Rekonsiliasi dan total laporan lulus | M1, M3, M4 |
| M6 — Rilis dan pemulihan | Finalisasi touchscreen, printer, merchant; installer, backup/restore, hardening, panduan | Uji perangkat dan akun asli, UAT penjual, restore, hosting dan kredensial produksi selesai | Milestone inti; perangkat dan pemilik tersedia pada tahap finalisasi |

## Checkpoint — 26 September 2026

**M0 dan M1 selesai; M2 dalam review laptop; M3 menunggu integrasi cloud; M4 selesai untuk software/simulasi.** M1 lulus 23 pemeriksaan dasar dan 60 pemeriksaan recovery/integritas di CI Windows. [Bukti dan batas pengujian](M1-VALIDATION.md) tersedia. [Perapian UI M2](M2-UI-REVIEW.md) sudah diimplementasikan; review kenyamanan klik pada laptop pengguna masih terbuka. [M3](M3-ADMIN-SYNC.md) melengkapi unggah foto, konflik draf, laporan perangkat dan verifikasi sinkronisasi/cache offline. Konfigurasi dan uji cloud nyata masih terbuka. [M4](M4-QRIS.md) lulus 219 pemeriksaan Windows, unit/API/browser web dan PostgreSQL. Pengujian merchant nyata tetap M6. **M5 aktif:** tahap sesi kas, refund berizin, jurnal dan sinkronisasi lulus 306 pemeriksaan Windows, admin/API/browser dan PostgreSQL 17 pada commit `58ba711`. Revisi migrasi SQL dan layout layar kecil terverifikasi; screenshot telah ditinjau. [Panduan tahap 1](M5-CASH-REFUNDS.md). [Tahap 2](M5-RECONCILIATION.md): dashboard, pencocokan bukti/pesanan, estimasi dan biaya aktual, pencairan serta mutasi bank lulus pengujian software. Google Sheets tetap terbuka.

## Penyesuaian urutan — 24 September 2026

Pengguna menguji preview pada laptop **non-touchscreen** dan menyatakan kecepatannya sudah memuaskan. Ini merupakan umpan balik penggunaan, belum pengukuran latensi. UI/UX masih versi awal; prioritas berikutnya adalah kenyamanan klik, area dan jarak tombol, keterbacaan, posisi aksi utama, serta konsistensi alur. Kecepatan tersebut dipertahankan saat tampilan dirapikan.

- Pengujian touchscreen nyata dipindahkan ke M6 dan checklist finalisasi #12. Pengujian mouse/touchpad dan keyboard dasar tetap berjalan pada M2.
- Printer belum tersedia. Issue #5 dipindahkan dari M2 ke M6. Desain struk dan pengujian kegagalan pada tingkat perangkat lunak tetap dapat dikerjakan.
- Akun merchant belum dibuat karena pemilik belum tersedia. Issue #9 dipindahkan dari M4 ke M6 untuk onboarding, aktivasi, dan pengujian akun asli. Logika pembayaran, inbox, dan popup tetap dikembangkan menggunakan mock/fixture berlabel uji pada M4.
- Ketiga kebutuhan itu **ditunda terencana sampai finalisasi**, sehingga tidak menjadi penghalang pengerjaan fitur lain. Pengujian aslinya belum dinyatakan lulus dan tetap diselesaikan sebelum pemakaian produksi.

## Aturan status

- **Planned**: belum diimplementasikan.
- **In progress**: sudah ada implementasi sebagian; kriteria akhir belum selesai.
- **Blocked**: membutuhkan akses/perangkat/keputusan yang belum tersedia.
- **Done**: kriteria penerimaan dan bukti uji tersedia, bukan sekadar file dibuat.

Lihat `STATUS.md` untuk keadaan terukur terkini. GitHub Issues akan memakai satu issue per hasil kerja penting dan memiliki checklist penerimaan. Milestone tidak ditutup otomatis hanya karena beberapa file di-push.

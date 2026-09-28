# Audit kesiapan produksi — 28 September 2026

Status: **BELUM DISETUJUI UNTUK PRODUKSI**. Pemeriksaan kode dan CI tidak menggantikan pemasangan serta UAT akun/perangkat nyata. Dokumen ini tidak mengesahkan transaksi uang nyata, merge, pembelian, atau deployment produksi.

## Sumber dan cakupan

Baseline `8d73edb6828128b3e334cce87647e5c429c788e4`, cabang `codex/m6-release-tools`, PR #21 masih draft dan belum merged saat diperiksa. Audit dilanjutkan pada `codex/production-audit`; tidak ditemukan AGENTS.md dalam tree baseline. Cabang main bukan sumber M6 pada checkpoint ini.

Review meliputi domain pesanan/pembayaran, SQLite/outbox/jurnal kas/refund, recovery dan arsip backup, transport sinkronisasi, pengaturan/PIN, alur kasir/printer/foto, route admin/perangkat/webhook/runner, autentikasi dan batas request, rekonsiliasi/CSV/Sheets, migrasi 001–006 dan hak akses, installer/update/uninstall, dependency serta workflow pengujian. Ini review kode dan pengujian skenario terpilih, bukan sertifikasi keamanan atau jaminan seluruh cacat sudah ditemukan.

## Temuan dan perubahan

| ID | Temuan | Dampak | Perbaikan / bukti regresi |
| --- | --- | --- | --- |
| A01 | Transport mengirim 50 snapshot tanpa memperhatikan batas HTTP 256.000 byte | Antrean pesanan panjang setelah offline terus menerima 413 dan tidak maju | Batasi kiriman ke awalan antrean yang muat dalam batas byte; acknowledgement hanya berlaku untuk event yang benar-benar dikirim. SyncChecks mengirim 50 versi pesanan 300 baris dengan nama multibyte, memeriksa urutan lengkap dan acknowledgement palsu untuk event belum terkirim. |
| A02 | Kunci backend Supabase selalu digunakan sebagai Bearer JWT | Setup dengan `sb_secret_...` tidak kompatibel dengan kontrak kunci modern | REST/Storage mengirim kunci modern hanya melalui `apikey`; JWT legacy tetap didukung. Kunci publishable ditolak sebagai kunci backend; redirect REST/Storage ditolak. Tes unit memeriksa header dan tes server hasil build memeriksa login, database serta unggah foto untuk pasangan kunci modern melalui fixture ketat. |
| A03 | Ringkasan harian lokal menyaring waktu pembaruan order | Transaksi dengan waktu bayar sebelum tengah malam tetapi commit sesudahnya dapat berbeda hari dari laporan cloud | Gunakan `Payment.PaidAt` dengan perbandingan DateTimeOffset; tes melintasi satu tick sebelum tengah malam WIB. Tidak mengubah transaksi tersimpan atau schema. |
| A04 | Bagian utama DEVELOPMENT masih mencantumkan migrasi sampai 005 | Setup baru bisa melewatkan limiter login 006 | Selaraskan urutan migrasi setup dengan M6 dan jelaskan pemetaan key modern/legacy. |

Ringkasan harian lokal sekarang membaca pembayaran selesai untuk menyaring waktu bayar secara tepat, termasuk offset historis; biaya pembacaan bertambah seiring jumlah transaksi. Indeks/proyeksi tanggal khusus dapat ditambahkan dalam migrasi terpisah bila pengukuran pada data operasional menunjukkan kebutuhan. Tidak ada reset database atau perubahan riwayat.

## Verifikasi sesi ini

- Baseline: 32 unit admin, typecheck, production build, 21 hasil Node API integration dan `npm audit --omit=dev --audit-level=high` lulus (0 kerentanan dilaporkan).
- Perubahan pada commit `12b78c0420121f338f304460111332bf1f35880c`: dua run CI lengkap lulus, [push 36451037522](https://github.com/Parjimin/warung-rafi/actions/runs/36451037522) dan [PR 36451087084](https://github.com/Parjimin/warung-rafi/actions/runs/36451087084). Keduanya meluluskan job admin, database dan desktop. Pembaruan laporan sesudah commit ini hanya mendokumentasikan bukti tersebut.
- Admin: 33 unit, 22 hasil Node API integration, typecheck, production build serta tiga alur browser (katalog/monitor, rekonsiliasi, laporan/Sheets retry) lulus. Audit npm melaporkan 0 kerentanan.
- Windows: 386 pemeriksaan lulus: domain 23, recovery 61, payment 31, finance 50, backup 34, sync/cache 32, WPF 116, operations/security 27, installer/update 8, smoke paket terpasang 4. Build WPF, audit dependensi .NET, packaging dan startup dengan bundled runtime lulus. Paket unsigned tersedia pada artefak run.
- Database CI: migrasi 001–006 dan tujuh suite SQL lulus pada PostgreSQL 17 dengan simulasi role managed database.
- SQL lokal: migrasi 001–006 serta tujuh suite (`operations`, `assertions`, `sync_monitor`, `payments`, `finance`, `reconciliation`, `reports`) lulus di PostgreSQL 18.3 WASM / PGlite 0.5.8. Directive psql `\\copy` hanya diganti dengan insert fixture JSON identik dalam runner sementara. Ini bukan bukti proyek Supabase nyata atau PostgreSQL 17 CI.
- Percobaan .NET lokal: SDK 10.0.401 tersedia, tetapi build MSBuild berhenti pada project-reference evaluation tanpa diagnostik compiler (0 error terlapor, exit gagal). Tidak dihitung lulus.
- Percobaan browser lokal: unduhan Chromium Playwright gagal karena arsip unduhan tidak valid. Tidak dihitung lulus lokal; bukti Windows dan browser berasal dari CI di atas.

Tidak ada akun merchant/cloud pengguna, credential produksi, printer, speaker atau laptop touchscreen yang diakses dalam audit ini.

## Syarat produksi yang masih terbuka

| Syarat | Bukti yang diperlukan sebelum disetujui |
| --- | --- |
| Rilis yang akan dipasang | CI Windows/admin/PostgreSQL 17 lulus untuk commit yang persis akan dirilis; installer dari run tersebut. Paket masih unsigned. |
| Supabase dan Vercel UJI | Migrasi 001–006, bucket foto, Auth allowlist, konfigurasi key, origin, token perangkat; login dan koneksi nyata berhasil. Jangan mengulang migrasi awal pada database terisi. |
| Sinkronisasi nyata | Menu/foto diterbitkan dan diterima; tunai offline, restart, reconnect, backlog dan retry cocok dengan cloud tanpa duplikasi; antrean seluruh jurnal kosong. |
| Google Sheets | Workbook Restricted dibagikan kepada service account yang tepat; ekspor manual dan retry cocok seluruh sel; scheduler membuktikan job antrean terproses. Scheduler tidak membuat laporan harian sendiri. |
| QRIS merchant | Aktivasi static QRIS Midtrans, konsistensi merchant/environment/key, webhook nyata tervalidasi, nominal sama tidak melunasi otomatis, refund/rekonsiliasi dan pencairan dibedakan. Uji uang nyata hanya dengan mandat nominal pemilik. |
| Rekening dan biaya | SeaBank diterima merchant; biaya/MDR, pembulatan dan jadwal pencairan berdasarkan akun/laporan aktual. |
| Windows dan printer | Versi OS/DPI/resolusi tercatat, printer OKAY 58D test page dan struk fisik, kertas habis/cabut kabel/cetak ulang, suara dan sentuhan; update/reinstall mempertahankan data. |
| Backup dan pemulihan | Backup terbaru di media lain, kata sandi tersimpan aman, restore UJI termasuk putus koneksi/konflik cloud dan laptop pengganti; cocokkan nominal dan riwayat. |
| Identitas dan menu final | Konfirmasi nama/alamat struk yang masih tertanam dalam Receipt.cs; harga/foto/menu dummy diganti sebelum berjualan. |
| Lingkungan produksi bersih | Pisahkan transaksi latihan, akun/key/workbook dan data usaha; jangan menghapus ledger atau memulihkan backup UJI ke produksi. Paket hosting harus mengizinkan bisnis. |
| Persetujuan pemilik | UAT alur jualan, buka/tutup kas, pengembalian, kehilangan koneksi, prosedur gangguan dan penanggung jawab operasional. |

## Rujukan perubahan kompatibilitas

Dokumentasi resmi Supabase diperiksa pada sesi audit: https://supabase.com/docs/guides/getting-started/api-keys — kunci publishable/secret bukan JWT; gunakan header `apikey`. Dukungan request ini telah diuji memakai fixture, tetapi konfigurasi proyek Supabase pengguna tetap wajib diuji nyata.

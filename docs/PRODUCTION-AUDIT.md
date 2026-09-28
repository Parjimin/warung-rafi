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

Pada audit kode awal tidak ada akun cloud pengguna yang diakses. Pemeriksaan lingkungan UJI berikut dilakukan setelah pemilik memberikan akses; tidak ada transaksi, migrasi, atau konfigurasi cloud yang diubah. Akun merchant, printer, speaker dan laptop touchscreen belum diuji.

## Pemeriksaan lingkungan UJI — 28 September 2026, lanjutan

- Situs UJI merespons HTTPS dan mengarahkan root ke login. Empat GET API admin (catalog, devices, finance, reports) tanpa sesi mengembalikan 401; GET katalog perangkat tanpa token juga 401.
- Login melalui browser menampilkan `Asal permintaan tidak diizinkan.` Kode memeriksa kesamaan persis header Origin dengan `APP_ORIGIN` sebelum memanggil Supabase Auth. Periksa environment deployment aktif: origin harus sama persis dengan domain HTTPS yang dibuka, tanpa trailing slash. Nilai environment aktual belum dapat dibaca; password belum tervalidasi melalui aplikasi.
- GET `/api/device/setup` pada deployment mengembalikan 404, sedangkan kode M6 yang diaudit menyediakan route GET tersebut. Ini menunjukkan ketidaksesuaian deployment/routing; SHA deployment aktif belum diketahui. Pastikan root project `apps/admin`, sumber rilis yang tepat, kemudian deploy ulang lingkungan UJI dan periksa endpoint kembali.
- Pembacaan API Supabase berhasil. UUID akun pengelola cocok dengan email yang diberikan pemilik, dan email sudah terkonfirmasi. Metadata REST menampilkan fungsi `reserve_login_attempt`, `device_recovery_page`, `device_setup` serta tabel/fungsi keuangan dan laporan. Keberadaan metadata bukan pembuktian isi definisi migrasi, RLS, atau keberhasilan semua operasi.
- Bucket `menu-photos` tersedia, public, batas 3.000.000 byte, MIME `image/jpeg`. Unggah foto belum diuji melalui aplikasi.
- Kunci backend dan password dibagikan melalui chat oleh pemilik: rotasi sebelum produksi, perbarui konfigurasi server yang bergantung padanya, lalu uji ulang. Nilai credential tidak disimpan dalam repository atau laporan ini.
- Belum dilakukan perubahan environment, deployment, data transaksi atau percobaan login berulang. Diperlukan akses pengaturan hosting atau perbaikan oleh pemilik untuk melanjutkan uji admin.

## Syarat produksi yang masih terbuka

### Hasil pemeriksaan ulang setelah redeploy pemilik

Pada 28 September 2026 sekitar 23.58–23.59 WIB, dua blocker web sebelumnya sudah teratasi secara perilaku: POST login dengan Origin yang benar dan body kosong menghasilkan 400 validasi input, bukan 403 origin; GET setup perangkat tanpa token menghasilkan 401, bukan 404. Login browser akun pengelola berhasil. Halaman katalog, sinkronisasi, keuangan dan laporan berhasil dimuat dengan sesi tersebut. Ini tidak mengidentifikasi SHA deployment atau membuktikan seluruh perbaikan PR #22 telah dipasang.

Keadaan UJI yang terlihat: katalog berisi 0 menu, versi terbit 0; belum ada laporan laptop; ringkasan keuangan periode yang dibuka kosong dan memperingatkan data laptop belum lengkap; belum ada salinan laporan; halaman laporan menyatakan Google Sheets belum terhubung. Pemeriksaan ini hanya membaca halaman, tidak menerbitkan menu, membuat laporan, mengubah transaksi, atau mengaktifkan integrasi. Langkah berikutnya adalah menu final, pemasangan/koneksi kasir, serta konfigurasi Sheets sebelum UAT transaksi dan perangkat.

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

## Audit lanjutan — 29 September 2026

- Seluruh 20 tabel aplikasi yang diperiksa lewat REST menggunakan role anon menolak SELECT (HTTP 401, PostgreSQL 42501): catalog_state, order_snapshots, device_events, device_sync_status, provider_payments, payment_notifications, finance_events, cash_sessions, refunds, finance_admin_events, finance_admin_state, qris_costs, qris_fee_profiles, qris_matches, qris_payouts, qris_payout_items, report_jobs, report_attempts, login_windows, audit_log. Request memakai limit=0 dan tidak membaca isi transaksi. Ini membuktikan penolakan SELECT anon pada jalur tersebut, bukan seluruh hak akses semua role/operasi.
- Deployment menolak API admin tanpa sesi, recovery/payment/sync/finance perangkat tanpa token, serta runner ekspor tanpa token (401). Origin asing pada login ditolak (403). Pengujian tidak mengirim credential palsu atau membuat transaksi.
- Temuan A05: endpoint setup sebelumnya menandai QRIS/Sheets terkonfigurasi hanya dari keberadaan nilai environment. Perbaikan memvalidasi lingkungan Midtrans dan memakai validator akun layanan/ID spreadsheet yang sama dengan exporter. Nilai terisi tetapi tidak valid tidak lagi dilaporkan siap.
- Implementasi tambahan: panel Kesiapan layanan pada Sinkronisasi dan GET `/api/admin/readiness`, khusus pengelola, no-store. Memeriksa RPC setup database, format origin, konfigurasi perangkat, QRIS, Sheets, runner; identitas commit hanya ditampilkan bila hosting menyediakan SHA valid. Tidak mengembalikan nilai key, token, email akun layanan, ID spreadsheet, atau error provider. Status konfigurasi selalu dibedakan dari koneksi yang telah diuji; tidak ada label keseluruhan 'aman/siap produksi'.
- Verifikasi lokal perubahan: 33 unit, typecheck, build dan 24 hasil API integration lulus; tambahan tes meliputi penolakan anon/perangkat/non-owner, cache, redaksi, database gagal, dan konfigurasi cacat. Build awal mengalami cache Turbopack rusak; build bersih berhasil. CI/browser untuk perubahan ini perlu ditautkan setelah selesai.
- Masih memerlukan data/akses nyata: menu dan harga final, laptop kasir, printer, konfigurasi dan hak akses workbook Google, aktivasi merchant/webhook QRIS, backup/UAT, rotasi credential yang dibagikan, serta kecocokan commit deployment dengan hasil CI. Audit tidak mengisi menu fiktif, membuat transaksi uang nyata, atau menganggap credential provider tersedia.

## Rujukan perubahan kompatibilitas

Dokumentasi resmi Supabase diperiksa pada sesi audit: https://supabase.com/docs/guides/getting-started/api-keys — kunci publishable/secret bukan JWT; gunakan header `apikey`. Dukungan request ini telah diuji memakai fixture, tetapi konfigurasi proyek Supabase pengguna tetap wajib diuji nyata.

# Status implementasi — checkpoint 26 September 2026

**M5 tahap laporan/CSV/Sheets sudah terverifikasi pada tingkat software.** [CI `5c2b7f7`](https://github.com/Parjimin/warung-rafi/actions/runs/36244233885) lulus **306 pemeriksaan Windows**, 32 tes unit admin, 14 skenario API (16 hasil termasuk pembungkus), tiga alur browser dan enam suite SQL PostgreSQL 17. Laporan tetap per sesi atau tanggal WIB memuat 14 bagian, CSV dan antrean Sheets dengan retry serta verifikasi baca ulang. [Panduan tahap 1](M5-CASH-REFUNDS.md) · [Tahap 2](M5-RECONCILIATION.md) · [Tahap 3 dan screenshot](M5-REPORTS-SHEETS.md). **M5 tetap terbuka untuk koneksi Google nyata dan scheduler.** M2 menunggu review laptop, M3 menunggu integrasi cloud, dan uji merchant/perangkat tetap M6.

| Bagian | Implementasi tersedia | Verifikasi / batas saat checkpoint |
| --- | --- | --- |
| M0 — selesai | Blueprint, keputusan arsitektur, M0–M6, backlog, workflow CI dan pelacakan | 7 milestone dan 12 issue berhasil dibuat; workflow tracking lulus |
| M1 — selesai | Aturan pesanan, harga snapshot, tunai, ditunda, SQLite, optimistic concurrency, outbox atomik | 23 pemeriksaan dasar + 60 pemeriksaan recovery/integritas lulus di CI Windows: process-kill, SQLITE_FULL, konkurensi, antrean >1.000, dan batas tanggal WIB |
| M2 — review laptop | Satu bar navigasi, kartu mendatar 2×2, panel pesanan fleksibel/dapat diperbesar, tombol sejajar, pencarian, nama otomatis tersimpan, pembayaran/riwayat/kas dipoles, animasi singkat | 69 pemeriksaan layout/interaksi WPF lulus di CI Windows; screenshot aktual ditinjau; kenyamanan klik serta respons preview baru pada laptop pengguna masih perlu direview; touchscreen/printer tetap M6 |
| M3 — aktif | Login, tambah/edit menu, unggah/normalisasi foto, konflik draf, publikasi, monitor antrean/versi/konflik perangkat, cache katalog/foto offline | 13 tes unit admin, 5 skenario API melalui Next hasil build, alur browser, 15 pemeriksaan sync/cache Windows dan SQL lulus; setup dan uji Supabase/Storage/Vercel nyata masih terbuka |
| M4 — selesai (software) | Polling terpisah, validasi inbox atomik, deduplikasi ketat, kebijakan popup 60 detik, simulasi berlabel dengan database terpisah | 31 pemeriksaan inbox SQLite, 84 layout/interaksi WPF, 21 sinkronisasi/cache, 14 unit admin dan 11 skenario API lulus; SQL, build/publish Windows serta screenshot terverifikasi. Merchant nyata tetap M6. [Rincian M4](M4-QRIS.md) |
| M5 — aktif | Kas/refund, rekonsiliasi, biaya/pencairan/mutasi bank, audit, snapshot laporan per sesi/tanggal, CSV dan antrean Sheets | 306 Windows, 32 unit admin, 14 skenario API, tiga alur browser dan enam suite PostgreSQL 17 lulus. Screenshot ditinjau. Koneksi Google nyata/scheduler belum diaktifkan; biaya/pencairan masih manual berreferensi |
| M6 | Panduan setup dan paket preview melalui CI | Finalisasi touchscreen, printer, merchant, installer, backup/restore, perlindungan token, dan UAT masih terbuka |

## Pengerjaan per milestone

M0 dan M1 selesai. [Pengujian integritas dan pemulihan M1](M1-VALIDATION.md) lulus di CI Windows pada commit `6fe719b`. **M4: QRIS selesai pada lingkup software/simulasi**, dengan [alur, simulasi dan batas verifikasi](M4-QRIS.md). [Alur dan setup M3](M3-ADMIN-SYNC.md) tersedia. Review laptop untuk [M2](M2-UI-REVIEW.md) tetap terbuka. Kriteria integrasi cloud M3 masih terbuka. M5 tahap pertama menyelesaikan sesi kas, jurnal dan refund. Tahap kedua menyelesaikan rekonsiliasi, biaya, pencairan dan dashboard pada tingkat software. Tahap ketiga menyelesaikan snapshot laporan, CSV dan ekspor Sheets pada tingkat software. Koneksi Google nyata serta scheduler masih perlu dipasang dan diuji. Touchscreen, printer, dan merchant asli tetap pada M6.

## Umpan balik pengguna dan prioritas berikutnya

Pengguna melaporkan versi awal terasa cepat di laptop non-touchscreen, tetapi daftar pesanan terjepit dan beberapa kontrol kurang nyaman diklik. Screenshot menunjukkan header serta tombol vertikal memakan ruang, ditambah masalah kontras pada tombol hijau.

Revisi kedua M2 menyatukan brand/navigasi/tanggal dalam satu bar atas, menyeimbangkan empat kartu menjadi 2×2, membedakan ilustrasi hidangan, dan merapikan posisi subtotal/kontrol jumlah. Preview M2 mengurangi tinggi header, memberikan area fleksibel pada daftar pesanan, menempatkan total dan aksi tetap di bawah, serta menyediakan Perbesar. Katalog, pembayaran, ditunda, riwayat, dan kas memakai gaya konsisten. Nama pelanggan tersimpan otomatis. Animasi tekan/fade singkat tidak menunda aksi penyimpanan. Masih perlu review pemakaian pada laptop pengguna sebelum M2 ditutup; respons cepat belum dibuktikan melalui benchmark formal.

Uji touchscreen, printer OKAY 58D, serta pembuatan/aktivasi dan uji akun merchant asli **dijadwalkan pada M6**. Printer belum tersedia; pemilik belum sempat menyiapkan merchant. Pengembangan fitur lain tetap lanjut. M4 dapat menggunakan mock/fixture berlabel uji, tanpa menganggap transaksi simulasi sebagai pembayaran nyata.

## Bukti pengujian

Bukti M5 tahap 3: [CI 5c2b7f7](https://github.com/Parjimin/warung-rafi/actions/runs/36244233885), seluruh job lulus tanpa retry. Windows tetap **306 pemeriksaan** (23 dasar + 60 recovery + 31 inbox + 50 kas/refund + 26 sync/cache + 116 UI). Admin: **32 unit**, **14 skenario API** (16 hasil Node termasuk pembungkus), typecheck, build Next dan **tiga alur browser**. PostgreSQL 17: migrasi 001–005 dan enam suite SQL. Lima screenshot aktual pada artifact **WarungRafi-M5-reports-review** telah ditinjau; contoh disimpan dalam [panduan laporan](M5-REPORTS-SHEETS.md).

- Kontrak laporan berasal dari serializer C# yang melewati projection PostgreSQL. Sampel manual cocok: bruto Rp46.500, tunai Rp31.500, QRIS kasir Rp15.000, refund Rp37.500, pengeluaran laci Rp10.000; sesi ditutup seharusnya Rp112.500, fisik Rp112.000, selisih −Rp500.
- SQL menguji snapshot tetap, replay identitas/filter/pelaku, sesi melewati tengah malam, mutasi bank dari payout periode sebelumnya, koordinasi workbook, pemulihan lease, penolakan pekerja lama, backoff, target tetap, hak akses serta pengecualian label pelanggan dari snapshot.
- Browser menguji rentang tanggal tidak valid, respons pembuatan hilang lalu reload/retry, CSV, quota, respons penulisan Sheets hilang, retry tanpa tab ganda, status setelah baca ulang cocok, dan halaman 390 px tanpa overflow horizontal. Tabel rinci memiliki area gulir sendiri.
- Unit/API menguji tipe sel/formula, angka nol vs belum diketahui, ketidaksesuaian total sumber, refund lintas periode, payout batal, OAuth RS256, izin, readback berbeda, perubahan tab, autentikasi pengelola/runner dan pembatasan origin.
- Google memakai transport fixture dan kunci RSA uji sementara. Tidak ada akun Google nyata, private key produksi atau scheduler yang dipasang. Migrasi 005 masih perlu diterapkan pada cloud M3; M5/#11 tetap terbuka untuk verifikasi koneksi nyata. Label pelanggan dihapus pada sumber snapshot, bukan hanya disembunyikan di layar.

Bukti M5 tahap 2: [CI 3d90567](https://github.com/Parjimin/warung-rafi/actions/runs/36242047390), seluruh job lulus. Windows tetap 306 pemeriksaan; admin menjadi 22 unit dan 13 skenario API (15 hasil Node termasuk pembungkus). Artifact **WarungRafi-M5-finance-review** memuat tujuh screenshot aktual desktop/ponsel dengan data fixture.

- PostgreSQL 17: migrasi 001–004, lima suite SQL; retry identitas sama, penolakan isi berubah/revisi lama, pencocokan satu-ke-satu dan selisih, biaya null vs nol, snapshot estimasi, neto payout, alokasi ganda, tanggal WIB, selisih bank, pembatalan, akses backend/RLS, dan rollback projection/revisi saat jurnal gagal.
- Browser: pencocokan, tarif, estimasi, aktual, konflik tab dengan isian bertahan, respons hilang lalu reload/kirim ulang tanpa penulisan ganda, pencairan, mutasi berselisih, pembatalan serta jejak audit. Tidak ada overflow horizontal pada 390 px; tombol simpan dialog terjangkau. Screenshot aktual desktop/ponsel telah ditinjau.
- Koreksi timestamp webhook memastikan status refund berikutnya tidak menggeser tanggal pembayaran. Agregat mencakup semua baris dalam periode, walau detail dibatasi 50 per halaman.
- Pemeriksaan WPF sempat timeout pada transisi buka pesanan → bayar di run `e81c66c`; retry lulus. Checkpoint `3d90567` memperbaiki sinkronisasi tes: menunggu operasi UI selesai sebelum langkah berikutnya. Seluruh job lulus tanpa retry pada checkpoint tersebut.
- Browser/API memakai service fixture; aturan transaksi diuji terpisah pada PostgreSQL. Tarif fixture bukan tarif merchant nyata. Tidak ada dana ditransfer atau koneksi bank langsung. Pada checkpoint tahap 2, Sheets belum tersedia; implementasi software tahap 3 dan batas koneksi nyata dicatat di atas. Cloud aktual serta merchant/perangkat masih mengikuti milestone masing-masing.


Bukti M5 tahap 1: [Verify application — 58ba711](https://github.com/Parjimin/warung-rafi/actions/runs/36209021572), **seluruh job lulus**. Total **306 pemeriksaan Windows**: 23 dasar + 60 recovery + 31 inbox + 50 keuangan + 26 sinkronisasi/cache + 116 UI. Artifact **WarungRafi-Windows-preview** menyertakan aplikasi, simulasi QRIS, `Set-ManagerPin.ps1` dan panduan kas/refund. Artifact **WarungRafi-UI-review** memuat 26 screenshot Windows aktual.

- Keuangan: modal, penjualan neto kembalian, kas masuk/keluar, refund sebagian, batas saldo, PIN/cooldown, penutupan berselisih, retry, restart, rollback outbox, jurnal permanen dan backup migrasi.
- UI: alur kas keluar, permintaan dan persetujuan refund, pemeriksaan sebelum tutup kas, kembali mengubah hitungan, dan buka kas dari pembayaran. Enam pemeriksaan baru membuktikan tombol kas tetap terlihat pada 1280×720 dan 900×620 DIP, termasuk ketika rincian digulir. Screenshot kas pada kedua ukuran, PIN refund dan konfirmasi tutup kas telah ditinjau. DIP bukan uji DPI monitor fisik.
- Admin: 16 tes unit, 12 skenario API melalui aplikasi hasil build (Node melaporkan 14 termasuk pembungkus), typecheck, build Next.js dan alur browser lulus. Kontrak jurnal memakai fixture serializer C#; service cloud/provider memakai fixture terisolasi.
- PostgreSQL 17: migrasi 001–003 dan suite `assertions`, `sync_monitor`, `payments`, `finance` lulus. Pengiriman ulang tidak menggandakan jurnal; gap urutan, snapshot pesanan yang belum diterima dan perubahan isi ditolak secara atomik; hak akses serta larangan ubah/hapus jurnal diuji.
- Checkpoint awal `7922f9c` lulus Windows/admin tetapi gagal pada sintaks `CASE` migrasi 003. Koreksi ini sudah dibuktikan pada run `58ba711`; kegagalan awal tidak dihitung sebagai keberhasilan. Sebelum CI, seluruh migrasi/suite SQL juga lulus PostgreSQL 18.3 WASM melalui PGlite lokal.

Bukti ini belum mencakup Supabase/Vercel produksi, merchant asli, printer, touchscreen atau speaker fisik. Integrasi layanan nyata serta finalisasi tetap diperlukan sebelum aplikasi dipakai operasional.

Bukti M4: [Verify application — f6fd4cb](https://github.com/Parjimin/warung-rafi/actions/runs/36126169812), seluruh job lulus. **219 pemeriksaan Windows**: 23 dasar + 60 recovery + 31 inbox pembayaran + 21 sinkronisasi/cache + 84 UI. Artifact **WarungRafi-Windows-preview** berisi aplikasi dan `Coba-QRIS.cmd`; **WarungRafi-UI-review** berisi 19 screenshot render aktual.

- `WarungRafi.Checks`: 23 pemeriksaan domain, harga historis, pembayaran/outbox atomik dan struk; lulus lokal serta Windows CI.
- `WarungRafi.RecoveryChecks`: 60 pemeriksaan di Windows CI, termasuk penghentian proses, `SQLITE_FULL`, konkurensi dan backlog besar. Tidak memenuhi SSD fisik.
- `WarungRafi.PaymentChecks`: 31 pemeriksaan lokal/Windows untuk identitas/sequence, retry/restart, nominal sama, batch rusak, rollback cursor dan kesegaran notifikasi.
- `WarungRafi.SyncChecks`: 21 pemeriksaan Windows untuk HTTP/offline, acknowledgement, konflik, cursor, replay inbox dan cache foto. Service/folder/database terisolasi.
- `WarungRafi.UiChecks`: 84 pemeriksaan Windows pada lima ukuran area kerja DIP, alur kasir, pemulihan nama/draf dan popup simulasi. Fokus mengetik dipertahankan, tidak ada tombol pada popup, tidak melunasi pesanan, sound callback sekali per bukti baru, dan audio gagal tidak menahan popup. Screenshot terakhir ditinjau: popup berada di atas panel pesanan. Pengukuran DIP bukan uji DPI monitor fisik.
- Unit admin: 14 tes lulus lokal/CI, termasuk signature, status otoritatif, tanggal provider, kontrak katalog/penjualan, foto dan laporan perangkat.
- API melalui Next hasil build: 11 skenario lulus lokal/CI (Node melaporkan 13 termasuk pembungkus), terdiri dari lima M3 dan enam M4. Provider dan Supabase menggunakan fixture lokal; tidak memindahkan uang.
- Browser admin Chromium, typecheck dan build Next lulus CI. Uji browser menggunakan service fixture, belum deployment Supabase/Vercel aktual.
- Migrasi 001+002 dan assertions/sync_monitor/payments lulus PostgreSQL CI: retry, rollback, konflik, permissions, nominal sama dan status refund tidak mundur. Database uji sementara.

[Dokumentasi M4](M4-QRIS.md) menjelaskan alur, simulasi, bukti, serta batas pengujian. Sound callback membuktikan permintaan suara dari aplikasi; speaker fisik diuji di M6. Build/publish tidak membuktikan kecocokan printer atau touchscreen.

Bukti M3: [Verify application — 4e3fb3c](https://github.com/Parjimin/warung-rafi/actions/runs/36111996368), seluruh job lulus. Tersedia **WarungRafi-Windows-preview**, **WarungRafi-M3-admin-review** dan **WarungRafi-UI-review**. Total pemeriksaan desktop **23 + 60 + 15 + 69 = 167**; ini bukan uji perangkat/merchant fisik.

Bukti UI M2: [Verify application — de455eb](https://github.com/Parjimin/warung-rafi/actions/runs/36063728912), **23 + 60 + 69 pemeriksaan desktop**, build/publish Windows, admin dan database lulus. Run ini disimpan sebagai bukti historis; preview terbaru tersedia pada run M5 di atas. **WarungRafi-UI-review** berisi 18 screenshot render aktual.

Bukti M1: [Verify application — 6fe719b](https://github.com/Parjimin/warung-rafi/actions/runs/36013056985), seluruh job desktop, admin, dan database lulus.

Bukti awal: [Verify application #1](https://github.com/Parjimin/warung-rafi/actions/runs/35978994018), [Sync project tracking #1](https://github.com/Parjimin/warung-rafi/actions/runs/35978993935).

## Batas perilaku versi ini

1. Menu/harga awal adalah dummy. Katalog terbitan menggantikannya. Ilustrasi menu awal berupa gambar vektor lokal sementara; URL foto dari admin di-cache setelah unduhan berhasil. Unggah foto tersedia setelah bucket Storage dikonfigurasi. Foto yang belum berhasil diunduh memakai placeholder saat offline; objek storage tanpa referensi belum dibersihkan otomatis.
2. Pembayaran QRIS dicatat sebagai pernyataan kasir. Bukti provider tidak dihubungkan otomatis ke pesanan, meskipun nominal sama. Pengelola dapat mencocokkan bukti dan pesanan secara eksplisit di Keuangan dengan alasan serta audit, termasuk pasangan berselisih.
3. Popup hanya judul, nominal, jam; maksimal sekitar enam detik. Inbox disimpan sebelum ditampilkan. Bukti lebih dari 60 detik atau berasal dari masa depan tidak dibunyikan ketika aplikasi mengejar backlog. Ini mencegah suara lama mengesankan ada pembayaran baru. Gangguan saat sesudah simpan sebelum popup dapat menyebabkan popup terlewat; bukti tetap tersimpan.
4. Laptop membaca notifikasi melalui polling HTTPS, bukan koneksi websocket. Interval dasar 3 detik di pembayaran dan 12 detik di halaman lain, ditambah waktu jaringan/proses. Implementasi M4 memisahkan loop ini dari sinkronisasi katalog/outbox. Tidak ada jaminan suara tepat seketika. Tidak ada notifikasi OS ketika aplikasi ditutup.
5. Kasir tidak menunggu cloud atau printer untuk menerima pesanan berikutnya. Tidak ada retry cetak otomatis karena status fisik kertas belum dapat dibuktikan. `submitted` berarti masuk spooler, bukan pasti keluar kertas.
6. Nama printer harus diatur eksplisit. Data lokal tersimpan di SQLite; belum ada backup/restore UI. Jangan menghapus database untuk memperbaiki sinkronisasi.
7. Riwayat/daftar pesanan menampilkan hingga 1.000 catatan per tampilan. Ringkasan harian menghitung seluruh transaksi pada tanggal WIB, tanpa limit tersebut. Pagination menjadi pekerjaan lanjutan.
8. Autentikasi admin memakai satu user Supabase yang diizinkan. Token akses di cookie HTTP-only berlaku maksimal satu jam; setelah itu login ulang. MFA/role/rotasi token perangkat dan hardening login belum selesai.
9. Kategori awal tetap empat. Item boleh ditambah/diubah/dinonaktifkan. CRUD kategori bebas belum tersedia.
10. Kas hari ini menampilkan sesi aktif, uang laci tercatat dan penjualan kotor sesi secara terpisah. Refund berhasil mengurangi laci hanya jika tunai. Sesi bisa melewati tengah malam. Dashboard biaya, pencairan dan rekonsiliasi provider tersedia di web admin; masukan biaya/mutasi memakai referensi laporan manual. Penjualan sebelum migrasi tetap di riwayat dan tidak dibuatkan sesi kas fiktif.

11. Monitor menampilkan laporan perangkat terakhir, bukan kondisi langsung saat offline. Angka antrean menghitung perubahan data; versi cache tidak berarti harga pesanan aktif ikut berubah.

12. Laporan berupa salinan tetap. CSV tersedia tanpa Google; Sheets membutuhkan konfigurasi server dan scheduler terpisah. Retry tidak menggandakan tab dan status berhasil membutuhkan baca ulang cocok. Waktu verifikasi terakhir bukan jaminan isi tidak pernah diedit pemilik sesudahnya. Batas sumber/sel, histori tampilan, serta kolom yang belum direkam dijelaskan dalam panduan M5 tahap 3.

## Gate sebelum produksi

Build dan tes CI lulus; uji Windows 1366×768 / scaling; uji printer USB; aktivasi dan uji Midtrans static QRIS; Supabase dan paket Vercel yang sesuai penggunaan komersial; migrasi kredensial; validasi operasional ledger/refund/rekonsiliasi dan laporan Sheets; backup/restore; UAT penjual. Daftar pekerjaan dapat dipantau pada Issues per milestone.

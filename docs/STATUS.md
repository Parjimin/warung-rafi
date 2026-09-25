# Status implementasi — checkpoint 25 September 2026

**M4 aktif: ketahanan inbox dan simulasi QRIS di branch kerja; belum rilis produksi.** Pengiriman perubahan M4 ke GitHub tertahan oleh kegagalan pemeriksaan persetujuan otomatis akibat batas penggunaan layanan. CI Windows/PostgreSQL M4 belum dijalankan; bukti CI terakhir tetap M3. Tabel ini membedakan kode yang tersedia, bukti uji, dan pekerjaan yang masih terbuka. Status CI terbaru dapat diperiksa di tab [Actions](https://github.com/Parjimin/warung-rafi/actions).

| Bagian | Implementasi tersedia | Verifikasi / batas saat checkpoint |
| --- | --- | --- |
| M0 — selesai | Blueprint, keputusan arsitektur, M0–M6, backlog, workflow CI dan pelacakan | 7 milestone dan 12 issue berhasil dibuat; workflow tracking lulus |
| M1 — selesai | Aturan pesanan, harga snapshot, tunai, ditunda, SQLite, optimistic concurrency, outbox atomik | 23 pemeriksaan dasar + 60 pemeriksaan recovery/integritas lulus di CI Windows: process-kill, SQLITE_FULL, konkurensi, antrean >1.000, dan batas tanggal WIB |
| M2 — review laptop | Satu bar navigasi, kartu mendatar 2×2, panel pesanan fleksibel/dapat diperbesar, tombol sejajar, pencarian, nama otomatis tersimpan, pembayaran/riwayat/kas dipoles, animasi singkat | 69 pemeriksaan layout/interaksi WPF lulus di CI Windows; screenshot aktual ditinjau; kenyamanan klik serta respons preview baru pada laptop pengguna masih perlu direview; touchscreen/printer tetap M6 |
| M3 — aktif | Login, tambah/edit menu, unggah/normalisasi foto, konflik draf, publikasi, monitor antrean/versi/konflik perangkat, cache katalog/foto offline | 13 tes unit admin, 5 skenario API melalui Next hasil build, alur browser, 15 pemeriksaan sync/cache Windows dan SQL lulus; setup dan uji Supabase/Storage/Vercel nyata masih terbuka |
| M4 — aktif, lokal | Polling terpisah, validasi inbox atomik, deduplikasi ketat, kebijakan popup 60 detik, simulasi berlabel dengan database terpisah | 31 pemeriksaan inbox SQLite + 14 unit admin + 11 skenario API dan build/typecheck lulus lokal; pengujian baru Windows/SQL serta publikasi GitHub belum selesai. [Rincian M4](M4-QRIS.md) |
| M5 | Ringkasan penjualan tunai/QRIS lokal per tanggal WIB | Ledger, sesi kas, refund, MDR, payout, rekonsiliasi, Sheets belum diimplementasikan |
| M6 | Panduan setup dan paket preview melalui CI | Finalisasi touchscreen, printer, merchant, installer, backup/restore, perlindungan token, dan UAT masih terbuka |

## Pengerjaan per milestone

M0 dan M1 selesai. [Pengujian integritas dan pemulihan M1](M1-VALIDATION.md) lulus di CI Windows pada commit `6fe719b`. Saat ini **M4: QRIS**, dengan [alur, simulasi dan batas verifikasi](M4-QRIS.md). [Alur dan setup M3](M3-ADMIN-SYNC.md) tersedia. Review laptop untuk [M2](M2-UI-REVIEW.md) tetap terbuka. Kriteria integrasi cloud M3 dan verifikasi M4 belum seluruhnya selesai. Touchscreen, printer, dan merchant asli tetap pada M6.

## Umpan balik pengguna dan prioritas berikutnya

Pengguna melaporkan versi awal terasa cepat di laptop non-touchscreen, tetapi daftar pesanan terjepit dan beberapa kontrol kurang nyaman diklik. Screenshot menunjukkan header serta tombol vertikal memakan ruang, ditambah masalah kontras pada tombol hijau.

Revisi kedua M2 menyatukan brand/navigasi/tanggal dalam satu bar atas, menyeimbangkan empat kartu menjadi 2×2, membedakan ilustrasi hidangan, dan merapikan posisi subtotal/kontrol jumlah. Preview M2 mengurangi tinggi header, memberikan area fleksibel pada daftar pesanan, menempatkan total dan aksi tetap di bawah, serta menyediakan Perbesar. Katalog, pembayaran, ditunda, riwayat, dan kas memakai gaya konsisten. Nama pelanggan tersimpan otomatis. Animasi tekan/fade singkat tidak menunda aksi penyimpanan. Masih perlu review pemakaian pada laptop pengguna sebelum M2 ditutup; respons cepat belum dibuktikan melalui benchmark formal.

Uji touchscreen, printer OKAY 58D, serta pembuatan/aktivasi dan uji akun merchant asli **dijadwalkan pada M6**. Printer belum tersedia; pemilik belum sempat menyiapkan merchant. Pengembangan fitur lain tetap lanjut. M4 dapat menggunakan mock/fixture berlabel uji, tanpa menganggap transaksi simulasi sebagai pembayaran nyata.

## Bukti pengujian

Checkpoint lokal M4: **23 pemeriksaan regresi dasar + 31 pemeriksaan inbox SQLite** di Linux, **14 tes unit admin**, **11 skenario API** melalui Next hasil build (13 termasuk pembungkus), typecheck dan build web lulus. Pengujian desktop dan PostgreSQL baru belum dinyatakan lulus. Daftar di bawah adalah bukti CI terakhir M3, bukan hasil perubahan M4.

- `node --test apps/admin/tests/*.test.ts`: **13 tes lulus**. Mencakup pembayaran, katalog/total, batas body, decoding/ukuran/jenis foto, kegagalan storage dan validasi laporan perangkat.
- `dotnet run --project tests/WarungRafi.Checks`: **23 pemeriksaan lulus**, juga lulus di CI Windows. Build WPF dan publish self-contained Windows x64 berhasil. Build tidak membuktikan kompatibilitas printer/touchscreen.
- `dotnet run --project tests/WarungRafi.RecoveryChecks -c Release`: **60 pemeriksaan lulus di CI Windows**. Database tes terisolasi; proses anak benar-benar dihentikan. Simulasi kapasitas menggunakan error asli SQLite `SQLITE_FULL`, bukan memenuhi SSD fisik. Restore dependency lokal pada checkpoint ini terhambat akses NuGet; hasil suite baru mengacu pada CI.
- `dotnet run --project tests/WarungRafi.UiChecks -c Release`: **69 pemeriksaan lulus di Windows**, mencakup lima ukuran area kerja DIP, empat item tanpa scroll pada 1280×720, kontrol tetap terlihat, nama/draf pulih, ditunda/resume, nominal invalid, kembalian, dan popup pasif. Screenshot aktual WPF ditinjau. Pengukuran DIP bukan uji DPI monitor fisik.
- `dotnet run --project tests/WarungRafi.SyncChecks -c Release`: **15 pemeriksaan lulus di Windows**: gangguan HTTP, offline, konflik, acknowledgement rusak/sebagian, cache katalog/foto dan pengiriman ulang. HTTP/database/folder terisolasi.
- `node --test integration/api.test.ts` dari `apps/admin`: **5 skenario API lulus** (Node melaporkan 6 termasuk pembungkus suite), memakai Next hasil build dan service Supabase tiruan.
- `node integration/browser.mjs`: alur unggah, edit, simpan, terbitkan, konflik dan muat ulang lulus di Chromium CI; monitor desktop/mobile dan screenshot ditinjau.
- Migrasi 001+002 dan assertions PostgreSQL **lulus di CI**: retry dedupe, rollback batch, konflik versi, persistensi diagnostik, refund tidak mundur, serta role permissions. Database uji sementara, bukan produksi.
- Instalasi dependency, tes unit, API, typecheck dan build Next.js berhasil lokal pada checkpoint M3; CI juga lulus. Chromium lokal belum tersedia karena unduhan browser tidak lengkap, sehingga bukti browser mengacu pada CI. Lockfile dikomit; instalasi berikutnya memakai `npm ci`.

Bukti M3: [Verify application — 4e3fb3c](https://github.com/Parjimin/warung-rafi/actions/runs/36111996368), seluruh job lulus. Tersedia **WarungRafi-Windows-preview**, **WarungRafi-M3-admin-review** dan **WarungRafi-UI-review**. Total pemeriksaan desktop **23 + 60 + 15 + 69 = 167**; ini bukan uji perangkat/merchant fisik.

Bukti UI M2: [Verify application — de455eb](https://github.com/Parjimin/warung-rafi/actions/runs/36063728912), **23 + 60 + 69 pemeriksaan desktop**, build/publish Windows, admin dan database lulus. Unduh **WarungRafi-Windows-preview** dari run ini untuk mencoba UI baru; **WarungRafi-UI-review** berisi 18 screenshot render aktual.

Bukti M1: [Verify application — 6fe719b](https://github.com/Parjimin/warung-rafi/actions/runs/36013056985), seluruh job desktop, admin, dan database lulus.

Bukti awal: [Verify application #1](https://github.com/Parjimin/warung-rafi/actions/runs/35978994018), [Sync project tracking #1](https://github.com/Parjimin/warung-rafi/actions/runs/35978993935).

## Batas perilaku versi ini

1. Menu/harga awal adalah dummy. Katalog terbitan menggantikannya. Ilustrasi menu awal berupa gambar vektor lokal sementara; URL foto dari admin di-cache setelah unduhan berhasil. Unggah foto tersedia setelah bucket Storage dikonfigurasi. Foto yang belum berhasil diunduh memakai placeholder saat offline; objek storage tanpa referensi belum dibersihkan otomatis.
2. Pembayaran QRIS dicatat sebagai pernyataan kasir. Bukti provider tidak dihubungkan otomatis ke pesanan, meskipun nominal sama. Tidak ada fitur rekonsiliasi final pada versi ini.
3. Popup hanya judul, nominal, jam; maksimal sekitar enam detik. Inbox disimpan sebelum ditampilkan. Bukti lebih dari 60 detik atau berasal dari masa depan tidak dibunyikan ketika aplikasi mengejar backlog. Ini mencegah suara lama mengesankan ada pembayaran baru. Gangguan saat sesudah simpan sebelum popup dapat menyebabkan popup terlewat; bukti tetap tersimpan.
4. Laptop membaca notifikasi melalui polling HTTPS, bukan koneksi websocket. Interval dasar 3 detik di pembayaran dan 12 detik di halaman lain, ditambah waktu jaringan/proses. Implementasi M4 memisahkan loop ini dari sinkronisasi katalog/outbox. Tidak ada jaminan suara tepat seketika. Tidak ada notifikasi OS ketika aplikasi ditutup.
5. Kasir tidak menunggu cloud atau printer untuk menerima pesanan berikutnya. Tidak ada retry cetak otomatis karena status fisik kertas belum dapat dibuktikan. `submitted` berarti masuk spooler, bukan pasti keluar kertas.
6. Nama printer harus diatur eksplisit. Data lokal tersimpan di SQLite; belum ada backup/restore UI. Jangan menghapus database untuk memperbaiki sinkronisasi.
7. Riwayat/daftar pesanan menampilkan hingga 1.000 catatan per tampilan. Ringkasan harian menghitung seluruh transaksi pada tanggal WIB, tanpa limit tersebut. Pagination menjadi pekerjaan lanjutan.
8. Autentikasi admin memakai satu user Supabase yang diizinkan. Token akses di cookie HTTP-only berlaku maksimal satu jam; setelah itu login ulang. MFA/role/rotasi token perangkat dan hardening login belum selesai.
9. Kategori awal tetap empat. Item boleh ditambah/diubah/dinonaktifkan. CRUD kategori bebas belum tersedia.
10. Cash summary adalah penjualan kotor tercatat, bukan saldo laci atau uang bersih rekening. Belum menghitung 0,7% atau tarif lain: biaya tidak di-hardcode tanpa profil merchant dan data aktual.

11. Monitor menampilkan laporan perangkat terakhir, bukan kondisi langsung saat offline. Angka antrean menghitung perubahan data; versi cache tidak berarti harga pesanan aktif ikut berubah.

## Gate sebelum produksi

Build dan tes CI lulus; uji Windows 1366×768 / scaling; uji printer USB; aktivasi dan uji Midtrans static QRIS; Supabase dan paket Vercel yang sesuai penggunaan komersial; migrasi kredensial; ledger/refund/rekonsiliasi; backup/restore; UAT penjual. Daftar pekerjaan dapat dipantau pada Issues per milestone.

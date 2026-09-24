# Status implementasi — checkpoint 24 September 2026

**Versi 0.1: fondasi yang dapat ditinjau, belum rilis produksi.** Tabel ini membedakan kode yang tersedia, bukti uji, dan pekerjaan yang masih terbuka. Status CI terbaru dapat diperiksa di tab [Actions](https://github.com/Parjimin/warung-rafi/actions).

| Bagian | Implementasi tersedia | Verifikasi / batas saat checkpoint |
| --- | --- | --- |
| M0 — selesai | Blueprint, keputusan arsitektur, M0–M6, backlog, workflow CI dan pelacakan | 7 milestone dan 12 issue berhasil dibuat; workflow tracking lulus |
| M1 — selesai | Aturan pesanan, harga snapshot, tunai, ditunda, SQLite, optimistic concurrency, outbox atomik | 23 pemeriksaan dasar + 60 pemeriksaan recovery/integritas lulus di CI Windows: process-kill, SQLITE_FULL, konkurensi, antrean >1.000, dan batas tanggal WIB |
| M2 | WPF navbar atas, tombol besar, empat kategori, keranjang, uang/kembalian, riwayat, cetak terpisah dari UI | Build WPF lokal dan CI Windows lulus; publish preview berhasil; prioritas perapian UI/UX dengan mouse/touchpad; uji sentuh dan OKAY 58D dijadwalkan M6 |
| M3 | Login Supabase Auth, tambah/edit menu, URL foto, draf/publikasi, API device, cache katalog/foto | Tes kontrak, typecheck dan build Next.js lulus di CI; akun cloud, unggah foto, observabilitas konflik belum tersedia |
| M4 | SHA512 + Status API, persistensi provider, inbox/cursor, popup pasif + suara | Tes unit webhook lulus; pengujian logika/simulasi dapat lanjut; aktivasi dan uji merchant end-to-end dijadwalkan M6 |
| M5 | Ringkasan penjualan tunai/QRIS lokal per tanggal WIB | Ledger, sesi kas, refund, MDR, payout, rekonsiliasi, Sheets belum diimplementasikan |
| M6 | Panduan setup dan paket preview melalui CI | Finalisasi touchscreen, printer, merchant, installer, backup/restore, perlindungan token, dan UAT masih terbuka |

## Pengerjaan per milestone

M0 dan M1 selesai. [Pengujian integritas dan pemulihan M1](M1-VALIDATION.md) lulus di CI Windows pada commit `6fe719b`. Tahap berikutnya adalah **M2: perapian UI/UX kasir**. Sebagian kode M2–M4 sudah tersedia, tetapi kriteria milestone tersebut belum seluruhnya selesai. Touchscreen, printer, dan merchant asli tetap pada M6.

## Umpan balik pengguna dan prioritas berikutnya

Pada 24 September 2026 pengguna melaporkan aplikasi terasa cepat dan responsif di laptop non-touchscreen. Beberapa kontrol masih kurang nyaman diklik. UI saat ini adalah prototipe fungsional; perapian visual dan interaksi belum selesai. Checkpoint M1 ini memperkuat penyimpanan dan penghitung pesanan ditunda; perapian visual M2 belum dikerjakan.

Prioritas M2: ukuran dan jarak tombol, area klik, hierarki aksi utama, tata letak keranjang/pembayaran, keterbacaan, serta konsistensi feedback interaksi. Validasi awal memakai mouse/touchpad dan keyboard pada laptop yang tersedia sambil mempertahankan respons cepat.

Uji touchscreen, printer OKAY 58D, serta pembuatan/aktivasi dan uji akun merchant asli **dijadwalkan pada M6**. Printer belum tersedia; pemilik belum sempat menyiapkan merchant. Pengembangan fitur lain tetap lanjut. M4 dapat menggunakan mock/fixture berlabel uji, tanpa menganggap transaksi simulasi sebagai pembayaran nyata.

## Bukti pengujian

- `node --test apps/admin/tests/*.test.ts`: **9 tes lulus**. Mencakup signature salah, merchant/identitas berbeda, status provider otoritatif, provider down, nominal pecahan, total/kembalian, katalog, dan batas body streaming.
- `dotnet run --project tests/WarungRafi.Checks`: **23 pemeriksaan lulus**, juga lulus di CI Windows. Build WPF dan publish self-contained Windows x64 berhasil. Build tidak membuktikan kompatibilitas printer/touchscreen.
- `dotnet run --project tests/WarungRafi.RecoveryChecks -c Release`: **60 pemeriksaan lulus di CI Windows**. Database tes terisolasi; proses anak benar-benar dihentikan. Simulasi kapasitas menggunakan error asli SQLite `SQLITE_FULL`, bukan memenuhi SSD fisik. Restore dependency lokal pada checkpoint ini terhambat akses NuGet; hasil suite baru mengacu pada CI.
- Migrasi dan assertions PostgreSQL **lulus di CI**: retry dedupe, rollback batch, konflik versi, refund tidak mundur, serta role permissions. Database uji sementara, bukan produksi.
- Registry npm tidak dapat diakses dari lingkungan penyusunan. **Build Next.js dan typecheck lulus pada CI GitHub**. Lockfile hasil run diambil dan dikomit; instalasi berikutnya memakai `npm ci`.

Bukti terbaru M1: [Verify application — 6fe719b](https://github.com/Parjimin/warung-rafi/actions/runs/36013056985), seluruh job desktop, admin, dan database lulus.

Bukti awal: [Verify application #1](https://github.com/Parjimin/warung-rafi/actions/runs/35978994018), [Sync project tracking #1](https://github.com/Parjimin/warung-rafi/actions/runs/35978993935).

## Batas perilaku versi ini

1. Menu/harga awal adalah dummy. Katalog terbitan menggantikannya. Foto contoh memakai simbol; URL foto dari admin di-cache setelah unduhan berhasil. Unggah file foto belum tersedia.
2. Pembayaran QRIS dicatat sebagai pernyataan kasir. Bukti provider tidak dihubungkan otomatis ke pesanan, meskipun nominal sama. Tidak ada fitur rekonsiliasi final pada versi ini.
3. Popup hanya judul, nominal, jam; maksimal sekitar enam detik. Inbox disimpan sebelum ditampilkan. Bukti lebih dari 60 detik tidak dibunyikan ketika aplikasi mengejar backlog. Ini mencegah suara lama mengesankan ada pembayaran baru. Gangguan saat sesudah simpan sebelum popup dapat menyebabkan popup terlewat; bukti tetap tersimpan.
4. Laptop membaca notifikasi melalui polling HTTPS, bukan koneksi websocket. Interval dasar 3 detik di pembayaran dan 12 detik di halaman lain, ditambah waktu jaringan/proses. Tidak ada jaminan suara tepat seketika. Tidak ada notifikasi OS ketika aplikasi ditutup.
5. Kasir tidak menunggu cloud atau printer untuk menerima pesanan berikutnya. Tidak ada retry cetak otomatis karena status fisik kertas belum dapat dibuktikan. `submitted` berarti masuk spooler, bukan pasti keluar kertas.
6. Nama printer harus diatur eksplisit. Data lokal tersimpan di SQLite; belum ada backup/restore UI. Jangan menghapus database untuk memperbaiki sinkronisasi.
7. Riwayat/daftar pesanan menampilkan hingga 1.000 catatan per tampilan. Ringkasan harian menghitung seluruh transaksi pada tanggal WIB, tanpa limit tersebut. Pagination menjadi pekerjaan lanjutan.
8. Autentikasi admin memakai satu user Supabase yang diizinkan. Token akses di cookie HTTP-only berlaku maksimal satu jam; setelah itu login ulang. MFA/role/rotasi token perangkat dan hardening login belum selesai.
9. Kategori awal tetap empat. Item boleh ditambah/diubah/dinonaktifkan. CRUD kategori bebas belum tersedia.
10. Cash summary adalah penjualan kotor tercatat, bukan saldo laci atau uang bersih rekening. Belum menghitung 0,7% atau tarif lain: biaya tidak di-hardcode tanpa profil merchant dan data aktual.

## Gate sebelum produksi

Build dan tes CI lulus; uji Windows 1366×768 / scaling; uji printer USB; aktivasi dan uji Midtrans static QRIS; Supabase dan paket Vercel yang sesuai penggunaan komersial; migrasi kredensial; ledger/refund/rekonsiliasi; backup/restore; UAT penjual. Daftar pekerjaan dapat dipantau pada Issues per milestone.

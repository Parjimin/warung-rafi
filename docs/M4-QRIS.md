# M4 — Notifikasi QRIS statis

Checkpoint 25 September 2026: implementasi dan pengujian API tersedia pada branch kerja `codex/m4-qris-notifications`. **M4 belum dinyatakan selesai**: pengiriman GitHub tertahan karena pemeriksaan persetujuan otomatis gagal akibat batas penggunaan layanan; CI Windows dan PostgreSQL untuk perubahan ini belum dijalankan. Preview terakhir yang sudah terverifikasi tetap paket M3.

## Perilaku untuk kasir

Notifikasi normal tampil di pojok kanan atas aplikasi dengan tiga baris:

> Pembayaran QRIS diterima  
> Rp22.500  
> Pukul 18.44

Popup tidak memiliki tombol, tidak mengambil fokus keyboard, dan tidak menangkap klik. Popup ditampilkan sekitar enam detik. Suara memakai bunyi notifikasi Windows (`SystemSounds.Asterisk`); volume dan bunyi fisik mengikuti pengaturan laptop. Kegagalan audio tidak membatalkan penyimpanan, menghentikan kasir, atau membuat popup menetap.

Notifikasi hanya menyampaikan bukti pembayaran provider. Pesanan yang sedang dibuka tetap sama. Dua pembayaran Rp22.500 dengan ID berbeda tetap merupakan dua bukti berbeda; kesamaan nominal tidak melunasi atau memasangkan pesanan secara otomatis. Pencatatan QRIS pada pesanan masih berupa konfirmasi kasir, terpisah dari bukti provider dan pencairan rekening. Rekonsiliasi merupakan pekerjaan M5.

## Coba tanpa akun merchant

Setelah paket M4 berhasil dibangun dan diterbitkan, ekstrak preview Windows lalu buka `Coba-QRIS.cmd`. Alternatif dari folder aplikasi:

```powershell
.\WarungRafi.exe --demo-qris
```

Dari source pada Windows dengan .NET 10 SDK:

```powershell
dotnet run --project apps/desktop/WarungRafi.Desktop -- --demo-qris
```

1. Jendela berjudul **SIMULASI QRIS** dan footer bertuliskan **SIMULASI · BUKAN PEMBAYARAN NYATA**.
2. Tambahkan menu bila ingin mencoba nominal sesuai pesanan. Tanpa item, nominal contoh Rp22.500.
3. Klik **Coba QRIS** di footer. Tombol ini hanya ada di mode simulasi, di luar popup.
4. Bukti dummy disimpan melalui inbox lokal yang sama, kemudian popup berlabel **SIMULASI** tampil dan aplikasi meminta Windows membunyikan notifikasi.
5. Lanjutkan mengetik atau memilih menu. Notifikasi tidak melunasi pesanan.

Data simulasi tersimpan di `%LOCALAPPDATA%\WarungRafi\DemoQris\warung-rafi.db`. Data kasir normal menggunakan `%LOCALAPPDATA%\WarungRafi\warung-rafi.db`. Mode simulasi tidak menjalankan sinkronisasi online maupun pencetakan. Setiap klik membuat ID dummy baru; kiriman ulang ID yang sama diuji oleh suite otomatis. Tutup simulasi lalu buka `WarungRafi.exe` tanpa argumen untuk kembali ke mode biasa.

## Cara kerja dan integritas

| Tahap | Perilaku |
| --- | --- |
| Webhook | Endpoint `POST /api/midtrans/notification` memeriksa ukuran payload dan signature SHA512 dengan server key. |
| Status provider | Backend meminta Status API melalui host resmi sesuai `MIDTRANS_ENV`. Transaction ID, order ID, merchant, mata uang IDR, tipe QRIS, serta nominal harus cocok. Status pada webhook saja tidak cukup. |
| Waktu | Waktu provider ditafsirkan sebagai WIB; tanggal tidak valid yang dinormalisasi JavaScript ditolak. |
| Commit server | Fungsi PostgreSQL menyimpan status provider dan event notifikasi dalam satu transaksi. Respons berhasil diberikan hanya sesudah RPC berhasil. Gangguan provider/database menghasilkan respons gagal agar dapat diulang. |
| Deduplikasi server | Satu transaction ID hanya menghasilkan satu event. Refund/partial refund yang sudah tercatat tidak dimundurkan oleh settlement yang terlambat. |
| Polling laptop | Endpoint ber-token `GET /api/device/payments?after=…` mengirim maksimal 100 event, berurutan. Polling pembayaran berjalan terpisah dari outbox, katalog, foto, dan laporan perangkat. |
| Regresi domain/storage | **23 pemeriksaan dasar lulus ulang lokal** pada perubahan M4. |
| Inbox SQLite | Seluruh batch dan cursor disimpan atomik. Identitas, nominal, waktu, ukuran batch dan rentang sequence divalidasi. ID/sequence yang digunakan ulang dengan isi berbeda ditolak; satu baris rusak membatalkan seluruh batch. |
| Popup | Hanya event baru yang sudah berhasil disimpan dan berumur 0–60 detik yang masuk antrean. Kesegaran diperiksa kembali ketika gilirannya tampil. |

Interval dasar polling adalah tiga detik ketika berada di halaman pembayaran dan dua belas detik di halaman lain, ditambah waktu jaringan/proses. Perubahan halaman mengikuti siklus polling berjalan. Ini bukan jaminan notifikasi seketika. Setiap loop menunggu permintaannya sendiri selesai, sehingga tidak menumpuk request polling yang tumpang tindih. Proses penyimpanan lokal tetap dikoordinasikan oleh SQLite.

Cursor menunjukkan bukti terakhir yang **sudah tersimpan**, bukan bukti yang pasti terdengar. Sequence boleh memiliki celah akibat rollback PostgreSQL. Bukti yang belum dikenal tetapi berada di belakang cursor ditolak. Bukti lama tetap disimpan ketika koneksi pulih, tetapi tidak dibunyikan sebagai pembayaran baru. Waktu dari masa depan juga tidak dibunyikan.

Jika aplikasi berhenti setelah commit lokal tetapi sebelum popup tampil, bukti tetap tersimpan dan popup dapat terlewat. Aplikasi sengaja tidak membunyikan ulang bukti tersimpan saat restart. Aplikasi harus terbuka untuk polling dan popup; belum ada notifikasi OS saat aplikasi tertutup.

## Pengujian dan status bukti

| Lapisan | Bukti pada checkpoint ini |
| --- | --- |
| Unit admin | **14 tes lulus lokal**, termasuk validasi signature, identitas/status otoritatif, kegagalan provider dan tanggal kalender. |
| API hasil build | **11 skenario lulus lokal**: lima skenario M3 dan enam M4. Node melaporkan 13 tes termasuk dua pembungkus. Next hasil build benar-benar menerima request HTTP. Provider dan Supabase menggunakan service fixture lokal. |
| TypeScript / build web | Typecheck dan build produksi Next berhasil lokal. |
| Regresi domain/storage | **23 pemeriksaan dasar lulus ulang lokal** pada perubahan M4. |
| Inbox SQLite | Suite baru `WarungRafi.PaymentChecks` mencakup retry/restart, perubahan identitas, batch rusak, rollback cursor, nominal sama, urutan bercelah dan kesegaran. **31 pemeriksaan lulus lokal di Linux**, termasuk kegagalan cursor dengan trigger SQLite asli. Verifikasi Windows tetap menunggu CI. |
| Transport Windows | `WarungRafi.SyncChecks` ditambah gangguan koneksi, inbox kosong/rusak, cursor dan replay respons. CI baru belum dijalankan. |
| UI WPF | `WarungRafi.UiChecks` ditambah alur bukti asli menuju popup simulasi, fokus/ketikan, sound callback, duplikat, bukti lama, tidak melunasi pesanan, dan audio gagal. Screenshot direncanakan `14-qris-simulation.png`; belum dihasilkan pada checkpoint ini. |
| PostgreSQL | `database/tests/payments.sql` menguji pending/settlement/refund, nominal sama, perubahan identitas, permissions, serta rollback jika insert notifikasi gagal. CI baru belum dijalankan. |
| Pelacakan milestone | Pemeriksaan lokal memastikan issue merchant dapat dipindah ke M6 tanpa mengganti isi/checklist/status issue. Perubahan GitHub belum dijalankan. |

Fixture provider hanya dimuat oleh proses pengujian melalui `integration/provider-preload.mjs`; aplikasi produksi tidak mengimpor modul itu dan tidak menyediakan route pembayaran palsu. Uji API memeriksa alur HTTP; uji PostgreSQL terpisah memeriksa transaksi dan deduplikasi database yang sesungguhnya. Sound callback membuktikan aplikasi meminta suara, bukan membuktikan speaker fisik berbunyi.

## Finalisasi M6

- Aktifkan merchant dan peroleh static QRIS resmi yang transaksinya dapat dilihat oleh Midtrans. QRIS cetak dari provider lain tidak otomatis menjadi terhubung ke aplikasi ini.
- Uji cakupan issuer/acquirer bersama Midtrans. Dokumentasi static QRIS mencatat kemungkinan transaksi dirutekan ke acquirer sebelumnya jika merchant pernah didaftarkan di sana; transaksi di luar Midtrans tidak dapat dipantau lewat integrasi ini.
- Pasang endpoint webhook HTTPS publik dan konfigurasi server key/merchant ID/environment di backend. Server key tidak ditempatkan di laptop.
- Uji pembayaran nyata, pengiriman ulang, jaringan terputus, laptop restart, fokus kasir, jam Windows, volume dan speaker.
- Verifikasi rekening pencairan SeaBank serta jadwal/biaya aktual. Status settlement pembayaran tidak sama dengan dana sudah masuk ke SeaBank.
- Uji dukungan refund sesuai produk merchant. Pengamanan status refund dalam kode bukan janji fitur refund API static QRIS; dokumentasi static QRIS yang ditinjau menyebut refund belum tersedia.
- Printer, sentuhan fisik, UAT penjual dan hardening kredensial tetap berada di milestone finalisasi.

Rujukan resmi yang ditinjau 25 September 2026: [Static QRIS](https://docs.midtrans.com/docs/introduction-to-static-qris), [HTTP notifications](https://docs.midtrans.com/docs/https-notification-webhooks), [Get Status API](https://docs.midtrans.com/docs/get-status-api-requests).

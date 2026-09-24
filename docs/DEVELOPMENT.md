# Menjalankan dan mengembangkan

## Prasyarat

- Laptop Windows 10/11 yang didukung .NET 10, arsitektur x64 untuk paket preview. Versi Windows dan ukuran layar perlu dicatat saat UAT.
- .NET 10 SDK untuk pengembangan. Paket self-contained CI tidak memerlukan SDK di laptop kasir.
- Node.js 24 dan npm untuk web. Git dan akun GitHub untuk mengikuti progres.
- Supabase baru untuk uji cloud, akun Supabase Auth pengelola, akun Midtrans sandbox; credential produksi belum dibutuhkan untuk tes unit.

## Kasir offline

```powershell
dotnet run --project tests/WarungRafi.Checks
dotnet run --project tests/WarungRafi.RecoveryChecks
dotnet run --project apps/desktop/WarungRafi.Desktop
```

Tanpa konfigurasi cloud kasir tetap berjalan dengan menu dummy. Setiap perubahan pesanan disimpan lokal. Satu transaksi SQLite menulis pesanan, pembayaran, dan outbox pada saat selesai. Letak database: `%LOCALAPPDATA%\WarungRafi\warung-rafi.db`. Cache foto berada dalam subfolder `images`.

Alur uji: pilih Nasi → tambah minuman → Simpan Dulu → buka dari Pesanan Ditunda → Lanjut Bayar → isi uang → Selesaikan Pesanan → lihat kembalian → Pesanan Baru. Tutup/buka aplikasi lalu periksa riwayat. Pakai data uji, bukan transaksi pelanggan.

## Printer OKAY 58D

Pasang driver Windows resmi yang sesuai unit printer dan hubungkan melalui USB. Cetak Windows test page dulu. Buka PowerShell yang akan menjalankan kasir:

```powershell
$env:WARUNG_PRINTER_NAME = 'nama persis printer di Windows'
dotnet run --project apps/desktop/WarungRafi.Desktop
```

Jangan memakai PDF printer sebagai printer kasir. Preview menggunakan driver/spooler dan lebar logis 58 mm. Uji margin, baris panjang, kertas habis, cabut USB, dan cetak ulang; belum ada klaim kecocokan fisik. Tidak mengirim perintah autocut. Pesanan tetap selesai meskipun pencetakan gagal. Tombol Cetak Ulang menghasilkan struk bertanda SALINAN.

## Database cloud

1. Buat proyek Supabase khusus uji. Jalankan `database/001_foundation.sql` satu kali lewat SQL Editor. Jangan menjalankan migration awal berulang pada database yang sudah terisi; migration selanjutnya harus berupa file baru.
2. Buat satu user melalui Supabase Auth. Nonaktifkan public signup bila tidak digunakan. Catat user UUID untuk `ADMIN_USER_ID`.
3. RLS aktif; anon dan authenticated tidak punya akses tabel langsung. Server saja memakai service role. Jangan masukkan service role ke environment desktop atau variabel `NEXT_PUBLIC_*`.
4. Tes SQL otomatis menggunakan PostgreSQL sementara dengan role tiruan. Tes ini memverifikasi dedupe, atomic batch, konflik, regresi refund, dan hak akses. Uji layanan Supabase aktual tetap diperlukan.

## Admin lokal dan Vercel

```bash
cd apps/admin
npm ci
cp .env.example .env.local
npm test
npm run typecheck
npm run dev
```

Isi `.env.local` sesuai contoh. `APP_ORIGIN` harus sama persis dengan origin browser, tanpa trailing slash. Lokal: `http://localhost:3000`; produksi: `https://domain-yang-dipakai`. Semua key hanya dibaca server. Login memakai email/password user Supabase yang UUID-nya tercantum di `ADMIN_USER_ID`.

Di admin: Tambah menu → Terapkan ke draf → Simpan draf → Terbitkan ke kasir. Draf memakai kontrol versi untuk menolak penimpaan perubahan dari tab lama. Penonaktifan menu memakai kotak Tersedia, bukan menghapus riwayat item. URL foto harus HTTPS; gunakan foto milik warung. Upload foto langsung direncanakan pada M3.

Untuk Vercel pilih framework Next.js dan **Root Directory `apps/admin`**. Isi environment server sesuai contoh; jangan commit credential. Gunakan paket yang mengizinkan penggunaan usaha. Deployment belum dilakukan pada checkpoint ini karena proyek hosting dan credential belum tersedia. Koneksi serverless menggunakan REST Supabase, sehingga tidak membutuhkan VPS atau koneksi PostgreSQL persisten dari function.

## Menghubungkan laptop

Buat token acak minimal 32 karakter dari sumber kriptografis. Isi nilai yang sama di `DEVICE_API_TOKEN` server dan `WARUNG_DEVICE_TOKEN` laptop. `DEVICE_ID` server tetap `kasir-utama`; jangan berubah sembarangan karena ia namespace sinkronisasi.

```powershell
$env:WARUNG_API_BASE_URL = 'https://domain-admin'
$env:WARUNG_DEVICE_TOKEN = '<token-perangkat>'
dotnet run --project apps/desktop/WarungRafi.Desktop
```

API laptop hanya menerima HTTPS. Ia mengirim maksimum 50 event per batch dan menghapus status pending hanya sesudah menerima daftar ID yang diakui server. Payload event tetap disimpan lokal. Versi out-of-order/conflict menolak batch; jangan mereset database/outbox sebagai jalan pintas. Catat kasusnya untuk inspeksi pengelola. Provider inbox tetap diperiksa walau batch penjualan bermasalah.

Katalog tersimpan lokal dan baru dipasang ke UI ketika layar Jualan tidak berisi keranjang. Harga item yang sudah masuk pesanan tetap menjadi snapshot. Perubahan menu tidak mengubah struk historis.

## Midtrans static QRIS

- Pastikan produk static QRIS merchant benar-benar aktif. QR cetakan harus milik merchant yang sama dengan `MIDTRANS_MERCHANT_ID`.
- Atur URL notifikasi ke `https://domain-admin/api/midtrans/notification` pada pengaturan akun yang relevan. Pastikan deployment protection tidak menghalangi webhook.
- Pilih `MIDTRANS_ENV=sandbox` atau `production` secara eksplisit. Server key wajib sesuai lingkungan.
- Endpoint memeriksa SHA512 lalu memanggil Status API untuk identitas/status yang otoritatif. Database menyimpan transaction ID unik dan satu notification event ketika settlement pertama diterima. Respons sukses baru diberikan setelah commit database.
- Laptop menyimpan bukti/cursor sebelum mengantrekan suara dan popup. Kesamaan nominal tidak digunakan untuk melunasi pesanan.
- Tes otomatis tidak memanggil akun nyata dan tidak memindahkan uang. Uji merchant membutuhkan akun serta skenario nominal yang disetujui pemilik.
- Settlement transaksi di Midtrans bukan bukti dana sudah dicairkan ke SeaBank. Rekonsiliasi pencairan terpisah masuk M5.

## CI dan progres

`Verify application` menjalankan tes .NET, build/publish WPF pada Windows, tes/typecheck/build Next.js, serta tes SQL pada PostgreSQL sementara. Jika sukses tersedia artifact **WarungRafi-Windows-preview** di run tersebut. Artifact adalah preview tanpa installer/tanda tangan.

`Sync project tracking` membuat tujuh milestone dan backlog dari `.github/planning.json`. Workflow dipicu perubahan planning pada `main` atau manual lewat Actions. Ia tidak menimpa checklist issue yang sudah diperbarui orang dan tidak menutup issue otomatis.

Saat registry tidak dapat diakses lokal, jangan menyatakan build lulus. Pakai log CI sebagai bukti dan gunakan lockfile yang dikomit untuk resolusi dependency. Tinjau dependency dan advisory lagi sebelum rilis; baseline ini bukan janji bebas kerentanan di masa depan.

## Bukti pemulihan M1

Lihat [M1-VALIDATION.md](M1-VALIDATION.md) untuk skenario process-kill, simulasi SQLITE_FULL, konkurensi, dan antrean besar. Suite memakai database sementara; tidak menghapus atau mengubah database kasir.

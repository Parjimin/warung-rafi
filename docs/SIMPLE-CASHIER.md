# Kasir satu laptop dan laporan otomatis

## Penggunaan

Unduh artifact **WarungRafi-Windows-portable** dari run CI yang lulus pada branch ini. Ekstrak seluruh ZIP ke folder biasa lalu buka `WarungRafi.exe`. Runtime .NET sudah dibundel. Tidak perlu menjalankan installer atau PowerShell. Windows tetap memerlukan driver printer yang sesuai.

Pada komputer yang belum dikonfigurasi, masuk sekali dengan email/kata sandi pengelola web dan buat PIN pengelola. Alamat layanan sudah disertakan dalam aplikasi. Kata sandi akun tidak disimpan; token perangkat dan PIN terlindungi dengan akun Windows. Tombol lewati membuka kasir offline; saat dibuka lagi, login ditawarkan kembali. Pengaturan lama dipertahankan.

Aplikasi mengirim antrean transaksi sekitar setiap 12 detik. Loop Sheets terpisah memeriksa setiap menit setelah antrean lokal kosong. Tutup aplikasi berarti penghentian loop; saat dibuka kembali, data lokal yang belum dikirim diproses. Tidak ada Cloud Scheduler yang diperlukan untuk mode ini.

Sheets memakai **dua tab: Riwayat Penjualan dan Rekap Penjualan**, berisi bulan berjalan berdasarkan WIB. Pada pergantian bulan isi diganti, termasuk penghapusan sisa baris bulan lalu. Riwayat lengkap tetap berada di database dan dapat dilaporkan dari web. Kelompok tab laporan manual lama tidak diubah atau dihapus. Ekspor snapshot manual tetap tersedia sebagai arsip pilihan.

Hanya data berubah yang ditulis; setiap 15 menit dilakukan penulisan dan verifikasi ulang untuk memperbaiki edit manual. Status sukses memerlukan baca ulang cocok. Gangguan Sheets tidak menghambat transaksi dan dicoba lagi setiap menit. Indikator Sheets terpisah dari status penyimpanan transaksi. Laporan bersifat salinan cloud; periksa catatan Sinkronisasi di tab Rekap Penjualan untuk kelengkapan. Batas laporan lama tetap berlaku (maksimal 5.000 pesanan / 150.000 sel); jika terlampaui, laporan otomatis gagal dengan aman dan laporan periode lebih pendek tetap dapat dibuat di web.

Data lokal tetap di `%LOCALAPPDATA%\WarungRafi`, sehingga mengganti folder program tidak menghapus transaksi. Menyalin folder program ke laptop lain tidak memindahkan database atau konfigurasi terenkripsi; gunakan prosedur pemulihan bila berganti laptop.

## Aktivasi rilis oleh pengelola server (sekali)

1. Database memakai migrasi 001–006. Mode langsung tidak memerlukan antrean migrasi 007 atau scheduler.
2. Deploy aplikasi admin dari commit perubahan ini. Pertahankan konfigurasi Supabase dan Google Sheets yang sudah bekerja. `DEVICE_ID` dan `DEVICE_API_TOKEN` harus terisi (token 32–512 karakter tanpa spasi).
3. Unduh paket Windows dari commit yang sama. Akun pengelola yang valid dapat menerima token perangkat melalui endpoint aktivasi; service role dan private key Google tidak masuk paket/ZIP atau repositori. Untuk Sheets langsung, koneksi tersebut diberikan hanya setelah login pengelola dan disimpan terenkripsi di laptop.
4. Pada laptop nyata, masuk, pastikan menu terbit, lakukan transaksi uji dan periksa indikator Sheets serta nilai di `Riwayat Penjualan`. Putuskan internet, lakukan transaksi uji lain, sambungkan kembali; pastikan satu transaksi hanya tercatat sekali. Uji printer dan backup tetap perlu dilakukan pada perangkat nyata.

Perubahan ini tidak otomatis memasang migrasi atau deployment produksi. Paket tidak ditandatangani; hasil CI/fixture bukan bukti perangkat atau Google produksi sudah diuji.

Untuk aplikasi yang sudah terpasang, buka Pengaturan → Hubungkan Sheets langsung dan login pengelola sekali. Lihat [alur langsung](DIRECT-SHEETS.md).

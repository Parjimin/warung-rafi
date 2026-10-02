# Google Sheets langsung dari laptop

Paket portable menyertakan runtime Node 24 pada folder `sheets`. Tidak perlu menginstal Node atau menjalankan perintah sendiri. `WarungRafi.exe` menjalankan worker tanpa jendela konsol, mengirim koneksi melalui stdin, dan menghentikannya ketika aplikasi ditutup. Runtime dan kode adalah bagian dari manifest installer.

Setiap menit, setelah antrean transaksi lokal kosong, worker mengambil `report_source` bulan berjalan (WIB) langsung dari Supabase, menghitung dua tab dengan fungsi laporan yang sama, lalu mengakses Google OAuth dan Sheets langsung. Tidak memanggil `/api/device/sheets`, `claim_live_sheets`, atau `finish_live_sheets`. Penulisan idempoten, hash dan verifikasi baca ulang tetap berlaku. Data tidak berubah dilewati hingga pemeriksaan ulang 15 menit. Data bulan lama tetap di database; tab live menampilkan bulan berjalan.

Web admin, sinkronisasi transaksi/katalog, dan aktivasi tetap memakai server. Jadi ini memindahkan seluruh pekerjaan Sheets, bukan seluruh backend kasir, ke laptop. Antrean transaksi harus berhasil dikirim sebelum pembaruan sumber cloud.

## Aktivasi sekali

Deploy server dari commit ini dengan konfigurasi Supabase/Google yang sudah ada. Tidak ada migrasi baru. Ekstrak paket Windows lengkap. Instalasi baru menawarkan login pengelola dan PIN. Instalasi lama: Pengaturan → Hubungkan Sheets langsung → login pengelola. Kata sandi login tidak disimpan; konfigurasi koneksi diproteksi DPAPI untuk akun Windows yang sama.

Sesuai penggunaan satu laptop milik pengelola, koneksi langsung memakai service key Supabase dan service account Google milik usaha. Ini adalah kredensial berhak akses tinggi: hanya diberikan setelah password login dan identitas ADMIN_USER_ID diverifikasi, dengan opt-in `directSheets: true`. Token perangkat biasa tidak bisa mengambilnya. Respons no-store; tidak disimpan di repositori, paket distribusi, argumen proses, atau log. Rotasi kunci dilakukan di penyedia kemudian hubungkan ulang laptop.

Buka aplikasi, lakukan transaksi, tunggu antrean kosong, pastikan status "Sheets tersinkron langsung dari laptop" dan cocokkan dua tab. Coba refund berhasil dan pastikan total berkurang sekali. Tutup aplikasi dan pastikan tidak ada worker milik aplikasi yang tertinggal. Kesuksesan CI bukan verifikasi produksi.

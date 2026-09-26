# M5 tahap 1 — Sesi kas dan pengembalian

Checkpoint perangkat lunak; **M5 masih berjalan**. Tahap ini mengerjakan buku kas lokal, persetujuan pengembalian, jurnal permanen dan penerimaan jurnal di server. Rekonsiliasi QRIS, biaya aktual, pencairan, dashboard keuangan admin dan Google Sheets belum selesai. Uji merchant, printer dan touchscreen nyata tetap pada M6.

## Alur kasir

1. Buka **Kas hari ini → Buka kas**, lalu masukkan modal di laci. Pilih **Mulai tanpa modal · Rp0** jika memang tidak ada. Aplikasi tidak menganggap kolom kosong sebagai modal nol.
2. Jika kasir langsung menekan **Bayar** pada pesanan pertama, aplikasi meminta modal, lalu mengembalikan kasir ke pesanan yang sama. Memilih menu dan menyimpan pesanan ditunda tetap dapat dilakukan sebelum kas dibuka.
3. Sesi kas tetap terbuka setelah aplikasi ditutup atau laptop dimulai ulang. Satu laptop hanya mempunyai satu sesi aktif. Pergantian tanggal WIB tidak menutup sesi otomatis.
4. **Kas masuk** untuk tambahan modal atau uang masuk di luar penjualan. **Kas keluar** untuk pengambilan uang atau pembelian dari laci. Keduanya wajib mempunyai nominal positif dan keterangan. Gunakan alur pengembalian untuk refund pelanggan agar tidak tercatat dua kali.
5. **Hitung & tutup kas** meminta uang yang benar-benar dihitung. Aplikasi menampilkan selisih, mewajibkan alasan jika selisih tidak nol, lalu menampilkan halaman pemeriksaan sebelum tombol **Ya, tutup kas**. Hitungan akhir tidak dapat ditimpa.
6. Riwayat memuat 100 sesi terbaru. Rincian sesi memuat 500 catatan terbaru. Seluruh jurnal tetap disimpan. Angka ringkasan dihitung dari seluruh catatan sesi, bukan dari batas tampilan.

Rumus uang laci:

`modal + penjualan tunai + kas masuk − kas keluar − refund tunai berhasil`

Contoh: modal Rp100.000, belanja Rp22.500 dibayar Rp25.000 dan kembalian Rp2.500 → uang laci tercatat Rp122.500. Pembayaran QRIS Rp15.000 tidak mengubah angka laci. Tombol dan judul ringkasan menjelaskan bahwa ini sesi aktif, yang bisa melintasi tengah malam, bukan laporan kalender harian.

Penjualan tercatat masih merupakan nilai kotor pesanan selesai. Total pengembalian dalam sesi ditampilkan terpisah karena bisa berasal dari penjualan sesi sebelumnya. Uang laci bukan saldo rekening atau laba usaha.

## Pengembalian pelanggan

**Riwayat → Lihat rincian → Pengembalian** menyediakan nominal sebagian atau seluruh sisa pesanan, alasan, dan cara pengembalian: tunai dari laci atau transfer/provider.

| Status | Pengaruh |
| --- | --- |
| Menunggu pengelola | Sisa yang bisa diajukan dicadangkan; uang laci belum berkurang |
| Berhasil | Persetujuan PIN dan keterangan disimpan; uang laci berkurang hanya jika cara pengembalian tunai |
| Tidak terlaksana | Alasan dan persetujuan tersimpan; cadangan nominal dilepas; uang laci tidak berubah |

Pengelola membuka **Catat hasil**, mengisi referensi transfer atau keterangan penyerahan, lalu PIN. Sebelum menyimpan sebagai berhasil, pengelola harus mencentang bahwa uang sudah dikembalikan. Aplikasi **mencatat hasil yang dinyatakan pengelola**, belum memanggil API transfer/refund provider atau memverifikasi bukti eksternal otomatis. Jangan menekan berhasil sebelum uang benar-benar diserahkan.

Refund berhasil membutuhkan sesi terbuka dan masuk sesi saat refund dicatat. Refund tunai tidak boleh melebihi kas tercatat. Pengembalian QRIS melalui uang tunai tetap menyimpan metode pembayaran asli sebagai QRIS. Refund yang sudah diputuskan tidak bisa diubah diam-diam; transaksi penjualan asal dan harga historis tetap tersimpan.

## PIN pengelola

Tidak ada PIN bawaan. Paket preview menyertakan `Set-ManagerPin.ps1`; pengelola menjalankannya pada akun Windows yang menjalankan kasir, dengan Windows PowerShell 5.1/.NET Framework 4.7.2+ atau PowerShell 7:

```powershell
.\Set-ManagerPin.ps1
```

Masukkan PIN 6–12 angka dua kali. Script tidak menampilkan PIN atau hash. Konfigurasi hash disimpan pada variabel lingkungan pengguna Windows `WARUNG_MANAGER_PIN_HASH`. Mulai ulang sesi Windows sebelum membuka kasir agar proses baru membaca konfigurasi. Kebijakan eksekusi script organisasi tetap harus diikuti; script ini tidak mengubah atau melewati kebijakan tersebut.

Hash menggunakan PBKDF2-SHA256, salt acak 16 byte, 600.000 iterasi dan hasil 32 byte. Lima kesalahan PIN mengunci persetujuan selama lima menit; penghitung tetap tersimpan setelah aplikasi dimulai ulang. PIN tidak dikirim ke server. PIN ini membatasi tindakan dalam aplikasi; pengamanan akun Windows, hak akses file, distribusi konfigurasi, rotasi dan prosedur pemulihan tetap pekerjaan finalisasi M6. Orang yang bisa mengganti konfigurasi aplikasi pada akun Windows tersebut juga bisa mengganti hash PIN.

## Penyimpanan dan sinkronisasi

- Perubahan sesi kas, pengembalian dan penjualan ditulis dalam transaksi SQLite yang sama dengan jurnal dan antrean keuangannya. Gagal menulis antrean berarti seluruh tindakan dibatalkan.
- Identitas tindakan stabil membuat percobaan ulang tidak menggandakan uang. Jurnal tidak dapat diperbarui atau dihapus melalui operasi SQL biasa. Status kirim disimpan terpisah.
- Antrean pesanan dan keuangan berbeda. Laptop mengirim pesanan dahulu, lalu maksimal 50 catatan keuangan berurutan ke `/api/device/finance`. Jika snapshot pesanan yang diperlukan belum sampai, server menolak batch dan laptop menyimpannya untuk dicoba lagi.
- Server memeriksa urutan, identitas, sesi aktif, pembayaran asal, batas refund dan saldo laci. Batch yang gagal tidak diakui sebagian. Catatan dengan ID yang sama dan isi berbeda ditolak.
- Footer dan heartbeat menghitung gabungan perubahan pesanan serta keuangan yang belum dikirim. Satu pesanan dapat menghasilkan beberapa perubahan; angka antrean bukan jumlah penjualan.
- Pengiriman data tidak menahan kasir saat menerima pesanan. Polling notifikasi QRIS tetap terpisah. Popup pasif, suara, bukti provider dan status pesanan tidak diubah oleh fitur kas.

Untuk server yang sudah mempunyai migrasi 001 dan 002, terapkan `database/003_finance.sql` melalui pengelola database sebelum memakai endpoint baru. Tidak ada kredensial database pada desktop. Migration ini belum dijalankan ke akun Supabase pengguna karena konfigurasi cloud nyata masih terbuka pada M3.

## Upgrade database lokal

Schema lokal naik dari versi 1 ke 2. Sebelum migrasi versi 1, aplikasi membuat backup SQLite konsisten di samping database dengan nama `warung-rafi.db.before-v2-<id>.db`. Jika backup gagal, migrasi tidak diteruskan. Restart pada versi 2 tidak membuat backup migrasi baru.

Pesanan, pembayaran, antrean lama dan bukti QRIS dipertahankan. Transaksi lama tetap tampil pada riwayat. Aplikasi tidak mengarang modal awal atau menempatkan penjualan sebelum migrasi ke sesi kas baru. Laporan lintas versi harus mempertahankan perbedaan ini saat pelaporan M5 dilanjutkan. Backup ini khusus migrasi, belum menggantikan backup berkala atau fitur pemulihan M6.

## Verifikasi

`WarungRafi.FinanceChecks` mencakup saldo laci/kembalian, sesi tunggal, refund sebagian, retry, PIN/cooldown, rollback saat antrean gagal, jurnal permanen, restart dan backup migrasi. Dengan `--write-contract`, tes menghasilkan fixture serializer C# yang dipakai tes API dan PostgreSQL; fixture hanya data uji.

Tes WPF menjalankan klik kas keluar, refund, pemeriksaan tutup kas, kembali mengubah hitungan, penutupan berselisih dan pembukaan kas dari pembayaran. Screenshot otomatis mencakup layar kas pada 1280×720 dan 900×620 DIP. Ini belum mengukur kenyamanan sentuh di perangkat nyata.

Review screenshot awal menemukan tombol kas masuk/keluar ikut tergulir pada 900×620. Revisi lokal memasang kedua tombol di bagian bawah panel, menambah jarak label dan nominal, serta merapikan kolom PIN. Build lulus; enam pemeriksaan visibilitas tambahan dan render revisi ini masih menunggu Windows CI.

Hasil CI untuk commit yang diverifikasi dicatat di [STATUS.md](STATUS.md). Tes API memakai service fixture terisolasi; tes SQL memakai PostgreSQL sungguhan di CI. Keduanya belum membuktikan integrasi merchant atau akun cloud produksi.

## Tahap M5 berikutnya

1. Dashboard keuangan admin dan rekonsiliasi eksplisit bukti provider terhadap catatan pembayaran.
2. Profil biaya bertanggal berlaku, biaya estimasi terpisah dari biaya aktual, dan pencairan dengan bukti.
3. Proyeksi laporan terperinci, ekspor CSV/Google Sheets, retry dan pemeriksaan kelengkapan.

Tarif 0,7% tidak ditetapkan sebagai biaya aktual. Helper perhitungan estimasi diuji dengan tarif contoh yang diberikan sebagai input; belum digunakan untuk membebankan biaya atau mengubah total penjualan.

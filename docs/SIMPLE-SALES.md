# Spreadsheet kasir sederhana

Ekspor baru berisi dua tab. Pembaruan otomatis memakai nama tetap `Riwayat Penjualan` dan `Rekap Penjualan`, untuk bulan berjalan (WIB). Ekspor laporan manual tetap memiliki awalan ID laporan supaya arsip periode lain tidak tertimpa.

Riwayat: satu baris per menu, dengan waktu bayar, nomor pesanan, pcs, harga satuan, subtotal, metode pembayaran, jenis, status, dan keterangan. Pembatalan bernilai nol. Refund berhasil adalah baris negatif pada tanggal uang dikembalikan; refund menunggu/gagal bernilai nol. Refund pesanan bulan sebelumnya tetap mengurangi bulan penyelesaiannya. Jika pesanan asal berada di luar periode, ID asal ditampilkan.

Rekap: jumlah pesanan dihitung dari transaksi, bukan banyak baris menu. Ada total sebelum refund, refund berhasil, setelah refund, total pcs, pembayaran tunai/QRIS, per hari, per menu, serta rincian kas masuk/keluar. Kas masuk/keluar tidak mengubah total penjualan. Refund nominal tidak dapat digunakan untuk menebak pcs atau menu yang dikembalikan. Total setelah refund bukan laba.

Rekap dihitung ulang dari sumber yang sama setiap pembaruan, sehingga otomatis mengikuti transaksi baru tanpa rumus atau baris total di Riwayat. Data buatan pengguna ditulis sebagai teks, bukan formula.

Tab otomatis lama WR_live_* disembunyikan setelah kedua tab baru selesai ditulis dan diverifikasi. Isinya tidak dihapus. Arsip laporan manual dan tab buatan pengguna tetap tersedia.

Aplikasi mempertahankan Jualan, Ditunda, Riwayat, Kas, refund, cetak ulang, dan backup/pemulihan. Pengaturan rutin menampilkan printer dan PIN; alamat server/token/pemeriksaan koneksi dipindahkan ke Koneksi lanjutan. Refund dilakukan dari Riwayat > rincian > Pengembalian; status berhasil hanya dicatat setelah uang benar-benar diserahkan dan PIN pengelola diverifikasi.

Penerapan memerlukan deployment server dari commit ini dan aplikasi Windows hasil build commit ini. Paket terbaru menjalankan pemrosesan Sheets langsung dari laptop; lihat DIRECT-SHEETS.md. Kesuksesan CI bukan verifikasi spreadsheet produksi.

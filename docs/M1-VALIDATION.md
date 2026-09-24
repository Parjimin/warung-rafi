# M1 — integritas dan pemulihan data lokal

Suite `tests/WarungRafi.RecoveryChecks` melengkapi pemeriksaan domain yang sudah ada. Seluruh database dibuat dalam direktori sementara `WarungRafiRecovery-<GUID>` dan dihapus setelah tes. Suite tidak membuka database kasir pengguna dan tidak menghubungi layanan pembayaran.

## Cakupan verifikasi

| Skenario | Cara pengujian | Hasil yang diwajibkan |
| --- | --- | --- |
| Proses mati ketika menyimpan perubahan pesanan | Proses anak menjalankan `LocalStore.SaveAsync`; parent membunuhnya di dalam transaksi SQLite | Draf lama utuh; perubahan dan event yang belum commit dibatalkan |
| Proses mati sebelum commit pembayaran | Proses anak dibunuh setelah INSERT pembayaran tetapi sebelum commit | Pesanan tetap belum dibayar; pembayaran dan outbox parsial tidak ada |
| Proses mati setelah commit pembayaran | Proses anak menyelesaikan transaksi, lalu dibunuh sebelum shutdown normal | Penjualan, pembayaran, dan outbox bertahan; retry menghasilkan transaksi yang sama |
| Proses mati saat mengakui outbox | Parent membunuh proses anak di tengah update acknowledgment | Event belum diakui tetap dapat dikirim ulang |
| Ruang database habis | Batasi `max_page_count` pada koneksi uji lalu minta SQLite mengalokasikan data tambahan dalam transaksi pembayaran | Engine mengembalikan `SQLITE_FULL` (13), penjualan lama utuh, pembayaran gagal tidak setengah tersimpan, retry normal berhasil |
| Penyelesaian serentak | Delapan instance `LocalStore` memakai koneksi terpisah dan menyelesaikan order yang sama | Hanya satu pembayaran dan satu event selesai |
| Riwayat lebih dari batas tampilan | Fixture 1.025 penjualan ditambah draf/pesanan ditunda yang lebih lama | Pesanan lama masih ditemukan; jumlah dan ringkasan harian tidak berhenti di 1.000 |
| Antrean dan respons hilang | Ambil batch 50, buka ulang, akui sebagian, ulang acknowledgment, habiskan antrean | Urutan stabil; event yang belum diakui tidak hilang atau terlewat |
| Pergantian tanggal WIB | Fixture sebelum, tepat pada, dan sesudah batas tengah malam WIB | Ringkasan memasukkan transaksi pada tanggal yang benar tanpa bergantung zona waktu OS |
| Input invalid | Metode/status tidak dikenal dan jumlah negatif dikirim ke batas penyimpanan | Ditolak tanpa pembayaran atau event tambahan |
| Database dari aplikasi lebih baru | Naikkan penanda schema pada database uji | Inisialisasi ditolak dan penanda/schema/data tidak ditimpa |

## Mengapa proses benar-benar dihentikan

Proses anak memiliki titik tunggu yang dipasang melalui fungsi dan TEMP trigger SQLite pada koneksi uji. Parent menunggu penanda kesiapan, lalu memanggil `Process.Kill` untuk proses anak yang baru dibuatnya. Ini bukan `Dispose` normal atau sekadar membuat instance `LocalStore` kedua.

Koneksi pembaca independen menjaga WAL tetap tersedia; ukuran cache kecil serta alokasi tambahan dalam transaksi memaksa frame ditulis ke WAL sebelum proses dihentikan. Tes memeriksa frame tersebut dan menjalankan `integrity_check` serta `foreign_key_check` sesudah membuka ulang. Hook pengujian hanya internal untuk assembly tes, tidak diaktifkan oleh kasir, dan koneksinya tidak masuk connection pool aplikasi.

## Batas pembuktian

Simulasi kapasitas menggunakan batas halaman SQLite untuk menghasilkan error asli `SQLITE_FULL`; suite tidak memenuhi seluruh SSD atau mengubah partisi laptop. Process-kill membuktikan recovery pada tingkat proses dan transaksi, bukan jaminan terhadap kerusakan SSD, driver, atau perangkat yang mengabaikan flush saat listrik padam. Backup/restore operasional tetap bagian M6.

Fixture 1.025 transaksi digunakan untuk menguji pembacaan dan antrean; ini bukan hasil benchmark transaksi per detik. Batas tampilan 1.000 masih ada pada daftar riwayat; pagination UI merupakan pekerjaan M2. Penghitung dan ringkasan harian tidak memakai batas itu.

## Menjalankan

```powershell
dotnet run --project tests/WarungRafi.Checks -c Release
dotnet run --project tests/WarungRafi.RecoveryChecks -c Release
```

CI Windows menjalankan keduanya sebelum build dan publish preview. Hasil tercatat pada langkah **Verify crash recovery, full storage and backlog**. Jangan menyatakan M1 selesai sebelum hasil suite dan CI diperiksa.

Referensi mekanisme: [SQLite maximum database pages](https://www.sqlite.org/limits.html), [max_page_count](https://www.sqlite.org/pragma.html#pragma_max_page_count), [cache_spill](https://www.sqlite.org/pragma.html#pragma_cache_spill).

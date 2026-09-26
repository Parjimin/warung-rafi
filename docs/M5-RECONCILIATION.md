# M5 tahap 2 — rekonsiliasi dan rekening

Halaman **Keuangan** (`/keuangan`) tersedia bagi akun pengelola yang sama dengan katalog. Terapkan migrasi `004_reconciliation.sql` setelah 001–003, lalu deploy admin. Tahap ini belum mengaktifkan Supabase/Vercel produksi atau akun merchant asli.

## Cara memakai

1. Pilih tanggal laporan, maksimal 31 hari, menurut WIB. Periksa peringatan sinkronisasi laptop sebelum menilai kelengkapan omzet.
2. Buka **Cocokkan**. Pilih satu pesanan QRIS dan satu bukti provider secara eksplisit. Dua pembayaran bernominal sama tetap dua bukti. Tinjau identitas, nilai, dan alasan sebelum menyimpan. Selisih nominal memerlukan centang pengakuan; hubungan dapat dilepas dengan alasan dan riwayat permanen. Pilihan tetap tersedia saat berpindah halaman/periode untuk pencocokan lintas tanggal.
3. Buka **Biaya**. Tarif estimasi diisi dari perjanjian merchant, dengan masa berlaku, basis poin (100 = 1%), biaya tetap dan aturan pembulatan. Tidak ada tarif komersial bawaan. Estimasi mengikuti tanggal pembayaran WIB dan disimpan sekali sebagai snapshot. Biaya aktual, biaya lain, serta refund provider diisi dari laporan beserta referensinya. Kolom kosong bukan nol; isi 0 jika laporan menyatakan nol.
4. Buka **Pencairan**. Pilih 1–50 bukti dari merchant yang sama dan telah memiliki biaya aktual. Isi referensi/tanggal laporan, nama bank, empat digit terakhir rekening, biaya pencairan, penyesuaian bertanda dan alasannya. Angka bruto, biaya transaksi dan refund dihitung server dari bukti yang dipilih. Ini pencatatan laporan, bukan transfer dana.
5. Sesudah mutasi bank tersedia, pilih **Catat mutasi bank**. Isi tanggal, nominal aktual dan referensi mutasi. Selisih dari neto laporan tetap ditampilkan. Status settlement saja tidak membuat catatan masuk rekening.
6. Koreksi mutasi dapat dibuat dengan alasan baru. Untuk mengubah biaya transaksi yang sudah dialokasikan, batalkan catatan pencairannya terlebih dahulu, koreksi biaya, lalu buat catatan pengganti. Pembatalan mengeluarkan pencairan dan konfirmasi banknya dari total aktif, melepas alokasi bukti, serta mempertahankan nilai sebelum/sesudah di **Jejak audit**.

## Arti angka

| Angka | Sumber / tanggal |
| --- | --- |
| Omzet, tunai, QRIS | Snapshot pesanan selesai; tanggal pembayaran WIB |
| Pengembalian selesai | Refund kasir berstatus selesai; tanggal pengembalian WIB |
| Bukti QRIS | Bukti settlement/refund/partial_refund provider; tanggal pembayaran asli WIB |
| Biaya aktual / estimasi / refund provider | Biaya yang dicatat bagi bukti dalam periode pembayaran tersebut |
| Neto pencairan | Bruto − biaya transaksi − refund provider − biaya pencairan + penyesuaian; tanggal laporan pencairan |
| Masuk rekening | Nominal mutasi bank pada pencairan aktif; tanggal masuk rekening |

Omzet tidak ditambah lagi dengan bukti QRIS atau pencairan. Refund kasir dan refund pada laporan provider adalah dua sumber yang direkonsiliasi, bukan dua pengurang otomatis. Laporan tidak menyatakan laba. Biaya aktual yang belum diketahui ditampilkan sebagai jumlah bukti yang belum lengkap; estimasi tidak menggantikannya. Setiap daftar berhalaman 50 catatan; total dihitung atas seluruh periode, bukan halaman yang terlihat.

Webhook refund tidak boleh menggeser tanggal pembayaran ke tanggal refund. Migrasi 004 memulihkan timestamp pembayaran dari inbox settlement permanen bila tersedia; untuk transaksi tanpa inbox, timestamp tersimpan dipertahankan. Trigger berikutnya menjaga tanggal awal setelah pembayaran terverifikasi.

## Penyimpanan dan pemulihan

Semua perubahan memakai ID perintah, revisi optimistis, akun dari sesi server, dan alasan. Simpan projection serta jurnal audit dalam satu transaksi. Kirim ulang ID dan isi yang sama mengembalikan hasil lama, tanpa penulisan kedua. ID yang dipakai ulang dengan isi/pelaku berbeda ditolak. Revisi lama dari tab lain meminta muat ulang; isian yang sedang diperiksa tetap ada.

Jika respons hilang, halaman menyimpan perintah tertunda dalam session storage dan menawarkan **Kirim ulang catatan**, termasuk setelah halaman dimuat ulang pada tab yang sama. Jangan mengganti isi perintah saat statusnya belum terkonfirmasi. Menutup tab/browser dapat menghilangkan session storage: periksa jejak audit sebelum membuat perintah baru. API menolak sesi tidak sah, asal lintas situs, format/nominal/tanggal tidak valid. Browser tidak menerima service-role key. Tabel dilindungi RLS; jurnal dan profil tarif tidak bisa diubah/hapus.

## Batas tahap ini

- Biaya dan pencairan dimasukkan manual dari laporan provider dan mutasi bank; belum mengimpor CSV atau menghubungi API laporan/bank. Referensi berupa teks, belum lampiran dokumen.
- Tarif yang disimpan bersifat tetap, periode satu merchant tidak boleh tumpang tindih. UI koreksi/penonaktifan profil salah belum tersedia; periksa tarif sebelum menyimpan. Estimasi adalah snapshot; aktual masih dapat dikoreksi dengan audit.
- Alokasi satu bukti utuh ke satu pencairan aktif; pencairan sebagian satu bukti belum didukung. Koreksi setelah alokasi menggunakan pembatalan dan pencatatan pengganti.
- Jejak audit pada UI menampilkan 50 perubahan terakhir dari semua periode. Riwayat lengkap tetap berada di database; ekspor/paginasi audit lengkap menjadi pekerjaan pelaporan berikutnya.
- Google Sheets tetap tahap 3 / issue #11. Aktivasi cloud M3, uji merchant serta perangkat M6 tetap terbuka.

## Verifikasi checkpoint

Lokal: 22 tes unit admin, typecheck dan build Next lulus; migrasi 001–004 beserta lima suite SQL lulus PostgreSQL 18.3 WASM. Pengujian mencakup identitas/retry, revisi lama, pencocokan bernominal sama/selisih, biaya belum diketahui, snapshot estimasi, alokasi ganda, neto dan mutasi bank, pembatalan, tanggal WIB, pagination, akses service-role dan rollback jika penulisan jurnal gagal. Bukti CI PostgreSQL 17 serta browser akan dicatat setelah run checkpoint selesai.

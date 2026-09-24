# Keputusan arsitektur awal

1. **Desktop lokal dahulu.** C# domain tidak bergantung WPF. Semua uang penjualan disimpan sebagai integer rupiah dan dihitung checked; biaya persentase nanti memakai decimal.
2. **SQLite sebagai catatan lokal.** Snapshot pesanan dan outbox disimpan dalam satu transaksi. Pembayaran memiliki constraint satu pembayaran per pesanan. Versi optimistis mencegah penulisan draf lama di atas pesanan selesai.
3. **Penyelesaian idempoten.** Percobaan menyelesaikan pesanan yang sudah selesai menghasilkan data tersimpan yang sama, bukan pembayaran kedua. Pembatalan final tidak boleh diselesaikan lagi.
4. **QRIS manual di kasir.** Tombol selesai menyimpan pernyataan pembayaran kasir. Ia tidak membuat charge dan tidak memilih bukti Midtrans berdasarkan nominal.
5. **Webhook fail closed.** Signature diperiksa, lalu data transaksi diambil ulang melalui Status API Midtrans untuk memastikan merchant, nominal, mata uang, identitas, dan status. Hanya data server-ke-server yang terverifikasi disimpan sebagai bukti.
6. **Penyimpanan persisten sebelum respons sukses.** PostgreSQL RPC memakai ID penyedia unik serta event notifikasi unik. Server tidak mengakui webhook jika database gagal.
7. **Pencocokan bukan popup.** Satu transaksi masuk menghasilkan satu notifikasi; order tidak berubah karenanya. Rekonsiliasi admin merupakan milestone berikutnya.
8. **API perangkat berotorisasi.** Token perangkat wajib, rahasia server tidak disimpan di desktop. Setup awal menggunakan environment variable; pengelolaan kredensial Windows yang terlindungi masuk M6.
9. **Admin fail closed.** Login admin menggunakan Supabase Auth dan cookie HTTP-only berisi token akses maksimal satu jam. Setiap API mengonfirmasi user melalui Auth dan allowlist `ADMIN_USER_ID`; mutasi memeriksa Origin. Tidak ada password default. Pengelolaan banyak pengguna/role masuk M6.
10. **Belum produksi.** CI Windows, migrasi pada database uji, perangkat, dan merchant nyata adalah gate sebelum penggunaan usaha.

Sumber integrasi: [Midtrans webhook](https://docs.midtrans.com/docs/https-notification-webhooks), [Status API](https://docs.midtrans.com/docs/get-status-api-requests), [GoPay Static QRIS](https://docs.midtrans.com/docs/gopay-static-qris), [Next.js route handlers](https://nextjs.org/docs/app/getting-started/route-handlers).

11. **Cursor mengikuti urutan commit.** Penyimpanan notifikasi provider diserialisasi dengan advisory transaction lock. Identity sequence saja tidak menjamin urutan commit; tanpa lock polling dapat melewatkan transaksi konkuren.
12. **Foto dan cetak tidak menghambat input.** Foto dimuat asinkron lalu di-cache lokal; spooler berjalan pada thread STA terpisah. Commit penjualan tidak menunggu keduanya.

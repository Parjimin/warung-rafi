using Microsoft.Data.Sqlite;
using WarungRafi.Core;
using WarungRafi.Storage;

var passed=0;
void Check(bool condition,string name) { if(!condition) throw new Exception(name); Console.WriteLine($"PASS {name}"); passed++; }
void Reject(Action action,string name) { try { action(); } catch(InvalidOperationException) { Check(true,name); return; } throw new Exception(name); }
async Task RejectAsync(Func<Task> action,string name) { try { await action(); } catch(InvalidOperationException) { Check(true,name); return; } throw new Exception(name); }
var product=new Product("p","Nasi Sayur","Nasi",8000);
var order=OrderRules.Add(Order.New(),product);
order=OrderRules.Add(order,product with { Price=10000 });
Check(order.Total==16000,"harga snapshot dipertahankan ketika jumlah ditambah");
Reject(()=>OrderRules.Complete(order,PaymentMethod.Cash,15000),"tolak uang kurang");
var sale=OrderRules.Complete(order,PaymentMethod.Cash,20000);
Check(sale.Payment.Change==4000,"hitung kembalian tepat");
Reject(()=>OrderRules.Add(sale.Order,product),"pesanan selesai tidak dapat diedit");
Check(OrderRules.Complete(order,PaymentMethod.QrisManual,0).Payment.Change==0,"QRIS manual bukan uang tunai");
Check(OrderRules.Hold(order,"Budi").Id==order.Id,"menunda mempertahankan identitas");
var path=Path.Combine(Path.GetTempPath(),$"warung-rafi-{Guid.NewGuid():N}.db");
var store=new LocalStore(path); await store.InitializeAsync();
try
{
    order=await store.SaveAsync(order);
    var stale=order;
    order=await store.SaveAsync(OrderRules.Add(order,product));
    await RejectAsync(async()=>{ await store.SaveAsync(stale); },"tolak draf lama menimpa versi baru");
    var completed=await store.CompleteAsync(order.Id,order.Version,PaymentMethod.Cash,30000);
    var again=await store.CompleteAsync(order.Id,order.Version,PaymentMethod.Cash,30000);
    Check(completed.Order.Version==again.Order.Version,"retry selesai tidak membuat pembayaran baru");
    Check((await store.PendingAsync()).Length==3,"satu event outbox per perubahan termasuk selesai");
    var reopened=new LocalStore(path);
    Check((await reopened.SaleAsync(order.Id))!.Payment.Amount==24000,"transaksi bertahan setelah instance dibuka ulang");
    var proof=new ProviderPayment(1,"provider-1",24000,DateTimeOffset.UtcNow);
    Check((await store.ReceivePaymentsAsync([proof])).Length==1,"bukti baru diterima satu kali");
    Check((await store.ReceivePaymentsAsync([proof])).Length==0,"bukti ulang tidak membuat notifikasi baru");
    Check(await store.CursorAsync()==1,"cursor inbox tersimpan");
    Check((await store.ListAsync(OrderStatus.Completed)).Length==1,"bukti QRIS tidak membuat penjualan kedua");
    Check(await store.ReceiveCatalogAsync(new CatalogSnapshot(1,[product with { Price=10000 }])) ,"katalog baru disimpan");
    Check(!await store.ReceiveCatalogAsync(new CatalogSnapshot(1,[product])) ,"katalog lama tidak menimpa versi baru");
    Check((await reopened.CatalogAsync()).Products[0].Price==10000,"katalog bertahan setelah dibuka ulang");
    Check((await store.SaleAsync(order.Id))!.Order.Total==24000,"publikasi katalog tidak mengubah pesanan selesai");
    var summary=await store.DailySalesAsync(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).Date);
    Check(summary.Cash==24000 && summary.Count==1,"ringkasan WIB menjumlah transaksi selesai");
    var draft=await store.SaveAsync(OrderRules.Add(Order.New(),product));
    using(var connection=new SqliteConnection($"Data Source={path}"))
    {
        connection.Open(); using var cmd=connection.CreateCommand();
        cmd.CommandText="CREATE TRIGGER simulate_disk_failure BEFORE INSERT ON outbox BEGIN SELECT RAISE(ABORT,'simulated write failure'); END;";
        cmd.ExecuteNonQuery();
    }
    try { await store.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,10000); throw new Exception("failure injection did not fail"); }
    catch(SqliteException) { }
    Check(await store.SaleAsync(draft.Id)==null,"kegagalan outbox rollback pembayaran");
    Check((await store.ListAsync(OrderStatus.Draft)).Any(x=>x.Id==draft.Id),"kegagalan outbox rollback status selesai");
    var receipt=Receipt.Build(completed,true);
    Check(receipt.Any(x=>x.Text.Contains("SALINAN")),"cetak ulang diberi penanda");
    Check(receipt.Any(x=>x.Text.Contains("Rp8.000")),"struk menggunakan harga historis");
}
finally
{
    SqliteConnection.ClearAllPools();
    foreach(var file in new[]{path,path+"-wal",path+"-shm"}) if(File.Exists(file)) File.Delete(file);
}
Console.WriteLine($"{passed} checks passed.");

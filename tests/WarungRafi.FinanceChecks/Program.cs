using System.Text.Json;
using Microsoft.Data.Sqlite;
using WarungRafi.Core;
using WarungRafi.Storage;

var folder=Path.Combine(Path.GetTempPath(),"warung-finance-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var path=Path.Combine(folder,"test.db");const string pin="728491";var hash=ManagerPin.Hash(pin);var db=new LocalStore(path,hash);var count=0;
string Id()=>Guid.NewGuid().ToString("N");
void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
async Task Reject<T>(Func<Task> action,string name) where T:Exception {try{await action();}catch(T){Check(true,name);return;}throw new Exception("Expected failure: "+name);}
object? Sql(string query,string? at=null){using var c=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=at??path,Pooling=false}.ToString());c.Open();using var cmd=c.CreateCommand();cmd.CommandText=query;return cmd.ExecuteScalar();}
async Task<Order> Draft(long price=22500)=>await db.SaveAsync(OrderRules.Add(Order.New(),new Product("food","Nasi uji","Nasi",price)));
try
{
    await db.InitializeAsync();var draft=await Draft();
    await Reject<InvalidOperationException>(()=>db.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,25000),"sale requires an open session");
    Check(await db.SaleAsync(draft.Id) is null&&await db.PendingFinanceCountAsync()==0,"missing session leaves no payment or journal");
    var session=await db.OpenCashAsync(Id(),100000);await db.OpenCashAsync(session.Id,100000);
    Check(await db.PendingFinanceCountAsync()==1,"opening retry is idempotent");
    await Reject<InvalidOperationException>(()=>db.OpenCashAsync(session.Id,1),"opening identity cannot change amount");
    await Reject<InvalidOperationException>(()=>new LocalStore(path).OpenCashAsync(Id(),0),"second connection cannot open another session");
    var sale=await db.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,25000);
    Check((await db.ActiveCashAsync())!.Expected==122500&&sale.Payment.Change==2500,"drawer adds sale net of change, not tendered");
    await db.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,25000);Check(await db.PendingFinanceCountAsync()==2,"completion retry creates one finance event");
    var qris=await Draft(15000);await db.CompleteAsync(qris.Id,qris.Version,PaymentMethod.QrisManual,0);
    Check((await db.ActiveCashAsync()) is {Expected:122500,QrisSales:15000,CashSales:22500,SaleCount:2},"QRIS sales do not increase drawer");
    var movement=Id();await db.RecordCashMovementAsync(movement,session.Id,false,10000,"Beli es batu");await db.RecordCashMovementAsync(movement,session.Id,false,10000,"Beli es batu");
    await db.RecordCashMovementAsync(Id(),session.Id,true,5000,"Tambahan modal");
    Check((await db.ActiveCashAsync()) is {Expected:117500,Gross:37500,CashIn:5000,CashOut:10000},"cash movements are not sales and retry does not double expense");
    await Reject<InvalidOperationException>(()=>db.RecordCashMovementAsync(movement,session.Id,false,9000,"Beli es batu"),"movement ID cannot be reused");
    await Reject<InvalidOperationException>(()=>db.RecordCashMovementAsync(Id(),session.Id,false,999999,"Kas keluar"),"cannot spend more than recorded cash");
    await Reject<ArgumentException>(()=>db.RecordCashMovementAsync(Id(),session.Id,false,0,"Kas keluar"),"zero cash movement is rejected");
    var refund=await db.RequestRefundAsync(Id(),draft.Id,5000,RefundChannel.Cash,"Satu item batal");
    Check((await db.ActiveCashAsync())!.Expected==117500,"requested refund does not reduce cash");
    await db.RequestRefundAsync(refund.Id,draft.Id,5000,RefundChannel.Cash,"Satu item batal");
    await Reject<InvalidOperationException>(()=>db.RequestRefundAsync(Id(),draft.Id,18000,RefundChannel.Cash,"Kelebihan"),"pending refund reserves refundable balance");
    await Reject<InvalidOperationException>(()=>db.ResolveRefundAsync(refund.Id,true,"111111","Diserahkan ke pelanggan"),"incorrect manager PIN cannot approve");
    Check((await db.RefundsAsync(draft.Id)).Single().State==RefundState.Requested,"failed authorization leaves refund pending");
    await db.ResolveRefundAsync(refund.Id,true,pin,"Diserahkan ke pelanggan");await db.ResolveRefundAsync(refund.Id,true,pin,"Diserahkan ke pelanggan");
    Check((await db.ActiveCashAsync()) is {Expected:112500,CashRefunds:5000,SalesRefunds:5000},"cash refund and retry affect cash exactly once");
    Check((await db.SaleAsync(draft.Id))!.Payment==sale.Payment,"refund preserves original payment");
    var failed=await db.RequestRefundAsync(Id(),draft.Id,17500,RefundChannel.External,"Permintaan pelanggan");
    await db.ResolveRefundAsync(failed.Id,false,pin,"Pelanggan membatalkan");
    Check((await db.ActiveCashAsync())!.Expected==112500,"failed refund has no cash effect");
    var replacement=await db.RequestRefundAsync(Id(),draft.Id,17500,RefundChannel.External,"Permintaan pelanggan");
    await db.ResolveRefundAsync(replacement.Id,true,pin,"Referensi bank UJI-123");
    Check((await db.ActiveCashAsync()) is {Expected:112500,SalesRefunds:22500,CashRefunds:5000},"external refund records sales return without reducing drawer");
    await Reject<InvalidOperationException>(()=>db.RequestRefundAsync(Id(),draft.Id,1,RefundChannel.Cash,"Tambahan refund"),"completed refunds cannot exceed original sale");
    await Reject<InvalidOperationException>(()=>db.ResolveRefundAsync(refund.Id,false,pin,"Diubah kembali"),"completed refund cannot be reversed silently");
    await Reject<InvalidOperationException>(()=>new LocalStore(path).ResolveRefundAsync(refund.Id,true,pin,"Diserahkan ke pelanggan"),"unconfigured manager approval fails closed");
    await Reject<ArgumentException>(()=>db.CloseCashAsync(session.Id,112000,""),"variance needs a reason");
    var closed=await db.CloseCashAsync(session.Id,112000,"Selisih hitung Rp500");await db.CloseCashAsync(session.Id,112000,"Selisih hitung Rp500");
    Check(closed.Session.Difference==-500&&closed.Session.ExpectedAtClose==112500&&await db.ActiveCashAsync() is null,"close preserves counted cash, expected cash and variance");
    await Reject<InvalidOperationException>(()=>db.CloseCashAsync(session.Id,112500,""),"closed count cannot be rewritten");
    var held=await db.SaveAsync(OrderRules.Hold(await Draft(8000),"Pak Dedi"));
    var second=await db.OpenCashAsync(Id(),20000);var qr=await db.RequestRefundAsync(Id(),qris.Id,15000,RefundChannel.Cash,"QRIS dikembalikan tunai");await db.ResolveRefundAsync(qr.Id,true,pin,"Tunai diserahkan pengelola");
    Check((await db.ActiveCashAsync()) is {Expected:5000,Gross:0,CashRefunds:15000},"refund from prior session lands in current session");
    Check((await db.SaleAsync(qris.Id))!.Payment.Method==PaymentMethod.QrisManual,"QRIS-to-cash refund preserves original method");
    Check((await db.OrderAsync(held.Id))!.Status==OrderStatus.Held,"session changes do not cancel or complete held orders");
    var resumed=await db.SaveAsync(OrderRules.Resume(held));await db.CompleteAsync(resumed.Id,resumed.Version,PaymentMethod.Cash,10000);
    Check((await db.ActiveCashAsync())!.Expected==13000,"held order is recorded in session when paid");
    // Fail after payment/order writes but before finance-outbox insertion: all effects must roll back.
    var atomic=await Draft(1000);var before=await db.PendingFinanceCountAsync();var ordersBefore=await db.PendingCountAsync();
    Sql("CREATE TRIGGER fail_finance_outbox BEFORE INSERT ON finance_outbox BEGIN SELECT RAISE(ABORT,'fixture write failure'); END;");
    await Reject<SqliteException>(()=>db.CompleteAsync(atomic.Id,atomic.Version,PaymentMethod.Cash,1000),"finance outbox failure aborts sale transaction");
    Check(await db.SaleAsync(atomic.Id) is null&&(await db.OrderAsync(atomic.Id))!.Status==OrderStatus.Draft&&await db.PendingFinanceCountAsync()==before&&await db.PendingCountAsync()==ordersBefore,"rollback leaves no partial sale, payment, journal or queues");
    Sql("DROP TRIGGER fail_finance_outbox;");await db.CompleteAsync(atomic.Id,atomic.Version,PaymentMethod.Cash,1000);
    await Reject<SqliteException>(()=>Task.Run(()=>Sql("UPDATE finance_events SET amount=0")),"journal refuses updates");
    await Reject<SqliteException>(()=>Task.Run(()=>Sql("DELETE FROM finance_events")),"journal refuses deletion");
    var entries=await db.PendingFinanceAsync();Check(entries.Select(e=>e.Sequence).SequenceEqual(Enumerable.Range(1,entries.Length).Select(x=>(long)x)),"journal sequence is contiguous after rollback");
    if(args.Contains("--write-contract"))
    {
        var sales=new List<CompletedSale>();foreach(var order in await db.ListAsync(OrderStatus.Completed))sales.Add((await db.SaleAsync(order.Id))!);
        Directory.CreateDirectory("artifacts");await File.WriteAllTextAsync("artifacts/finance-contract.json",JsonSerializer.Serialize(new {events=entries,sales},new JsonSerializerOptions(JsonSerializerDefaults.Web))+"\n");
    }
    Check(!JsonSerializer.Serialize(entries).Contains(hash)&&!JsonSerializer.Serialize(entries).Contains(pin),"journal contains no manager PIN or hash");
    await db.AcknowledgeFinanceAsync(entries.Take(2).Select(e=>e.Id).ToArray());
    Check(await db.PendingFinanceCountAsync()==entries.Length-2,"acknowledgement removes only accepted entries from queue");
    var reopened=new LocalStore(path,hash);await reopened.InitializeAsync();Check((await reopened.ActiveCashAsync())!.Expected==14000,"drawer survives offline restart");
    for(var i=0;i<5;i++)await Reject<InvalidOperationException>(()=>reopened.ResolveRefundAsync(refund.Id,true,"111111","Diserahkan ke pelanggan"),"wrong PIN attempt "+(i+1));
    await Reject<InvalidOperationException>(()=>new LocalStore(path,hash).ResolveRefundAsync(refund.Id,true,pin,"Diserahkan ke pelanggan"),"PIN cooldown persists across app restart");
    Check(ManagerPin.Verify(hash,pin)&&!ManagerPin.Verify("broken",pin)&&ManagerPin.Hash(pin)!=hash,"PIN hash is salted and malformed hashes fail closed");
    // Simulate a v1 database by removing only v2 structures; retain real order/payment data.
    await db.CloseCashAsync(second.Id,14000,"");Sql("DROP TRIGGER finance_no_delete; DROP TRIGGER finance_no_update; DROP TABLE finance_outbox; DROP TABLE finance_events; DROP TABLE refunds; DROP TABLE cash_sessions; PRAGMA user_version=1;");
    await reopened.InitializeAsync();var backup=Directory.GetFiles(folder,"*.before-v2-*.db").Single();
    Check(Convert.ToInt64(Sql("PRAGMA user_version",backup))==1&&Convert.ToInt64(Sql("SELECT COUNT(*) FROM payments",backup))==4,"pre-migration backup retains v1 payments and schema version");
    Check(Convert.ToInt64(Sql("PRAGMA user_version"))==2&&(await reopened.SaleAsync(draft.Id))!.Payment==sale.Payment,"migration preserves existing transactions");
    Check(await reopened.ActiveCashAsync() is null&&await reopened.PendingFinanceCountAsync()==0,"migration does not invent opening cash or historical ledger entries");
    await reopened.InitializeAsync();Check(Directory.GetFiles(folder,"*.before-v2-*.db").Length==1,"v2 restart does not create repeated migration backups");
    Check(FinanceRules.EstimateFee(22500,70)==158&&FinanceRules.EstimateFee(5000,0)==0,"optional fee estimator rounds half up without claiming actual provider fee");
}
finally{SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
Console.WriteLine($"{count} finance integrity checks passed.");

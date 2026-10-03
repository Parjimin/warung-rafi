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
    Check(await db.PendingFinanceCountAsync()==0,"startup does not create cash bookkeeping");
    var sale=await db.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,25000);
    await db.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,25000);Check(await db.PendingFinanceCountAsync()==2,"completion retry creates one finance event");
    var qris=await Draft(15000);await db.CompleteAsync(qris.Id,qris.Version,PaymentMethod.QrisManual,0);
    var refund=await db.RequestRefundAsync(Id(),draft.Id,5000,RefundChannel.Cash,"Satu item batal");
    await db.RequestRefundAsync(refund.Id,draft.Id,5000,RefundChannel.Cash,"Satu item batal");
    await Reject<InvalidOperationException>(()=>db.RequestRefundAsync(Id(),draft.Id,18000,RefundChannel.Cash,"Kelebihan"),"pending refund reserves refundable balance");
    await Reject<InvalidOperationException>(()=>db.ResolveRefundAsync(refund.Id,true,"111111","Diserahkan ke pelanggan"),"incorrect manager PIN cannot approve");
    Check((await db.RefundsAsync(draft.Id)).Single().State==RefundState.Requested,"failed authorization leaves refund pending");
    await db.ResolveRefundAsync(refund.Id,true,pin,"Diserahkan ke pelanggan");await db.ResolveRefundAsync(refund.Id,true,pin,"Diserahkan ke pelanggan");
    Check((await db.SaleAsync(draft.Id))!.Payment==sale.Payment,"refund preserves original payment");
    var failed=await db.RequestRefundAsync(Id(),draft.Id,17500,RefundChannel.External,"Permintaan pelanggan");
    await db.ResolveRefundAsync(failed.Id,false,pin,"Pelanggan membatalkan");
    var replacement=await db.RequestRefundAsync(Id(),draft.Id,17500,RefundChannel.External,"Permintaan pelanggan");
    await db.ResolveRefundAsync(replacement.Id,true,pin,"Referensi bank UJI-123");
    await Reject<InvalidOperationException>(()=>db.RequestRefundAsync(Id(),draft.Id,1,RefundChannel.Cash,"Tambahan refund"),"completed refunds cannot exceed original sale");
    await Reject<InvalidOperationException>(()=>db.ResolveRefundAsync(refund.Id,false,pin,"Diubah kembali"),"completed refund cannot be reversed silently");
    await Reject<InvalidOperationException>(()=>new LocalStore(path).ResolveRefundAsync(refund.Id,true,pin,"Diserahkan ke pelanggan"),"unconfigured manager approval fails closed");
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
    var reopened=new LocalStore(path,hash);await reopened.InitializeAsync();
    for(var i=0;i<5;i++)await Reject<InvalidOperationException>(()=>reopened.ResolveRefundAsync(refund.Id,true,"111111","Diserahkan ke pelanggan"),"wrong PIN attempt "+(i+1));
    await Reject<InvalidOperationException>(()=>new LocalStore(path,hash).ResolveRefundAsync(refund.Id,true,pin,"Diserahkan ke pelanggan"),"PIN cooldown persists across app restart");
    Check(ManagerPin.Verify(hash,pin)&&!ManagerPin.Verify("broken",pin)&&ManagerPin.Hash(pin)!=hash,"PIN hash is salted and malformed hashes fail closed");
    Check((await reopened.SaleAsync(draft.Id))!.Payment==sale.Payment,"sale survives restart without manual cash session");
    Check(FinanceRules.EstimateFee(22500,70)==158&&FinanceRules.EstimateFee(5000,0)==0,"optional fee estimator rounds half up without claiming actual provider fee");
}
finally{SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
Console.WriteLine($"{count} finance integrity checks passed.");

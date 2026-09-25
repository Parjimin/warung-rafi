using Microsoft.Data.Sqlite;
using WarungRafi.Core;
using WarungRafi.Storage;

var folder=Path.Combine(Path.GetTempPath(),"warung-payments-"+Guid.NewGuid().ToString("N"));
var path=Path.Combine(folder,"test.db");var store=new LocalStore(path);await store.InitializeAsync();var passed=0;
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);passed++;}
async Task Reject(ProviderPayment[] batch,string label)
{
    var cursor=await store.CursorAsync();
    try { await store.ReceivePaymentsAsync(batch);throw new Exception("Accepted invalid batch: "+label); }
    catch(InvalidDataException) { Check(await store.CursorAsync()==cursor,label+" preserves cursor"); }
}
void Sql(string sql)
{
    using var connection=new SqliteConnection("Data Source="+path);connection.Open();using var command=connection.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery();
}
try
{
    var now=DateTimeOffset.UtcNow;var proof=new ProviderPayment(1,"txn-1",22500,now);
    var order=await store.SaveAsync(OrderRules.Add(Order.New(),DummyCatalog.Products[0]));
    var pending=await store.PendingCountAsync();
    Check((await store.ReceivePaymentsAsync([proof])).Length==1,"new proof committed once");
    Check(await store.CursorAsync()==1,"cursor follows durable proof");
    Check((await store.ReceivePaymentsAsync([proof,proof])).Length==0,"exact duplicate batch creates no alert");
    Check((await new LocalStore(path).ReceivePaymentsAsync([proof])).Length==0,"duplicate after restart creates no alert");
    Check((await store.ReceivePaymentsAsync([proof with { PaidAt=now.ToOffset(TimeSpan.FromHours(7)) }])).Length==0,"same instant in another offset is an exact retry");
    await Reject([proof with { Amount=22501 }],"changed amount");
    await Reject([proof with { PaidAt=now.AddSeconds(1) }],"changed paid time");
    await Reject([proof with { Sequence=2 }],"changed sequence");
    await Reject([proof with { TransactionId="another" }],"reused sequence");
    foreach(var bad in new[]{proof with { Sequence=0 },proof with { Sequence=-1 },proof with { Sequence=1_000_000_000_000_000 },proof with { Sequence=2,Amount=0 },proof with { Sequence=2,Amount=1_000_000_000_000 },proof with { Sequence=2,TransactionId=" " },proof with { Sequence=2,TransactionId=new string('x',257) },proof with { Sequence=2,PaidAt=default }})
        await Reject([bad],"invalid proof");
    await Reject(null!,"null batch");await Reject([null!],"null proof");await Reject(Enumerable.Repeat(proof,101).ToArray(),"oversized batch");
    var second=proof with { Sequence=3,TransactionId="txn-2" };
    await Reject([second,proof with { Sequence=4,TransactionId="txn-3",Amount=0 }],"later invalid row rolls back batch");
    Check((await store.ReceivePaymentsAsync([second])).Length==1,"rolled back earlier row can be delivered and sequence gap is valid");
    await Reject([proof with { Sequence=2,TransactionId="unknown-old" }],"unknown row behind cursor");
    Check((await store.ReceivePaymentsAsync([proof,second])).Length==0&&await store.CursorAsync()==3,"old exact retries never rewind cursor");
    var third=proof with { Sequence=4,TransactionId="txn-3" };
    Sql("CREATE TRIGGER fail_cursor BEFORE UPDATE ON settings WHEN NEW.key='payment_cursor' BEGIN SELECT RAISE(ABORT,'fixture cursor failure'); END");
    try { await store.ReceivePaymentsAsync([third]);throw new Exception("Expected cursor failure"); }catch(SqliteException) { Check(await store.CursorAsync()==3,"cursor write failure retains old cursor"); }
    Sql("DROP TRIGGER fail_cursor");
    Check((await store.ReceivePaymentsAsync([third])).Length==1,"cursor failure also rolled back proof so retry alerts once");
    Check((await store.ListAsync(OrderStatus.Draft)).Single().Version==order.Version&&await store.CountAsync(OrderStatus.Completed)==0&&await store.PendingCountAsync()==pending,"same-amount proofs do not modify orders or sales outbox");
    Check((await store.ReceivePaymentsAsync([])).Length==0&&await store.CursorAsync()==4,"empty inbox preserves cursor");
    var late=proof with { Sequence=5,TransactionId="late",PaidAt=now.AddMinutes(-5) };
    Check((await store.ReceivePaymentsAsync([late])).Length==1&&!PaymentAlertPolicy.IsFresh(late,now),"late evidence persists silently");
    Check(PaymentAlertPolicy.IsFresh(proof,now)&&PaymentAlertPolicy.IsFresh(proof,now.AddSeconds(60)),"freshness includes zero and sixty seconds");
    Check(!PaymentAlertPolicy.IsFresh(proof,now.AddSeconds(60).AddTicks(1))&&!PaymentAlertPolicy.IsFresh(proof,now.AddTicks(-1)),"expired and future timestamps never alert");
}
finally { SqliteConnection.ClearAllPools();Directory.Delete(folder,true); }
Console.WriteLine($"{passed} payment inbox checks passed.");

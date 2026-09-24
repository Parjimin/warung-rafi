using System.Text.Json;
using Microsoft.Data.Sqlite;
using WarungRafi.Core;
using WarungRafi.Storage;

namespace WarungRafi.RecoveryChecks;

internal sealed class RecoverySuite
{
    private int passed;
    public async Task Run()
    {
        await CrashCases();
        await DatabaseFull();
        await ConcurrentCompletion();
        await CapacityAndRetry();
        await WibBoundaries();
        await InvalidInput();
        await FutureSchema();
        Console.WriteLine($"{passed} recovery/integrity checks passed.");
    }
    private void Check(bool condition,string name)
    {
        if(!condition)throw new Exception(name);
        passed++;Console.WriteLine("PASS "+name);
    }
    private async Task Reject<T>(Func<Task> action,string name) where T:Exception
    {
        try { await action(); } catch(T) { Check(true,name);return; }
        throw new Exception("Expected rejection: "+name);
    }
    private async Task CrashCases()
    {
        foreach(var stage in new[]{"during-save","before-payment-commit","after-payment-commit","during-ack"})
        {
            using var db=new TestDatabase();await db.Store.InitializeAsync();
            var prior=await db.SaleAsync();var draft=await db.DraftAsync();
            var pendingBefore=await db.Store.PendingCountAsync();
            db.Execute("CREATE TABLE test_spill(payload BLOB)");
            // Keep an independent reader open, preventing close-time checkpointing from
            // masking the WAL recovery path. Child cache pressure spills dirty pages.
            using var witness=db.Connect();
            TestDatabase.Execute(witness,"PRAGMA wal_checkpoint(TRUNCATE)");
            await CrashWorker.KillAt(stage,db.Path,draft.Id);
            Check(new FileInfo(db.Path+"-wal").Length>32,stage+": WAL contains written frames before recovery");
            var reopened=new LocalStore(db.Path);await reopened.InitializeAsync();
            var actual=await reopened.OrderAsync(draft.Id);
            if(stage=="after-payment-commit")
            {
                var sale=await reopened.SaleAsync(draft.Id);
                Check(actual?.Status==OrderStatus.Completed && sale?.Payment.Amount==8000 && sale.Payment.Change==2000,stage+": committed order and payment survive kill");
                Check(await reopened.PendingCountAsync()==pendingBefore+1,stage+": committed outbox survives kill");
                var retry=await reopened.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,10000);
                Check(retry.Order.Version==sale!.Order.Version && await reopened.PendingCountAsync()==pendingBefore+1,stage+": retry is idempotent after restart");
            }
            else
            {
                Check(actual?.Status==OrderStatus.Draft && actual.Version==draft.Version && actual.Total==draft.Total,stage+": uncommitted order rolls back");
                Check(await reopened.SaleAsync(draft.Id)==null,stage+": no partial payment");
                Check(await reopened.PendingCountAsync()==pendingBefore,stage+": outbox/ack rolls back with its transaction");
                var retry=await reopened.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,10000);
                Check(retry.Payment.Amount==8000,stage+": recovered order can be completed normally");
            }
            Check((await reopened.SaleAsync(prior.Order.Id))?.Payment==prior.Payment,stage+": previous sale unchanged");
            Check(Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM test_spill"))==0,stage+": uncommitted spill data is discarded");
            Check(db.IntegrityOk(),stage+": integrity and foreign keys valid");
        }
    }
    private async Task DatabaseFull()
    {
        using var db=new TestDatabase();await db.Store.InitializeAsync();
        var prior=await db.SaleAsync();var draft=await db.DraftAsync();var pending=await db.Store.PendingCountAsync();
        db.Execute("CREATE TABLE test_disk_pressure(payload BLOB)");
        var pageLimit=Convert.ToInt64(db.Scalar("PRAGMA page_count"));
        var constrained=new LocalStore(db.Path,connection=>
        {
            // SQLite itself raises SQLITE_FULL on allocation. No fabricated exception,
            // no shared disk filling, and no capacity limit is applied to real app data.
            TestDatabase.Execute(connection,$"PRAGMA max_page_count={pageLimit};");
            TestDatabase.Execute(connection,"CREATE TEMP TRIGGER exhaust_storage BEFORE INSERT ON main.payments BEGIN INSERT INTO test_disk_pressure VALUES(zeroblob(2000000)); END;");
        });
        var wasFull=false;
        try { await constrained.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,10000); }
        catch(SqliteException error) when(error.SqliteErrorCode==13) { wasFull=true; }
        Check(wasFull,"capacity exhaustion: native SQLite returns SQLITE_FULL (13)");
        var reopened=new LocalStore(db.Path);await reopened.InitializeAsync();
        Check((await reopened.OrderAsync(draft.Id))?.Status==OrderStatus.Draft,"capacity exhaustion: order is still unpaid");
        Check(await reopened.SaleAsync(draft.Id)==null,"capacity exhaustion: no partial payment");
        Check(await reopened.PendingCountAsync()==pending,"capacity exhaustion: no phantom completed outbox event");
        Check((await reopened.SaleAsync(prior.Order.Id))?.Payment==prior.Payment,"capacity exhaustion: previous sale preserved");
        Check(Convert.ToInt64(db.Scalar("SELECT count(*) FROM test_disk_pressure"))==0,"capacity exhaustion: failing allocation rolls back");
        Check(db.IntegrityOk(),"capacity exhaustion: database integrity remains valid");
        var retried=await reopened.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,10000);
        Check(retried.Payment.Change==2000 && await reopened.PendingCountAsync()==pending+1,"capacity exhaustion: retry succeeds once capacity is available");
    }
    private async Task ConcurrentCompletion()
    {
        using var db=new TestDatabase();await db.Store.InitializeAsync();var draft=await db.DraftAsync();
        var stores=Enumerable.Range(0,8).Select(_=>new LocalStore(db.Path)).ToArray();
        var results=await Task.WhenAll(stores.Select(s=>s.CompleteAsync(draft.Id,draft.Version,PaymentMethod.Cash,10000)));
        Check(results.Select(x=>x.Payment).Distinct().Count()==1,"concurrent completion: independent connections return one payment");
        Check(Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM payments"))==1 && await db.Store.PendingCountAsync()==2,"concurrent completion: one payment and one completed outbox event");
        Check(db.IntegrityOk(),"concurrent completion: database remains valid");
    }
    private async Task CapacityAndRetry()
    {
        using var db=new TestDatabase();await db.Store.InitializeAsync();
        var held=await db.Store.SaveAsync(OrderRules.Hold(OrderRules.Add(Order.New(),TestDatabase.Product),"Pesanan lama"));
        var draft=await db.DraftAsync();
        // Seed historical transactions as one fixture transaction. This case tests reads,
        // limits and pagination of the actual outbox API, not write throughput.
        const int count=1025;
        var date=new DateTimeOffset(2026,9,24,12,0,0,TimeSpan.Zero);
        using(var connection=db.Connect())
        using(var tx=connection.BeginTransaction())
        {
            for(var i=0;i<count;i++)
            {
                var order=OrderRules.Add(Order.New(),TestDatabase.Product) with { Version=2,Status=OrderStatus.Completed,UpdatedAt=date.AddSeconds(i) };
                var payment=new Payment(order.Id,PaymentMethod.Cash,8000,10000,2000,order.UpdatedAt);
                // Place old held/draft below every sale, independent of the clock running CI.
                InsertSale(connection,tx,order,payment);
            }
            using var older=connection.CreateCommand();older.Transaction=tx;
            older.CommandText="UPDATE orders SET updated_at='2000-01-01T00:00:00.0000000+00:00' WHERE status IN (0,1)";older.ExecuteNonQuery();
            tx.Commit();
        }
        Check((await db.Store.ListAsync(OrderStatus.Completed)).Length==1000,"capacity: history respects display limit");
        Check((await db.Store.ListAsync(OrderStatus.Held)).Single().Id==held.Id,"capacity: old held order remains discoverable behind 1025 sales");
        Check((await db.Store.ListAsync(OrderStatus.Draft)).Single().Id==draft.Id,"capacity: old draft remains discoverable behind 1025 sales");
        Check(await db.Store.CountAsync(OrderStatus.Completed)==count,"capacity: aggregate count is not truncated by display limit");
        var summary=await db.Store.DailySalesAsync(new DateTime(2026,9,24));
        Check(summary.Count==count && summary.Cash==count*8000L,"capacity: daily total includes all 1025 sales");
        var first=await db.Store.PendingAsync();var replay=await new LocalStore(db.Path).PendingAsync();
        Check(first.Length==50 && first.Select(x=>x.Id).SequenceEqual(replay.Select(x=>x.Id)),"outbox: response loss replays same 50 events after reopen");
        await db.Store.AcknowledgeAsync(first.Take(10).Select(x=>x.Id).ToArray());
        var second=await db.Store.PendingAsync();
        Check(second[0].Id==first[10].Id && second.Length==50,"outbox: partial acknowledgment retains all unacknowledged events");
        await db.Store.AcknowledgeAsync(first.Take(10).Select(x=>x.Id).ToArray());
        Check(await db.Store.PendingCountAsync()==count+2-10,"outbox: repeated acknowledgment does not consume more events");
        var seen=new HashSet<string>(first.Take(10).Select(x=>x.Id));
        while(await db.Store.PendingCountAsync()>0)
        {
            var batch=await db.Store.PendingAsync();
            if(batch.Length==0 || batch.Any(x=>!seen.Add(x.Id)))throw new Exception("Outbox backlog skipped or repeated acknowledged events");
            await db.Store.AcknowledgeAsync(batch.Select(x=>x.Id).ToArray());
        }
        Check(seen.Count==count+2,"outbox: entire backlog drains without missing or duplicating events");
        Check(db.IntegrityOk(),"capacity: data integrity valid after backlog drains");
    }
    private async Task WibBoundaries()
    {
        using var db=new TestDatabase();await db.Store.InitializeAsync();
        using(var connection=db.Connect())
        using(var tx=connection.BeginTransaction())
        {
            foreach(var (time,method) in new[]{("2026-09-23T16:59:59Z",PaymentMethod.Cash),("2026-09-23T17:00:00Z",PaymentMethod.Cash),("2026-09-24T16:59:59Z",PaymentMethod.QrisManual),("2026-09-24T17:00:00Z",PaymentMethod.Cash)})
            {
                var timestamp=DateTimeOffset.Parse(time).ToUniversalTime();
                var order=OrderRules.Add(Order.New(),TestDatabase.Product) with { Version=2,Status=OrderStatus.Completed,UpdatedAt=timestamp };
                InsertSale(connection,tx,order,new Payment(order.Id,method,8000,8000,0,timestamp));
            }
            tx.Commit();
        }
        var day=await db.Store.DailySalesAsync(new DateTime(2026,9,24));
        Check(day.Count==2 && day.Cash==8000 && day.Qris==8000,"WIB: include local midnight and exclude next midnight regardless of OS timezone");
    }
    private async Task InvalidInput()
    {
        using var db=new TestDatabase();await db.Store.InitializeAsync();var draft=await db.DraftAsync();
        await Reject<ArgumentException>(async()=>{await db.Store.CompleteAsync(draft.Id,draft.Version,(PaymentMethod)99,10000);},"invalid method: rejected before persistent payment");
        var invalid=draft with { Lines=[draft.Lines[0] with { Quantity=-1 }] };
        await Reject<ArgumentException>(async()=>{await db.Store.SaveAsync(invalid);},"invalid line: negative quantity cannot poison local outbox");
        await Reject<ArgumentException>(async()=>{await db.Store.SaveAsync(draft with { Status=(OrderStatus)99 });},"invalid status: rejected at storage boundary");
        var cancelled=await db.Store.SaveAsync(OrderRules.Cancel(draft,"Pelanggan batal"));
        await Reject<InvalidOperationException>(async()=>{await db.Store.CompleteAsync(cancelled.Id,cancelled.Version,PaymentMethod.Cash,10000);},"cancelled order: cannot receive payment");
        Check(await db.Store.SaleAsync(draft.Id)==null && await db.Store.PendingCountAsync()==2,"rejected writes: no partial payment or extra outbox event");
    }
    private async Task FutureSchema()
    {
        using var db=new TestDatabase();await db.Store.InitializeAsync();var sale=await db.SaleAsync();
        db.Execute("PRAGMA user_version=99");
        await Reject<InvalidDataException>(()=>new LocalStore(db.Path).InitializeAsync(),"future schema: older app refuses to downgrade database");
        Check(Convert.ToInt64(db.Scalar("PRAGMA user_version"))==99 && (await db.Store.SaleAsync(sale.Order.Id))?.Payment==sale.Payment,"future schema: refusal preserves schema marker and existing sale");
    }
    private static void InsertSale(SqliteConnection connection,SqliteTransaction tx,Order order,Payment payment)
    {
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var command=connection.CreateCommand();command.Transaction=tx;
        command.CommandText="INSERT INTO orders VALUES($id,$version,2,$time,$order); INSERT INTO payments VALUES($id,$payment); INSERT INTO outbox(id,aggregate_id,version,payload,created_at) VALUES($event,$id,$version,$payload,$time);";
        command.Parameters.AddWithValue("$id",order.Id);command.Parameters.AddWithValue("$version",order.Version);
        command.Parameters.AddWithValue("$time",order.UpdatedAt.ToString("O"));command.Parameters.AddWithValue("$event",$"{order.Id}:{order.Version}");
        command.Parameters.AddWithValue("$order",JsonSerializer.Serialize(order,json));command.Parameters.AddWithValue("$payment",JsonSerializer.Serialize(payment,json));
        command.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(new {order,payment},json));command.ExecuteNonQuery();
    }
}

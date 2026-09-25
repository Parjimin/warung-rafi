using System.Text.Json;
using Microsoft.Data.Sqlite;
using WarungRafi.Core;

namespace WarungRafi.Storage;

public sealed partial class LocalStore
{
    private static void InitializeFinance(SqliteConnection connection,SqliteTransaction tx)
    {
        using var command=Command(connection,tx,"""
            CREATE TABLE IF NOT EXISTS cash_sessions(id TEXT PRIMARY KEY,closed INTEGER NOT NULL DEFAULT 0,payload TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS one_open_cash_session ON cash_sessions(closed) WHERE closed=0;
            CREATE TABLE IF NOT EXISTS refunds(id TEXT PRIMARY KEY,order_id TEXT NOT NULL REFERENCES orders(id),state INTEGER NOT NULL,amount INTEGER NOT NULL CHECK(amount>0),payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS finance_events(sequence INTEGER PRIMARY KEY AUTOINCREMENT,id TEXT NOT NULL UNIQUE,
                kind TEXT NOT NULL,session_id TEXT,order_id TEXT,amount INTEGER NOT NULL,cash_delta INTEGER NOT NULL,occurred_at TEXT NOT NULL,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS finance_outbox(id TEXT PRIMARY KEY REFERENCES finance_events(id),acknowledged INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS ix_finance_session ON finance_events(session_id);
            CREATE INDEX IF NOT EXISTS ix_refunds_order ON refunds(order_id);
            CREATE TRIGGER IF NOT EXISTS finance_no_update BEFORE UPDATE ON finance_events BEGIN SELECT RAISE(ABORT,'Finance history is immutable'); END;
            CREATE TRIGGER IF NOT EXISTS finance_no_delete BEFORE DELETE ON finance_events BEGIN SELECT RAISE(ABORT,'Finance history is immutable'); END;
            """);command.ExecuteNonQuery();
    }
    private static CashSession? ActiveSession(SqliteConnection connection,SqliteTransaction? tx)
    {
        using var command=Command(connection,tx,"SELECT payload FROM cash_sessions WHERE closed=0");
        return command.ExecuteScalar() is string value?JsonSerializer.Deserialize<CashSession>(value,Json):null;
    }
    private static CashSession? ReadSession(SqliteConnection connection,SqliteTransaction? tx,string id)
    {
        using var command=Command(connection,tx,"SELECT payload FROM cash_sessions WHERE id=$id",("$id",id));
        return command.ExecuteScalar() is string value?JsonSerializer.Deserialize<CashSession>(value,Json):null;
    }
    private static CashPosition Position(SqliteConnection connection,SqliteTransaction? tx,CashSession session)
    {
        using var command=Command(connection,tx,"SELECT kind,amount,cash_delta FROM finance_events WHERE session_id=$id",("$id",session.Id));
        using var reader=command.ExecuteReader();long cash=0,qris=0,income=0,expense=0,cashRefunds=0,refunds=0,count=0;
        while(reader.Read())
        {
            var kind=reader.GetString(0);var amount=reader.GetInt64(1);var delta=reader.GetInt64(2);
            checked
            {
                if(kind=="sale"){if(delta>0)cash+=amount;else qris+=amount;count++;}
                if(kind=="cash_in")income+=amount;
                if(kind=="cash_out")expense+=amount;
                if(kind=="refund_completed"){refunds+=amount;cashRefunds-=delta;}
            }
        }
        return new(session,cash,qris,income,expense,cashRefunds,refunds,count);
    }
    public Task<CashPosition?> ActiveCashAsync()=>Locked(()=>
    {using var c=Open();var session=ActiveSession(c,null);return session is null?null:Position(c,null,session);});
    public Task<FinanceEvent[]> CashEntriesAsync(string sessionId)=>Locked(()=>
    {
        using var c=Open();using var cmd=Command(c,null,"SELECT payload FROM finance_events WHERE session_id=$id ORDER BY sequence DESC LIMIT 500",("$id",sessionId));
        using var r=cmd.ExecuteReader();var entries=new List<FinanceEvent>();while(r.Read())entries.Add(JsonSerializer.Deserialize<FinanceEvent>(r.GetString(0),Json)!);return entries.ToArray();
    });
    public Task<CashPosition[]> CashHistoryAsync()=>Locked(()=>
    {
        using var c=Open();using var command=Command(c,null,"SELECT payload FROM cash_sessions ORDER BY rowid DESC LIMIT 100");
        var sessions=new List<CashSession>();using(var r=command.ExecuteReader())while(r.Read())sessions.Add(JsonSerializer.Deserialize<CashSession>(r.GetString(0),Json)!);
        return sessions.Select(s=>Position(c,null,s)).ToArray();
    });
    public Task<CashSession> OpenCashAsync(string id,long opening)=>Locked(()=>
    {
        FinanceRules.Id(id);FinanceRules.Amount(opening,true);
        using var c=Open();using var tx=c.BeginTransaction();var old=ReadSession(c,tx,id);
        if(old is not null){if(old.OpeningCash!=opening)throw new InvalidOperationException("Identitas buka kas sudah digunakan.");tx.Commit();return old;}
        if(ActiveSession(c,tx) is not null)throw new InvalidOperationException("Masih ada sesi kas terbuka. Lanjutkan sesi tersebut.");
        var session=new CashSession(id,DateTimeOffset.UtcNow,opening,"Kasir");
        using var insert=Command(c,tx,"INSERT INTO cash_sessions VALUES($id,0,$payload)",("$id",id),("$payload",JsonSerializer.Serialize(session,Json)));insert.ExecuteNonQuery();
        AddFinance(c,tx,"open-"+id,"session_opened",id,null,opening,opening,"Kasir","Modal awal",session);tx.Commit();return session;
    });
    public Task RecordCashMovementAsync(string id,string sessionId,bool incoming,long amount,string reason)=>Locked(()=>
    {
        FinanceRules.Id(id);FinanceRules.Amount(amount);reason=FinanceRules.Note(reason);
        using var c=Open();using var tx=c.BeginTransaction();
        var old=ReadFinance(c,tx,id);
        if(old is not null)
        {
            if(old.SessionId!=sessionId||old.Kind!=(incoming?"cash_in":"cash_out")||old.Amount!=amount||old.Reason!=reason)throw new InvalidOperationException("Identitas kas sudah dipakai untuk tindakan berbeda.");
            tx.Commit();return true;
        }
        var session=ActiveSession(c,tx);if(session?.Id!=sessionId)throw new InvalidOperationException("Sesi kas sudah berubah. Muat ulang.");
        if(!incoming&&Position(c,tx,session).Expected<amount)throw new InvalidOperationException("Kas tercatat tidak mencukupi. Periksa pergerakan kas dahulu.");
        AddFinance(c,tx,id,incoming?"cash_in":"cash_out",sessionId,null,amount,incoming?amount:-amount,"Kasir",reason,new { });tx.Commit();return true;
    });
    public Task<CashPosition> CloseCashAsync(string sessionId,long counted,string note)=>Locked(()=>
    {
        FinanceRules.Amount(counted,true);note=note.Trim();if(note.Length>200)throw new ArgumentException("Catatan maksimal 200 karakter.");
        using var c=Open();using var tx=c.BeginTransaction();var session=ReadSession(c,tx,sessionId)??throw new InvalidOperationException("Sesi tidak ditemukan.");
        var position=Position(c,tx,session);
        if(session.ClosedAt is not null)
        {if(session.CountedCash!=counted||session.ClosingNote!=note)throw new InvalidOperationException("Sesi sudah ditutup dengan nilai berbeda.");tx.Commit();return position;}
        if(position.Expected!=counted)note=FinanceRules.Note(note);
        var closed=session with {ClosedAt=DateTimeOffset.UtcNow,CountedCash=counted,ExpectedAtClose=position.Expected,ClosingNote=note};
        using var command=Command(c,tx,"UPDATE cash_sessions SET closed=1,payload=$payload WHERE id=$id",("$payload",JsonSerializer.Serialize(closed,Json)),("$id",sessionId));command.ExecuteNonQuery();
        AddFinance(c,tx,"close-"+sessionId,"session_closed",sessionId,null,counted,0,"Kasir",note,closed);tx.Commit();return position with {Session=closed};
    });
    public Task<Refund[]> RefundsAsync(string orderId)=>Locked(()=>
    {
        using var c=Open();using var command=Command(c,null,"SELECT payload FROM refunds WHERE order_id=$id ORDER BY rowid",("$id",orderId));
        using var r=command.ExecuteReader();var result=new List<Refund>();while(r.Read())result.Add(JsonSerializer.Deserialize<Refund>(r.GetString(0),Json)!);return result.ToArray();
    });
    private static Refund? ReadRefund(SqliteConnection c,SqliteTransaction? tx,string id)
    {using var cmd=Command(c,tx,"SELECT payload FROM refunds WHERE id=$id",("$id",id));return cmd.ExecuteScalar() is string p?JsonSerializer.Deserialize<Refund>(p,Json):null;}
    public Task<Refund> RequestRefundAsync(string id,string orderId,long amount,RefundChannel channel,string reason)=>Locked(()=>
    {
        FinanceRules.Id(id);FinanceRules.Amount(amount);reason=FinanceRules.Note(reason);if(!Enum.IsDefined(channel))throw new ArgumentException("Cara pengembalian tidak valid.");
        using var c=Open();using var tx=c.BeginTransaction();var old=ReadRefund(c,tx,id);
        if(old is not null)
        {if(old.OrderId!=orderId||old.Amount!=amount||old.Channel!=channel||old.Reason!=reason)throw new InvalidOperationException("Identitas pengembalian berubah.");tx.Commit();return old;}
        var payment=ReadPayment(c,tx,orderId)??throw new InvalidOperationException("Pesanan belum dibayar.");
        using var sum=Command(c,tx,"SELECT COALESCE(SUM(amount),0) FROM refunds WHERE order_id=$id AND state IN (0,1)",("$id",orderId));
        if(amount>payment.Amount-Convert.ToInt64(sum.ExecuteScalar()))throw new InvalidOperationException("Nominal melebihi sisa yang dapat dikembalikan; permintaan tertunda ikut dicadangkan.");
        var refund=new Refund(id,orderId,amount,channel,reason,RefundState.Requested,DateTimeOffset.UtcNow);
        using var insert=Command(c,tx,"INSERT INTO refunds VALUES($id,$order,0,$amount,$payload)",("$id",id),("$order",orderId),("$amount",amount),("$payload",JsonSerializer.Serialize(refund,Json)));insert.ExecuteNonQuery();
        AddFinance(c,tx,"request-"+id,"refund_requested",null,orderId,amount,0,"Kasir",reason,refund);tx.Commit();return refund;
    });
    public Task<Refund> ResolveRefundAsync(string id,bool completed,string pin,string reference)=>Locked(()=>
    {
        using var c=Open();VerifyManager(c,pin);reference=FinanceRules.Note(reference);
        using var tx=c.BeginTransaction();var old=ReadRefund(c,tx,id)??throw new InvalidOperationException("Pengembalian tidak ditemukan.");
        var target=completed?RefundState.Completed:RefundState.Failed;
        if(old.State!=RefundState.Requested)
        {if(old.State!=target||(completed?old.Reference:old.FailureReason)!=reference)throw new InvalidOperationException("Pengembalian sudah diputuskan berbeda.");tx.Commit();return old;}
        var session=completed?ActiveSession(c,tx):null;
        if(completed&&session is null)throw new InvalidOperationException("Buka kas dahulu agar pengembalian tercatat dalam sesi yang benar.");
        if(completed&&old.Channel==RefundChannel.Cash&&Position(c,tx,session!).Expected<old.Amount)throw new InvalidOperationException("Kas tercatat tidak mencukupi untuk pengembalian tunai.");
        var resolved=old with {State=target,CompletedAt=completed?DateTimeOffset.UtcNow:null,SessionId=session?.Id,ApprovedBy="Pengelola",Reference=completed?reference:"",FailureReason=completed?"":reference};
        using var update=Command(c,tx,"UPDATE refunds SET state=$state,payload=$payload WHERE id=$id",("$state",(int)target),("$payload",JsonSerializer.Serialize(resolved,Json)),("$id",id));update.ExecuteNonQuery();
        AddFinance(c,tx,"resolve-"+id,completed?"refund_completed":"refund_failed",session?.Id,old.OrderId,old.Amount,completed&&old.Channel==RefundChannel.Cash?-old.Amount:0,"Pengelola",reference,resolved);tx.Commit();return resolved;
    });
    private void VerifyManager(SqliteConnection c,string pin)
    {
        if(string.IsNullOrWhiteSpace(managerPinHash))throw new InvalidOperationException("PIN pengelola belum dikonfigurasi pada laptop ini.");
        using var tx=c.BeginTransaction();using var read=Command(c,tx,"SELECT value FROM settings WHERE key='manager_lock_until'");
        if(DateTimeOffset.TryParse(read.ExecuteScalar()?.ToString(),out var until)&&until>DateTimeOffset.UtcNow)throw new InvalidOperationException("Terlalu banyak PIN salah. Coba kembali setelah lima menit.");
        using var count=Command(c,tx,"SELECT value FROM settings WHERE key='manager_failures'");var failures=int.TryParse(count.ExecuteScalar()?.ToString(),out var n)?n:0;
        var valid=ManagerPin.Verify(managerPinHash,pin);failures=valid?0:failures+1;
        using var write=Command(c,tx,"INSERT INTO settings VALUES('manager_failures',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("$value",failures.ToString()));write.ExecuteNonQuery();
        if(failures>=5)
        {
            using var block=Command(c,tx,"INSERT INTO settings VALUES('manager_lock_until',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("$value",DateTimeOffset.UtcNow.AddMinutes(5).ToString("O")));block.ExecuteNonQuery();
            using var reset=Command(c,tx,"UPDATE settings SET value='0' WHERE key='manager_failures'");reset.ExecuteNonQuery();
        }
        tx.Commit();if(!valid)throw new InvalidOperationException("PIN pengelola tidak sesuai.");
    }
    private static FinanceEvent? ReadFinance(SqliteConnection c,SqliteTransaction? tx,string id)
    {using var cmd=Command(c,tx,"SELECT payload FROM finance_events WHERE id=$id",("$id",id));return cmd.ExecuteScalar() is string p?JsonSerializer.Deserialize<FinanceEvent>(p,Json):null;}
    private static void AddFinance(SqliteConnection c,SqliteTransaction tx,string id,string kind,string? sessionId,string? orderId,long amount,long delta,string actor,string reason,object details)
    {
        using var next=Command(c,tx,"SELECT COALESCE(MAX(sequence),0)+1 FROM finance_events");var sequence=Convert.ToInt64(next.ExecuteScalar());
        var entry=new FinanceEvent(id,sequence,kind,DateTimeOffset.UtcNow,sessionId,orderId,amount,delta,actor,reason,JsonSerializer.SerializeToElement(details,Json));
        using var insert=Command(c,tx,"INSERT INTO finance_events VALUES($seq,$id,$kind,$session,$order,$amount,$delta,$time,$payload)",("$seq",sequence),("$id",id),("$kind",kind),("$session",(object?)sessionId??DBNull.Value),("$order",(object?)orderId??DBNull.Value),("$amount",amount),("$delta",delta),("$time",entry.OccurredAt.ToString("O")),("$payload",JsonSerializer.Serialize(entry,Json)));insert.ExecuteNonQuery();
        using var outbox=Command(c,tx,"INSERT INTO finance_outbox(id) VALUES($id)",("$id",id));outbox.ExecuteNonQuery();
    }
    public Task<FinanceEvent[]> PendingFinanceAsync()=>Locked(()=>
    {
        using var c=Open();using var cmd=Command(c,null,"SELECT e.payload FROM finance_events e JOIN finance_outbox o ON e.id=o.id WHERE o.acknowledged=0 ORDER BY e.sequence LIMIT 50");
        using var r=cmd.ExecuteReader();var result=new List<FinanceEvent>();while(r.Read())result.Add(JsonSerializer.Deserialize<FinanceEvent>(r.GetString(0),Json)!);return result.ToArray();
    });
    public Task<int> PendingFinanceCountAsync()=>Locked(()=>
    {using var c=Open();using var cmd=Command(c,null,"SELECT COUNT(*) FROM finance_outbox WHERE acknowledged=0");return Convert.ToInt32(cmd.ExecuteScalar());});
    public Task AcknowledgeFinanceAsync(string[] ids)=>Locked(()=>
    {
        using var c=Open();using var tx=c.BeginTransaction();foreach(var id in ids){using var cmd=Command(c,tx,"UPDATE finance_outbox SET acknowledged=1 WHERE id=$id",("$id",id));cmd.ExecuteNonQuery();}tx.Commit();return true;
    });
}

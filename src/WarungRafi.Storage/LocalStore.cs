using System.Text.Json;
using Microsoft.Data.Sqlite;
using WarungRafi.Core;

namespace WarungRafi.Storage;

public sealed class LocalStore
{
    private readonly string connectionString;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public LocalStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 10 }.ToString();
    }
    public async Task InitializeAsync() => await Locked(() =>
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS orders(id TEXT PRIMARY KEY, version INTEGER NOT NULL,
                status INTEGER NOT NULL, updated_at TEXT NOT NULL, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS payments(order_id TEXT PRIMARY KEY REFERENCES orders(id), payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS outbox(id TEXT PRIMARY KEY, aggregate_id TEXT NOT NULL,
                version INTEGER NOT NULL, payload TEXT NOT NULL, created_at TEXT NOT NULL, acknowledged INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS provider_payments(transaction_id TEXT PRIMARY KEY, sequence INTEGER NOT NULL,
                amount INTEGER NOT NULL, paid_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS print_attempts(id TEXT PRIMARY KEY, order_id TEXT NOT NULL,
                created_at TEXT NOT NULL, is_copy INTEGER NOT NULL, status TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_orders_status_updated ON orders(status, updated_at);
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
        return true;
    });

    public Task<Order> SaveAsync(Order order) => Locked(() =>
    {
        if (order.Status == OrderStatus.Completed) throw new InvalidOperationException("Gunakan penyelesaian pembayaran.");
        using var connection = Open(); using var tx = connection.BeginTransaction();
        var existing = Load(connection, tx, order.Id);
        CheckVersion(order, existing);
        if (existing?.Status is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new InvalidOperationException("Pesanan final tidak dapat diubah.");
        var stored = Write(connection, tx, order, null);
        tx.Commit();
        return stored;
    });

    public Task<CompletedSale> CompleteAsync(string id, long expectedVersion, PaymentMethod method, long tendered) => Locked(() =>
    {
        using var connection = Open(); using var tx = connection.BeginTransaction();
        var current = Load(connection, tx, id) ?? throw new InvalidOperationException("Pesanan belum disimpan.");
        if (current.Status == OrderStatus.Completed)
        {
            var saved = ReadPayment(connection, tx, id) ?? throw new InvalidDataException("Pembayaran hilang.");
            tx.Commit(); return new CompletedSale(current, saved);
        }
        if (current.Version != expectedVersion) throw new InvalidOperationException("Pesanan telah berubah. Buka kembali pesanan.");
        var sale = OrderRules.Complete(current, method, tendered);
        var stored = Write(connection, tx, sale.Order, sale.Payment);
        using var payment = Command(connection, tx, "INSERT INTO payments(order_id,payload) VALUES($id,$payload)",
            ("$id", id), ("$payload", JsonSerializer.Serialize(sale.Payment, Json)));
        payment.ExecuteNonQuery(); tx.Commit();
        return new CompletedSale(stored, sale.Payment);
    });

    public Task<Order[]> ListAsync(params OrderStatus[] statuses) => Locked(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM orders";
        if(statuses.Length>0)
        {
            command.CommandText+=" WHERE status IN ("+string.Join(",",statuses.Select((_,i)=>"$s"+i))+")";
            for(var i=0;i<statuses.Length;i++) command.Parameters.AddWithValue("$s"+i,(int)statuses[i]);
        }
        command.CommandText+=" ORDER BY updated_at DESC LIMIT 1000";
        using var reader = command.ExecuteReader(); var result = new List<Order>();
        while (reader.Read())
        {
            var order = JsonSerializer.Deserialize<Order>(reader.GetString(0), Json)!;
            if (statuses.Length == 0 || statuses.Contains(order.Status)) result.Add(order);
        }
        return result.ToArray();
    });

    public Task<DailySales> DailySalesAsync(DateTime dateInWib) => Locked(() =>
    {
        var start=new DateTimeOffset(DateTime.SpecifyKind(dateInWib.Date,DateTimeKind.Unspecified),TimeSpan.FromHours(7)).ToUniversalTime();
        using var connection=Open();
        using var command=Command(connection,null,"SELECT p.payload FROM payments p JOIN orders o ON o.id=p.order_id WHERE o.status=2 AND o.updated_at >= $start AND o.updated_at < $end",
            ("$start",start.ToString("O")),("$end",start.AddDays(1).ToString("O")));
        using var reader=command.ExecuteReader();long cash=0,qris=0,count=0;
        while(reader.Read())
        {
            var payment=JsonSerializer.Deserialize<Payment>(reader.GetString(0),Json)!;
            if(payment.Method==PaymentMethod.Cash)cash=checked(cash+payment.Amount);else qris=checked(qris+payment.Amount);
            count++;
        }
        return new DailySales(cash,qris,count);
    });

    public Task<CatalogSnapshot> CatalogAsync() => Locked(() =>
    {
        using var connection=Open();using var command=Command(connection,null,"SELECT value FROM settings WHERE key='catalog'");
        return command.ExecuteScalar() is string payload ? JsonSerializer.Deserialize<CatalogSnapshot>(payload,Json)! : new CatalogSnapshot(0,DummyCatalog.Products);
    });
    public Task<bool> ReceiveCatalogAsync(CatalogSnapshot snapshot) => Locked(() =>
    {
        if(snapshot.Version<=0)return false;
        if(snapshot.Products.Length is <1 or >300 || snapshot.Products.Select(x=>x.Id).Distinct().Count()!=snapshot.Products.Length ||
            snapshot.Products.Any(x=>string.IsNullOrWhiteSpace(x.Id)||string.IsNullOrWhiteSpace(x.Name)||x.Price is <1 or >1_000_000_000|| !new[]{"Nasi","Lauk","Sundukan","Minuman"}.Contains(x.Category)))
            throw new InvalidDataException("Katalog tidak valid.");
        using var connection=Open();using var tx=connection.BeginTransaction();
        using var read=Command(connection,tx,"SELECT value FROM settings WHERE key='catalog'");
        var existing=read.ExecuteScalar() as string;
        if(existing is not null && JsonSerializer.Deserialize<CatalogSnapshot>(existing,Json)!.Version>=snapshot.Version)return false;
        using var write=Command(connection,tx,"INSERT INTO settings(key,value) VALUES('catalog',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("$value",JsonSerializer.Serialize(snapshot,Json)));
        write.ExecuteNonQuery();tx.Commit();return true;
    });

    public Task<CompletedSale?> SaleAsync(string id) => Locked(() =>
    {
        using var connection = Open(); var order = Load(connection, null, id);
        var payment = ReadPayment(connection, null, id);
        return order is not null && payment is not null ? new CompletedSale(order, payment) : null;
    });

    public Task<OutboxEvent[]> PendingAsync() => Locked(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,aggregate_id,version,payload,created_at FROM outbox WHERE acknowledged=0 ORDER BY rowid LIMIT 50";
        using var reader = command.ExecuteReader(); var events = new List<OutboxEvent>();
        while(reader.Read()) events.Add(new(reader.GetString(0),reader.GetString(1),reader.GetInt64(2),reader.GetString(3),DateTimeOffset.Parse(reader.GetString(4))));
        return events.ToArray();
    });

    public Task<int> PendingCountAsync() => Locked(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM outbox WHERE acknowledged=0";
        return Convert.ToInt32(command.ExecuteScalar());
    });

    public Task AcknowledgeAsync(string[] ids) => Locked(() =>
    {
        using var connection = Open(); using var tx = connection.BeginTransaction();
        foreach(var id in ids)
        {
            using var command=Command(connection,tx,"UPDATE outbox SET acknowledged=1 WHERE id=$id",("$id",id));
            command.ExecuteNonQuery();
        }
        tx.Commit(); return true;
    });

    public Task<long> CursorAsync() => Locked(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText="SELECT value FROM settings WHERE key='payment_cursor'";
        return long.TryParse(command.ExecuteScalar()?.ToString(), out var value) ? value : 0;
    });

    public Task<ProviderPayment[]> ReceivePaymentsAsync(ProviderPayment[] payments) => Locked(() =>
    {
        using var connection = Open(); using var tx=connection.BeginTransaction();
        var added=new List<ProviderPayment>();
        foreach(var payment in payments.OrderBy(x=>x.Sequence))
        {
            if(payment.Amount<=0 || string.IsNullOrWhiteSpace(payment.TransactionId)) throw new InvalidDataException("Bukti pembayaran tidak valid.");
            using var insert=Command(connection,tx,"INSERT OR IGNORE INTO provider_payments VALUES($id,$sequence,$amount,$paid)",
                ("$id",payment.TransactionId),("$sequence",payment.Sequence),("$amount",payment.Amount),("$paid",payment.PaidAt.ToString("O")));
            if(insert.ExecuteNonQuery()==1) added.Add(payment);
        }
        if(payments.Length>0)
        {
            using var cursor=Command(connection,tx,"INSERT INTO settings(key,value) VALUES('payment_cursor',$cursor) ON CONFLICT(key) DO UPDATE SET value=CAST(MAX(CAST(settings.value AS INTEGER), CAST(excluded.value AS INTEGER)) AS TEXT)",
                ("$cursor",payments.Max(x=>x.Sequence).ToString()));
            cursor.ExecuteNonQuery();
        }
        tx.Commit(); return added.ToArray();
    });

    public Task RecordPrintAsync(string orderId,bool copy,string status) => Locked(() =>
    {
        using var connection=Open(); using var command=Command(connection,null,"INSERT INTO print_attempts VALUES($id,$order,$time,$copy,$status)",
            ("$id",Guid.NewGuid().ToString("N")),("$order",orderId),("$time",DateTimeOffset.UtcNow.ToString("O")),("$copy",copy?1:0),("$status",status));
        command.ExecuteNonQuery(); return true;
    });

    private async Task<T> Locked<T>(Func<T> action)
    {
        await gate.WaitAsync();
        try { return await Task.Run(action); } finally { gate.Release(); }
    }
    private SqliteConnection Open()
    {
        var connection=new SqliteConnection(connectionString); connection.Open();
        using var command=connection.CreateCommand(); command.CommandText="PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL;"; command.ExecuteNonQuery();
        return connection;
    }
    private static SqliteCommand Command(SqliteConnection connection,SqliteTransaction? tx,string sql,params (string Key,object Value)[] values)
    {
        var command=connection.CreateCommand(); command.Transaction=tx; command.CommandText=sql;
        foreach(var item in values) command.Parameters.AddWithValue(item.Key,item.Value);
        return command;
    }
    private static Order? Load(SqliteConnection connection,SqliteTransaction? tx,string id)
    {
        using var command=Command(connection,tx,"SELECT payload FROM orders WHERE id=$id",("$id",id));
        return command.ExecuteScalar() is string payload ? JsonSerializer.Deserialize<Order>(payload,Json) : null;
    }
    private static Payment? ReadPayment(SqliteConnection connection,SqliteTransaction? tx,string id)
    {
        using var command=Command(connection,tx,"SELECT payload FROM payments WHERE order_id=$id",("$id",id));
        return command.ExecuteScalar() is string payload ? JsonSerializer.Deserialize<Payment>(payload,Json) : null;
    }
    private static void CheckVersion(Order order,Order? existing)
    {
        if((existing?.Version??0)!=order.Version) throw new InvalidOperationException("Versi pesanan sudah berubah. Muat ulang pesanan.");
    }
    private static Order Write(SqliteConnection connection,SqliteTransaction tx,Order order,Payment? payment)
    {
        var stored=order with { Version=checked(order.Version+1),UpdatedAt=DateTimeOffset.UtcNow };
        using var command=Command(connection,tx,"INSERT INTO orders VALUES($id,$version,$status,$time,$payload) ON CONFLICT(id) DO UPDATE SET version=excluded.version,status=excluded.status,updated_at=excluded.updated_at,payload=excluded.payload",
            ("$id",stored.Id),("$version",stored.Version),("$status",(int)stored.Status),("$time",stored.UpdatedAt.ToString("O")),("$payload",JsonSerializer.Serialize(stored,Json)));
        command.ExecuteNonQuery();
        using var outbox=Command(connection,tx,"INSERT INTO outbox(id,aggregate_id,version,payload,created_at) VALUES($id,$aggregate,$version,$payload,$time)",
            ("$id",$"{stored.Id}:{stored.Version}"),("$aggregate",stored.Id),("$version",stored.Version),
            ("$payload",JsonSerializer.Serialize(new { order=stored,payment },Json)),("$time",stored.UpdatedAt.ToString("O")));
        outbox.ExecuteNonQuery(); return stored;
    }
}

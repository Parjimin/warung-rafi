using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using WarungRafi.Core;

namespace WarungRafi.Storage;

public sealed partial class LocalStore
{
    public string DatabasePath=>databasePath;
    public bool HasManagerPin=>!string.IsNullOrWhiteSpace(managerPinHash);
    public Task VerifyManagerAsync(string pin)=>Locked(()=> {using var c=Open();VerifyManager(c,pin);return true;});
    public Task<string?> SettingAsync(string key)=>Locked(()=> {using var c=Open();using var cmd=Command(c,null,"SELECT value FROM settings WHERE key=$key",("$key",key));return cmd.ExecuteScalar() as string;});
    public Task BindCloudAsync(string origin)=>Locked(()=>
    {
        using var c=Open();using var tx=c.BeginTransaction();using var read=Command(c,tx,"SELECT value FROM settings WHERE key='cloud_origin'");
        var existing=read.ExecuteScalar() as string;if(existing is not null&&existing!=origin)throw new InvalidOperationException("Database ini terhubung ke server lain. Jangan mengganti server tanpa pemindahan data yang diperiksa.");
        using var cmd=Command(c,tx,"INSERT OR IGNORE INTO settings VALUES('cloud_origin',$origin)",("$origin",origin));cmd.ExecuteNonQuery();tx.Commit();return true;
    });
    public Task BindDeviceAsync(string device)=>Locked(()=>
    {
        if(string.IsNullOrWhiteSpace(device)||device.Length>80)throw new InvalidDataException("Identitas laptop tidak valid.");
        using var c=Open();using var tx=c.BeginTransaction();using var read=Command(c,tx,"SELECT value FROM settings WHERE key='cloud_device'");
        if(read.ExecuteScalar() is string saved&&saved!=device)throw new InvalidOperationException("Identitas laptop di server berubah. Periksa konfigurasi sebelum melanjutkan.");
        using var write=Command(c,tx,"INSERT OR IGNORE INTO settings VALUES('cloud_device',$id)",("$id",device));write.ExecuteNonQuery();tx.Commit();return true;
    });
    public async Task<BackupInfo> BackupAsync(string destination,string password)
    {
        BackupArchive.ValidatePassword(password);destination=Path.GetFullPath(destination);
        if(File.Exists(destination))throw new IOException("File tujuan sudah ada. Gunakan nama baru agar backup lama tetap tersimpan.");
        var folder=BackupArchive.TemporaryFolder();var snapshot=Path.Combine(folder,"snapshot.db");var temp=destination+".partial-"+Guid.NewGuid().ToString("N");
        try
        {
            // Hold the store gate only while SQLite captures its consistent online snapshot.
            await Locked(()=> {using var c=Open();using var target=BackupConnection(snapshot,false);c.BackupDatabase(target);return true;});
            return await Task.Run(()=>
            {
                using(var c=BackupConnection(snapshot,false)) {using var mode=c.CreateCommand();mode.CommandText="PRAGMA journal_mode=DELETE";mode.ExecuteNonQuery();}
                var now=DateTimeOffset.UtcNow;var info=InspectBackup(snapshot,null,now,Path.GetFileName(destination));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                BackupArchive.Encrypt(snapshot,temp,password,info.DatabaseId,now);File.Move(temp,destination);using var saved=File.OpenRead(destination);return info with {ArchiveHash=Convert.ToHexString(SHA256.HashData(saved))};
            });
        }
        finally {if(File.Exists(temp))File.Delete(temp);BackupArchive.DeleteTemporary(folder);}
    }
    private static SqliteConnection BackupConnection(string path,bool readOnly=true)
    {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
        try {c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA trusted_schema=OFF; PRAGMA foreign_keys=ON";cmd.ExecuteNonQuery();return c;}catch{c.Dispose();throw;}
    }
    private static readonly Lazy<string> ExpectedSchema=new(()=>
    {
        var folder=BackupArchive.TemporaryFolder();
        try {var path=Path.Combine(folder,"schema.db");new LocalStore(path).InitializeAsync().GetAwaiter().GetResult();SqliteConnection.ClearAllPools();using var c=BackupConnection(path);return Schema(c);}
        finally {BackupArchive.DeleteTemporary(folder);}
    });
    private static string Schema(SqliteConnection c)
    {
        using var cmd=c.CreateCommand();cmd.CommandText="SELECT type,name,tbl_name,sql FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name";
        using var rows=cmd.ExecuteReader();var text=new StringBuilder();while(rows.Read())for(var i=0;i<4;i++)text.Append(rows.IsDBNull(i)?"":rows.GetString(i)).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
    internal static BackupInfo InspectBackup(string path,string? expectedId,DateTimeOffset created,string fileName)
    {
        using var c=BackupConnection(path);
        using var version=c.CreateCommand();version.CommandText="PRAGMA user_version";
        if(Convert.ToInt32(version.ExecuteScalar())!=2||Schema(c)!=ExpectedSchema.Value)throw new InvalidDataException("Struktur/versi database tidak sesuai aplikasi ini. Tidak ada data yang dipulihkan.");
        using var integrity=c.CreateCommand();integrity.CommandText="PRAGMA integrity_check";
        if(integrity.ExecuteScalar() as string!="ok")throw new InvalidDataException("Pemeriksaan integritas backup gagal.");
        using var foreign=c.CreateCommand();foreign.CommandText="PRAGMA foreign_key_check";using(var r=foreign.ExecuteReader())if(r.Read())throw new InvalidDataException("Relasi database backup tidak utuh.");
        using var id=Command(c,null,"SELECT value FROM settings WHERE key='database_id'");var databaseId=id.ExecuteScalar() as string;
        if(databaseId is null||!Guid.TryParseExact(databaseId,"N",out _)||(expectedId is not null&&databaseId!=expectedId))throw new InvalidDataException("Identitas database backup tidak cocok.");
        long Count(string sql) {using var cmd=Command(c,null,sql);return Convert.ToInt64(cmd.ExecuteScalar());}
        if(Count("SELECT COUNT(*) FROM payments p LEFT JOIN orders o ON o.id=p.order_id WHERE o.status IS NULL OR o.status<>2")!=0||Count("SELECT COUNT(*) FROM orders o LEFT JOIN payments p ON p.order_id=o.id WHERE o.status=2 AND p.order_id IS NULL")!=0)throw new InvalidDataException("Pesanan dan pembayaran backup tidak cocok.");
        if(Count("SELECT COUNT(*) FROM finance_events")!=Count("SELECT COALESCE(MAX(sequence),0) FROM finance_events"))throw new InvalidDataException("Jurnal keuangan backup terputus.");
        if(Count("SELECT COUNT(*) FROM finance_events")!=Count("SELECT COUNT(*) FROM finance_outbox"))throw new InvalidDataException("Antrean jurnal backup tidak lengkap.");
        try
        {
            using var cmd=Command(c,null,"SELECT o.id,o.version,o.status,o.payload,p.payload FROM orders o LEFT JOIN payments p ON p.order_id=o.id");
            using var rows=cmd.ExecuteReader();
            while(rows.Read())
            {
                var order=JsonSerializer.Deserialize<Order>(rows.GetString(3),Json)!;OrderRules.Validate(order);
                if(order.Id!=rows.GetString(0)||order.Version!=rows.GetInt64(1)||(int)order.Status!=rows.GetInt32(2))throw new InvalidDataException("Isi pesanan backup tidak cocok dengan indeksnya.");
                if(!rows.IsDBNull(4))
                {
                    var payment=JsonSerializer.Deserialize<Payment>(rows.GetString(4),Json)??throw new InvalidDataException("Pembayaran kosong.");
                    if(payment.OrderId!=order.Id||payment.Amount!=order.Total||!Enum.IsDefined(payment.Method)||payment.Tendered<payment.Amount||payment.Change!=checked(payment.Tendered-payment.Amount)||(payment.Method==PaymentMethod.QrisManual&&payment.Change!=0))throw new InvalidDataException("Nominal pembayaran backup tidak cocok.");
                }
            }
        }
        catch(Exception error) when(error is JsonException or ArgumentException or OverflowException){throw new InvalidDataException("Isi transaksi backup tidak valid.",error);}
        return new(databaseId,created,Count("SELECT COUNT(*) FROM orders"),Count("SELECT COUNT(*) FROM payments"),Count("SELECT COUNT(*) FROM finance_events"),Count("SELECT (SELECT COUNT(*) FROM outbox WHERE acknowledged=0)+(SELECT COUNT(*) FROM finance_outbox WHERE acknowledged=0)"),fileName);
    }
    internal Task CheckpointForRestoreAsync()=>Locked(()=>
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA wal_checkpoint(TRUNCATE)";using(var r=cmd.ExecuteReader())if(!r.Read()||r.GetInt64(0)!=0)throw new IOException("Database masih dipakai. Tutup semua aplikasi lalu coba lagi.");
        return true;
    });
    internal static Task PrepareRestoredAsync(string path)=>Task.Run(()=>
    {
        using var c=BackupConnection(path,false);using var tx=c.BeginTransaction();using var cmd=Command(c,tx,"""
            INSERT INTO settings VALUES('recovery_required','1') ON CONFLICT(key) DO UPDATE SET value='1';
            INSERT INTO settings VALUES('sync_recheck','1') ON CONFLICT(key) DO UPDATE SET value='1';
            UPDATE outbox SET acknowledged=0; UPDATE finance_outbox SET acknowledged=0;
            """);cmd.ExecuteNonQuery();tx.Commit();
    });
    public Task CompleteOfflineRecoveryAsync()=>Locked(()=>
    {
        using var c=Open();using var tx=c.BeginTransaction();using var read=Command(c,tx,"SELECT value FROM settings WHERE key='cloud_origin'");
        if(read.ExecuteScalar() is string)throw new InvalidOperationException("Database pernah terhubung cloud. Periksa kecocokan server sebelum memakai kembali kasir.");
        using var cmd=Command(c,tx,"DELETE FROM settings WHERE key='recovery_required'");cmd.ExecuteNonQuery();tx.Commit();return true;
    });
    public Task ValidateRecoveryPageAsync(JsonElement page)=>Locked(()=>
    {
        using var c=Open();
        foreach(var order in page.GetProperty("orders").EnumerateArray())
        {
            var id=order.GetProperty("id").GetString()!;var version=order.GetProperty("version").GetInt64();
            using var cmd=Command(c,null,"SELECT payload FROM outbox WHERE aggregate_id=$id AND version=$version",("$id",id),("$version",version));
            var payload=cmd.ExecuteScalar() as string;
            if(payload is null||!JsonNode.DeepEquals(JsonNode.Parse(payload),JsonNode.Parse(order.GetProperty("payload").GetRawText())))throw new InvalidOperationException("Cloud memuat pesanan yang tidak ada atau berbeda dalam backup. Gunakan backup lebih baru; jangan melanjutkan dengan data tertimpa.");
        }
        var finance=page.GetProperty("finance");
        if(finance.ValueKind!=JsonValueKind.Null)
        {
            using var cmd=Command(c,null,"SELECT payload FROM finance_events WHERE sequence=$seq",("$seq",finance.GetProperty("sequence").GetInt64()));var payload=cmd.ExecuteScalar() as string;
            if(payload is null||!JsonNode.DeepEquals(JsonNode.Parse(payload),JsonNode.Parse(finance.GetProperty("payload").GetRawText())))throw new InvalidOperationException("Jurnal cloud lebih baru atau berbeda dari backup. Gunakan backup yang sesuai sebelum berjualan kembali.");
        }
        return true;
    });
    public Task CompleteCloudRecoveryAsync()=>Locked(()=>
    {using var c=Open();using var cmd=Command(c,null,"DELETE FROM settings WHERE key IN ('recovery_required','sync_recheck')");cmd.ExecuteNonQuery();return true;});
}

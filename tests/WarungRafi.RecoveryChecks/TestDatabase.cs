using Microsoft.Data.Sqlite;
using WarungRafi.Core;
using WarungRafi.Storage;

namespace WarungRafi.RecoveryChecks;

internal sealed class TestDatabase : IDisposable
{
    private readonly string folder=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"WarungRafiRecovery-"+Guid.NewGuid().ToString("N"));
    public string Path { get; }
    public LocalStore Store { get; }
    public static Product Product => new("nasi-uji","Nasi Uji","Nasi",8000);
    public TestDatabase()
    {
        Directory.CreateDirectory(folder);
        Path=System.IO.Path.Combine(folder,"test.db"); Store=new LocalStore(Path);
    }
    public async Task<Order> DraftAsync() => await Store.SaveAsync(OrderRules.Add(Order.New(),Product));
    public async Task<CompletedSale> SaleAsync()
    {
        var order=await DraftAsync();
        return await Store.CompleteAsync(order.Id,order.Version,PaymentMethod.Cash,10000);
    }
    public SqliteConnection Connect()
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path,Pooling=false }.ToString());
        connection.Open();return connection;
    }
    public void Execute(string sql)
    {
        using var connection=Connect();Execute(connection,sql);
    }
    public static void Execute(SqliteConnection connection,string sql)
    {
        using var command=connection.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery();
    }
    public object? Scalar(string sql)
    {
        using var connection=Connect();using var command=connection.CreateCommand();command.CommandText=sql;return command.ExecuteScalar();
    }
    public bool IntegrityOk()
    {
        using var connection=Connect();using var command=connection.CreateCommand();
        command.CommandText="PRAGMA integrity_check";
        if(!Equals(command.ExecuteScalar(),"ok"))return false;
        command.CommandText="PRAGMA foreign_key_check";
        using var reader=command.ExecuteReader();return !reader.Read();
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder,true);
    }
}

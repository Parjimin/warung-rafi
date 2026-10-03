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
}

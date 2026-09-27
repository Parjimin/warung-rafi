using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace WarungRafi.Storage;

public sealed record BackupInfo(string DatabaseId,DateTimeOffset CreatedAt,long Orders,long Payments,long FinanceEvents,long Pending,string FileName,string ArchiveHash="");

// One lease for the cashier and maintenance tools, held for the entire process lifetime.
public sealed class DatabaseLease : IDisposable
{
    private FileStream? handle;
    public string DatabasePath { get; }
    private DatabaseLease(string path,FileStream file) { DatabasePath=path;handle=file; }
    public static DatabaseLease Acquire(string path)
    {
        path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new(path,new FileStream(path+".app-lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)); }
        catch(IOException) { throw new InvalidOperationException("Kasir masih terbuka. Tutup semua jendela Warung Rafi sebelum membuka pemulihan."); }
    }
    internal void Ensure(string path)
    { if(handle is null||Path.GetFullPath(path)!=DatabasePath)throw new InvalidOperationException("Pemulihan harus memegang kunci aplikasi."); }
    public void Dispose() { handle?.Dispose();handle=null; }
}

public static class BackupArchive
{
    // v1: fixed 80-byte authenticated header, 64KiB AES-GCM records with unique nonces.
    // Header: magic, salt(16), nonce prefix(8), length(8), UTC ticks(8), database ID(32).
    private const int HeaderSize=80,Chunk=65536;
    private const long Maximum=2L*1024*1024*1024;
    private static readonly byte[] Magic="WRBACK01"u8.ToArray();
    public static void ValidatePassword(string password)
    { if(password.Length is <12 or >256)throw new ArgumentException("Kata sandi backup harus 12–256 karakter. Simpan terpisah dari laptop."); }
    internal static void Encrypt(string source,string destination,string password,string databaseId,DateTimeOffset created)
    {
        ValidatePassword(password);
        if(!Guid.TryParseExact(databaseId,"N",out _))throw new InvalidDataException("Identitas database tidak valid.");
        using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length is <1 or >Maximum)throw new InvalidDataException("Ukuran backup melebihi batas 2 GB.");
        var header=new byte[HeaderSize];Magic.CopyTo(header,0);RandomNumberGenerator.Fill(header.AsSpan(8,24));
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(32),input.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(40),created.UtcTicks);
        Encoding.ASCII.GetBytes(databaseId).CopyTo(header,48);
        var key=Rfc2898DeriveBytes.Pbkdf2(password,header.AsSpan(8,16),600000,HashAlgorithmName.SHA256,32);
        try
        {
            using var aes=new AesGcm(key,16);using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            output.Write(header);var plain=new byte[Chunk];var cipher=new byte[Chunk];var tag=new byte[16];var nonce=new byte[12];var aad=new byte[HeaderSize+4];header.CopyTo(aad,0);header.AsSpan(24,8).CopyTo(nonce);
            for(uint index=0;input.Position<input.Length;index++)
            {
                var count=(int)Math.Min(Chunk,input.Length-input.Position);input.ReadExactly(plain.AsSpan(0,count));
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8),index);BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(HeaderSize),index);
                aes.Encrypt(nonce,plain.AsSpan(0,count),cipher.AsSpan(0,count),tag,aad);output.Write(cipher,0,count);output.Write(tag);
            }
            CryptographicOperations.ZeroMemory(plain);output.Flush(true);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static (string Id,DateTimeOffset Created,string Hash) Decrypt(string source,string destination,string password)
    {
        ValidatePassword(password);
        using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
        var header=new byte[HeaderSize];input.ReadExactly(header);
        if(!header.AsSpan(0,8).SequenceEqual(Magic))throw new InvalidDataException("Ini bukan file backup Warung Rafi yang didukung.");
        var length=BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(32));
        if(length is <1 or >Maximum||input.Length!=HeaderSize+length+((length+Chunk-1)/Chunk)*16)throw new InvalidDataException("File backup terpotong atau tidak valid.");
        var id=Encoding.ASCII.GetString(header,48,32);
        if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("Identitas backup tidak valid.");
        var created=new DateTimeOffset(BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(40)),TimeSpan.Zero);
        var key=Rfc2898DeriveBytes.Pbkdf2(password,header.AsSpan(8,16),600000,HashAlgorithmName.SHA256,32);
        try
        {
            using var aes=new AesGcm(key,16);using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var plain=new byte[Chunk];var cipher=new byte[Chunk];var tag=new byte[16];var nonce=new byte[12];var aad=new byte[HeaderSize+4];header.CopyTo(aad,0);header.AsSpan(24,8).CopyTo(nonce);
            for(uint index=0;output.Position<length;index++)
            {
                var count=(int)Math.Min(Chunk,length-output.Position);input.ReadExactly(cipher.AsSpan(0,count));input.ReadExactly(tag);
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8),index);BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(HeaderSize),index);
                aes.Decrypt(nonce,cipher.AsSpan(0,count),tag,plain.AsSpan(0,count),aad);output.Write(plain,0,count);
            }
            CryptographicOperations.ZeroMemory(plain);output.Flush(true);input.Position=0;return(id,created,Convert.ToHexString(SHA256.HashData(input)));
        }
        catch(CryptographicException) { throw new InvalidDataException("Kata sandi salah atau isi backup telah berubah. Database belum diganti."); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static async Task<BackupInfo> InspectAsync(string file,string password)
    {
        var folder=TemporaryFolder();var path=Path.Combine(folder,"snapshot.db");
        try
        {
            var meta=await Task.Run(()=>Decrypt(file,path,password));
            return await Task.Run(()=>LocalStore.InspectBackup(path,meta.Id,meta.Created,Path.GetFileName(file)) with {ArchiveHash=meta.Hash});
        }
        finally { DeleteTemporary(folder); }
    }
    public static async Task<BackupInfo> RestoreAsync(string file,string password,string databasePath,DatabaseLease lease,string safetyFolder,bool allowUnreadableCurrent=false,string? expectedHash=null)
    {
        lease.Ensure(databasePath);databasePath=Path.GetFullPath(databasePath);
        // Stage beside the target so replacement stays on one filesystem.
        var folder=Path.Combine(Path.GetDirectoryName(databasePath)!,"restore-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var staged=Path.Combine(folder,"snapshot.db");
        try
        {
            var meta=await Task.Run(()=>Decrypt(file,staged,password));
            if(expectedHash is not null&&meta.Hash!=expectedHash)throw new InvalidDataException("File backup berubah setelah diperiksa. Periksa kembali sebelum memulihkan.");
            var info=await Task.Run(()=>LocalStore.InspectBackup(staged,meta.Id,meta.Created,Path.GetFileName(file)) with {ArchiveHash=meta.Hash});
            Directory.CreateDirectory(safetyFolder);
            if(File.Exists(databasePath))
            {
                try
                {
                    var current=new LocalStore(databasePath);
                    await current.BackupAsync(Path.Combine(safetyFolder,"before-restore-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".wrbackup"),password);
                    // An older backup must not forget that this database has since connected.
                    if(await current.SettingAsync("cloud_origin") is string origin)
                    {
                        if(await current.SettingAsync("database_id")!=info.DatabaseId)throw new InvalidOperationException("Identitas backup berbeda dari database yang terhubung cloud.");
                        var restored=new LocalStore(staged);await restored.BindCloudAsync(origin);
                        if(await current.SettingAsync("cloud_device") is string device)await restored.BindDeviceAsync(device);
                    }
                    await current.CheckpointForRestoreAsync();
                }
                catch(Exception error) when(error is SqliteException or InvalidDataException)
                {
                    if(!allowUnreadableCurrent)throw new InvalidOperationException("Database saat ini tidak dapat diperiksa. Pilih pemulihan database rusak setelah menutup kasir; berkas asli akan disimpan terpisah.",error);
                    // Preserve every byte of a corrupt database and WAL. Never delete them.
                    var saved=Path.Combine(safetyFolder,"unreadable-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(saved);
                    foreach(var suffix in new[]{"","-wal","-shm"})if(File.Exists(databasePath+suffix))File.Copy(databasePath+suffix,Path.Combine(saved,"warung-rafi.db"+suffix));
                }
            }
            await LocalStore.PrepareRestoredAsync(staged);
            SqliteConnection.ClearAllPools();
            // No app writer exists while the lease is held; move sidecars away before replacement.
            // If interrupted here, source main is checkpointed (healthy) or remains preserved (corrupt).
            foreach(var suffix in new[]{"-wal","-shm"})if(File.Exists(databasePath+suffix))File.Move(databasePath+suffix,Path.Combine(folder,"previous"+suffix));
            var rollback=databasePath+".before-restore-"+Guid.NewGuid().ToString("N");
            if(File.Exists(databasePath))File.Replace(staged,databasePath,rollback);else File.Move(staged,databasePath);
            // Keep rollback and corrupt safety copies; they are never silently overwritten.
            return info;
        }
        finally { DeleteTemporary(folder); }
    }
    internal static string TemporaryFolder()
    { var dir=Path.Combine(Path.GetTempPath(),"WarungRafi-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(dir,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);return dir; }
    internal static void DeleteTemporary(string dir) { try { Directory.Delete(dir,true); } catch(IOException) { } catch(UnauthorizedAccessException) { } }
}

namespace WarungRafi.Storage;

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
        catch(IOException) { throw new InvalidOperationException("Aplikasi Warung Rafi sudah terbuka. Gunakan jendela yang sedang berjalan."); }
    }
    public void Dispose() { handle?.Dispose();handle=null; }
}


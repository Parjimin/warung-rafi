using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WarungRafi.Core;
using WarungRafi.Storage;

namespace WarungRafi.Desktop;

internal sealed record DesktopSettings(string ApiOrigin="",string DeviceToken="",string PrinterName="",string ManagerPinHash="",int PinFailures=0,DateTimeOffset? PinBlockedUntil=null,string DirectSheetsJson="");
internal sealed class SettingsFile(string path)
{
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("WarungRafi.Settings.v1");
    internal string FilePath=>path;
    public DesktopSettings Load()
    {
        if(!File.Exists(path))return new();
        try
        {
            var plain=ProtectedData.Unprotect(File.ReadAllBytes(path),Entropy,DataProtectionScope.CurrentUser);
            try {return JsonSerializer.Deserialize<DesktopSettings>(plain)??throw new InvalidDataException();}
            finally {CryptographicOperations.ZeroMemory(plain);}
        }
        catch(Exception e) when(e is CryptographicException or JsonException or InvalidDataException) {throw new InvalidDataException("Pengaturan tidak dapat dibuka oleh akun Windows ini. Database tetap aman; gunakan akun yang membuat pengaturan atau pulihkan bersama pengelola.");}
    }
    public void Save(DesktopSettings settings)
    {
        Validate(settings);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var plain=JsonSerializer.SerializeToUtf8Bytes(settings);byte[] encoded;
        try {encoded=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);}finally{CryptographicOperations.ZeroMemory(plain);}
        var temp=path+".tmp-"+Guid.NewGuid().ToString("N");
        try {using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(encoded);stream.Flush(true);}File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static string NormalizeOrigin(string address)
    {
        if(string.IsNullOrWhiteSpace(address))return "";
        if(!Uri.TryCreate(address,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||uri.AbsolutePath!="/"||uri.Query.Length>0||uri.Fragment.Length>0)throw new ArgumentException("Isi alamat HTTPS admin saja, tanpa path, nama pengguna, atau parameter.");
        return uri.GetLeftPart(UriPartial.Authority)+"/";
    }
    public static void Validate(DesktopSettings settings)
    {
        var origin=NormalizeOrigin(settings.ApiOrigin);
        if(origin.Length>0&&(settings.DeviceToken.Length is <32 or >512||settings.DeviceToken.Any(char.IsWhiteSpace)))throw new ArgumentException("Token laptop harus 32–512 karakter tanpa spasi.");
        if(settings.PrinterName.Length>256)throw new ArgumentException("Nama printer terlalu panjang.");
    }
    public DesktopSettings Authorize(string pin)
    {
        var settings=Load();if(settings.ManagerPinHash.Length==0)throw new InvalidOperationException("Buat PIN pengelola terlebih dahulu.");
        if(settings.PinBlockedUntil>DateTimeOffset.UtcNow)throw new InvalidOperationException("Terlalu banyak PIN salah. Tunggu lima menit.");
        var valid=ManagerPin.Verify(settings.ManagerPinHash,pin);var failures=valid?0:settings.PinFailures+1;
        settings=settings with {PinFailures=failures>=5?0:failures,PinBlockedUntil=failures>=5?DateTimeOffset.UtcNow.AddMinutes(5):valid?null:settings.PinBlockedUntil};Save(settings);
        if(!valid)throw new InvalidOperationException("PIN pengelola tidak sesuai.");return settings;
    }
    public DesktopSettings ImportEnvironment()
    {
        if(File.Exists(path))return Load();
        var settings=new DesktopSettings(Environment.GetEnvironmentVariable("WARUNG_API_BASE_URL")??"",Environment.GetEnvironmentVariable("WARUNG_DEVICE_TOKEN")??"",Environment.GetEnvironmentVariable("WARUNG_PRINTER_NAME")??"",Environment.GetEnvironmentVariable("WARUNG_MANAGER_PIN_HASH")??"");
        settings=settings with {ApiOrigin=NormalizeOrigin(settings.ApiOrigin)};
        if(settings!=new DesktopSettings())Save(settings);return settings;
    }
}

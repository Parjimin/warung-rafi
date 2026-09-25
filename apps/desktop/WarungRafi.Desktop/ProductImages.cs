using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WarungRafi.Desktop;

internal static class ProductImages
{
    private static readonly HttpClient Http=new() { Timeout=TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string,Task<ImageSource?>> Pending=new();
    public static async Task<ImageSource?> GetAsync(string url)
    {
        var result=await Pending.GetOrAdd(url,key=>LoadAsync(key));
        if(result is null)Pending.TryRemove(url,out _);
        return result;
    }
    public static async Task PrefetchAsync(IEnumerable<string> urls,CancellationToken token)
    {
        foreach(var url in urls.Distinct())
        {
            if(token.IsCancellationRequested)return;
            await GetAsync(url);
        }
    }
    internal static async Task<ImageSource?> LoadAsync(string url,string? cacheFolder=null,HttpClient? client=null)
    {
        try
        {
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https")return null;
            var folder=cacheFolder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WarungRafi","images");
            Directory.CreateDirectory(folder);
            var path=Path.Combine(folder,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".img");
            byte[] bytes;
            if(File.Exists(path)) bytes=await File.ReadAllBytesAsync(path);
            else
            {
                using var response=await (client??Http).GetAsync(uri,HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength>5_000_000)return null;
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);
                using var memory=new MemoryStream();var buffer=new byte[8192];int read;
                while((read=await stream.ReadAsync(buffer,timeout.Token))>0)
                {
                    if(memory.Length+read>5_000_000)return null;
                    memory.Write(buffer,0,read);
                }
                bytes=memory.ToArray();
            }
            var image=await Task.Run(()=>
            {
                using var stream=new MemoryStream(bytes);
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth=320;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
            });
            if(!File.Exists(path))
            {
                var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try { await File.WriteAllBytesAsync(temp,bytes);File.Move(temp,path,true); }
                finally { if(File.Exists(temp))File.Delete(temp); }
            }
            return image;
        }
        catch { return null; }
    }
}

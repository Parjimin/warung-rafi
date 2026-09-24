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
    public static Task<ImageSource?> GetAsync(string url) => Pending.GetOrAdd(url,LoadAsync);
    private static async Task<ImageSource?> LoadAsync(string url)
    {
        try
        {
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https")return null;
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WarungRafi","images");
            Directory.CreateDirectory(folder);
            var path=Path.Combine(folder,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".img");
            byte[] bytes;
            if(File.Exists(path)) bytes=await File.ReadAllBytesAsync(path);
            else
            {
                using var response=await Http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead);
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
            if(!File.Exists(path))await File.WriteAllBytesAsync(path,bytes);
            return image;
        }
        catch { Pending.TryRemove(url,out _);return null; }
    }
}
